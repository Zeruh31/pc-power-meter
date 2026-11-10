using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Win32;
using PcPowerMeter.Core;
using PcPowerMeter.Sensors;

namespace PcPowerMeter.Services;

/// <summary>What was detected at start-up.</summary>
internal sealed record SensorInfo(PowerSource GpuSource, string? GpuName, PowerSource CpuSource, string? CpuName);

/// <summary>State of CSV logging, reported to the UI.</summary>
internal sealed record CsvStatus(bool Active, string? FilePath, string? Error);

/// <summary>Options for testing the "hardware missing" paths on a machine that has it.</summary>
internal sealed record SensorOptions(bool DisableGpu, bool DisableCpuMeter);

/// <summary>
/// Runs sensor reads and CSV writes on a dedicated background thread so the UI never
/// waits on WMI, NVML or the disk. Events are raised on that thread.
/// </summary>
internal sealed class SamplerService : IDisposable
{
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly SensorOptions _options;
    private readonly Thread _thread;
    private volatile bool _stopping;

    // Owned by the sampler thread.
    private AppSettings _settings;
    private CsvLogger? _csv;
    private PowerMeterEngine? _engine;
    private readonly Stopwatch _clock = new();
    private readonly GapDetector _gaps = new();
    private double _nextTick;
    private double _lastTick;

    public SamplerService(AppSettings settings, SensorOptions options)
    {
        _settings = settings.Normalized();
        _options = options;
        _thread = new Thread(Run) { IsBackground = true, Name = "Power sampler" };
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public event Action<SensorInfo>? SensorsReady;
    public event Action<PowerSample, SessionSnapshot>? SampleReady;
    public event Action<CsvStatus>? CsvStatusChanged;

    public void Start() => _thread.Start();

    /// <summary>Applies new settings on the sampler thread.</summary>
    public void UpdateSettings(AppSettings settings)
    {
        AppSettings copy = settings.Normalized();
        Post(() =>
        {
            AppSettings old = _settings;
            _settings = copy;
            if (copy.IntervalSeconds != old.IntervalSeconds)
            {
                // The running interval was scheduled with the old setting; judge it by both.
                _gaps.IntervalChanged(old.IntervalSeconds, copy.IntervalSeconds);
                _nextTick = _lastTick + copy.IntervalSeconds;
            }
            if (copy.CsvEnabled != old.CsvEnabled || ResolveCsvFolder(copy) != ResolveCsvFolder(old))
            {
                RestartCsv();
            }
        });
    }

    /// <summary>Clears the session statistics and starts a new CSV file if logging is on.</summary>
    public void ResetSession() => Post(() =>
    {
        _engine?.ResetSession();
        RestartCsv();
    });

    public static string DefaultCsvFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PC Power Meter");

    public static string ResolveCsvFolder(AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.CsvFolder) ? DefaultCsvFolder : settings.CsvFolder;

    /// <summary>
    /// After sleep or hibernation the stopwatch has kept counting: drop that interval and
    /// start again from fresh baselines instead of adding hours of made-up energy.
    /// </summary>
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Post(() =>
            {
                _engine?.Rebaseline();
                _lastTick = _clock.Elapsed.TotalSeconds;
                _nextTick = _lastTick + _settings.IntervalSeconds;
            });
        }
    }

    private void Post(Action command)
    {
        _commands.Enqueue(command);
        _wake.Set();
    }

    private void Run()
    {
        IGpuSensor? gpu = null;
        ICpuEnergySensor? cpuEnergy = null;
        try
        {
            if (!_options.DisableGpu)
            {
                gpu = (IGpuSensor?)NvmlGpuSensor.TryCreate() ?? NvidiaSmiGpuSensor.TryCreate();
            }
            if (!_options.DisableCpuMeter)
            {
                cpuEnergy = new EnergyMeterCpuSensor();
            }
            _engine = new PowerMeterEngine(gpu, cpuEnergy, new SystemTimesCpuLoadSensor());
            _engine.Start();
            SensorsReady?.Invoke(new SensorInfo(
                _engine.GpuSource, gpu?.Name, _engine.CpuSource, SystemTimesCpuLoadSensor.ProcessorName()));

            RestartCsv();
            Loop();
        }
        finally
        {
            _csv?.Dispose();
            gpu?.Dispose();
            cpuEnergy?.Dispose();
        }
    }

    private void Loop()
    {
        _clock.Restart();
        _lastTick = 0;
        _nextTick = _settings.IntervalSeconds;
        while (!_stopping)
        {
            while (_commands.TryDequeue(out Action? command))
            {
                command();
            }
            double now = _clock.Elapsed.TotalSeconds;
            double remaining = _nextTick - now;
            if (remaining > 0)
            {
                _wake.WaitOne(TimeSpan.FromSeconds(Math.Min(remaining, 0.5)));
                continue;
            }

            double dt = now - _lastTick;
            _lastTick = now;
            _nextTick += _settings.IntervalSeconds;
            if (_nextTick < now)
            {
                _nextTick = now + _settings.IntervalSeconds; // fell behind: resync
            }
            if (dt <= 0)
            {
                continue;
            }

            MeterSettings meter = _settings.ToMeterSettings();
            if (_gaps.IsGap(dt, meter.IntervalSeconds))
            {
                // Sleep, hibernation or a stalled read: measure nothing for this span.
                _engine!.Rebaseline();
                continue;
            }

            (PowerSample sample, SessionSnapshot session) = _engine!.Tick(dt, meter, DateTime.Now);
            WriteCsv(sample);
            SampleReady?.Invoke(sample, session);
        }
    }

    private void RestartCsv()
    {
        _csv?.Dispose();
        _csv = null;
        if (!_settings.CsvEnabled)
        {
            CsvStatusChanged?.Invoke(new CsvStatus(false, null, null));
            return;
        }
        try
        {
            _csv = CsvLogger.Open(ResolveCsvFolder(_settings), DateTime.Now);
            CsvStatusChanged?.Invoke(new CsvStatus(true, _csv.FilePath, null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            CsvStatusChanged?.Invoke(new CsvStatus(false, null, ex.Message));
        }
    }

    private void WriteCsv(PowerSample sample)
    {
        if (_csv is null)
        {
            return;
        }
        try
        {
            _csv.Write(sample);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            string path = _csv.FilePath;
            _csv.Dispose();
            _csv = null;
            CsvStatusChanged?.Invoke(new CsvStatus(false, path, ex.Message));
        }
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _stopping = true;
        _wake.Set();
        bool stopped = !_thread.IsAlive || _thread.Join(TimeSpan.FromSeconds(5));
        if (stopped)
        {
            _wake.Dispose();
        }
    }
}
