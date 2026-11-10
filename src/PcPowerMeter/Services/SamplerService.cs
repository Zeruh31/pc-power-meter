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

/// <summary>State of the usage history file, reported to the UI; Error is null when all is well.</summary>
internal sealed record HistoryStatus(string? Error);

/// <summary>Options for testing the "hardware missing" paths on a machine that has it.</summary>
internal sealed record SensorOptions(bool DisableGpu, bool DisableCpuMeter);

/// <summary>
/// Runs sensor reads, CSV writes and usage history updates on a dedicated background
/// thread so the UI never waits on WMI, NVML or the disk. Events are raised on that thread.
/// </summary>
internal sealed class SamplerService : IDisposable
{
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly SensorOptions _options;
    private readonly UsageHistory _usage;
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
    private double _lastFlush;
    private string? _historyError;

    /// <summary>
    /// The usage history is written this often (and on exit and before sleep), so a crash or
    /// power cut loses at most this much.
    /// </summary>
    public const double HistoryFlushSeconds = 120;

    public SamplerService(AppSettings settings, SensorOptions options, UsageHistory usage)
    {
        _settings = settings.Normalized();
        _options = options;
        _usage = usage;
        _thread = new Thread(Run) { IsBackground = true, Name = "Power sampler" };
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public event Action<SensorInfo>? SensorsReady;
    public event Action<PowerSample, SessionSnapshot>? SampleReady;
    public event Action<CsvStatus>? CsvStatusChanged;
    public event Action<HistoryStatus>? HistoryStatusChanged;

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

    /// <summary>Longest wait for the history to reach the disk before Windows suspends.</summary>
    private static readonly TimeSpan SuspendFlushTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Before sleep the history is written synchronously (the system may not wake up again,
    /// e.g. a battery runs flat). After sleep or hibernation the stopwatch has kept counting:
    /// drop that interval and start again from fresh baselines instead of adding hours of
    /// made-up energy.
    /// </summary>
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            // UsageHistory is thread-safe, so this thread can write it directly instead of
            // waiting for the sampler thread, which may be busy with a slow sensor read.
            Task<bool> flush = Task.Run(_usage.Flush);
            bool done = flush.Wait(SuspendFlushTimeout);
            bool ok = done && flush.Result;
            Post(() => ReportHistory(ok || !done ? null : _usage.LastError ?? "?"));
        }
        else if (e.Mode == PowerModes.Resume)
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
            if (!_usage.Load())
            {
                ReportHistory(_usage.LastError);
            }
            Loop();
        }
        finally
        {
            FlushHistory();
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
            _usage.Add(sample.Timestamp, sample.Energy);
            SampleReady?.Invoke(sample, session);
            if (now - _lastFlush >= HistoryFlushSeconds)
            {
                _lastFlush = now;
                FlushHistory();
            }
        }
    }

    /// <summary>Writes the usage history; a failure is reported and retried at the next flush.</summary>
    private void FlushHistory()
    {
        ReportHistory(_usage.Flush() ? null : _usage.LastError ?? "?");
    }

    private void ReportHistory(string? error)
    {
        if (error != _historyError)
        {
            _historyError = error;
            HistoryStatusChanged?.Invoke(new HistoryStatus(error));
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
