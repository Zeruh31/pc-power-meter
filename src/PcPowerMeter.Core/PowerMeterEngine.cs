namespace PcPowerMeter.Core;

/// <summary>
/// Turns raw sensor readings into power values and session statistics.
/// Not thread-safe: call it from a single sampling thread.
/// </summary>
public sealed class PowerMeterEngine
{
    private readonly IGpuSensor? _gpu;
    private readonly ICpuEnergySensor? _cpuEnergy;
    private readonly ICpuLoadSensor? _cpuLoad;

    private readonly RunningStat _gpuStat = new();
    private readonly RunningStat _cpuStat = new();
    private readonly RunningStat _otherStat = new();
    private readonly RunningStat _dcStat = new();
    private readonly RunningStat _wallStat = new();

    private EnergySnapshot? _prevEnergy;
    private CpuTimes? _prevTimes;
    private double _elapsed;
    private int _samples;
    private int _gpuMissing;
    private double _gpuMissingSeconds;
    private int _skippedGaps;
    private bool _started;

    /// <summary>An interval longer than this many sampling intervals is treated as a gap.</summary>
    public const double GapFactor = 3.0;

    /// <summary>Intervals up to this long are never treated as a gap (slow first WMI query etc.).</summary>
    public const double MinGapSeconds = 5.0;

    public PowerMeterEngine(IGpuSensor? gpu, ICpuEnergySensor? cpuEnergy, ICpuLoadSensor? cpuLoad)
    {
        _gpu = gpu;
        _cpuEnergy = cpuEnergy;
        _cpuLoad = cpuLoad;
    }

    /// <summary>GPU source, or <see cref="PowerSource.None"/> when there is no GPU sensor.</summary>
    public PowerSource GpuSource => _gpu?.Source ?? PowerSource.None;

    /// <summary>CPU source, decided by <see cref="Start"/>.</summary>
    public PowerSource CpuSource { get; private set; } = PowerSource.LoadEstimate;

    /// <summary>
    /// Takes the baselines. The CPU is measured when the energy counter answers here;
    /// otherwise the load-based estimate is used for the whole session.
    /// </summary>
    public void Start()
    {
        _prevEnergy = _cpuEnergy?.Read();
        CpuSource = _prevEnergy.HasValue ? PowerSource.EnergyMeter : PowerSource.LoadEstimate;
        _prevTimes = _cpuLoad?.Read();
        _started = true;
    }

    /// <summary>
    /// Increases with every <see cref="ResetSession"/>, so a consumer can drop samples that
    /// were produced before a reset but delivered after it.
    /// </summary>
    public int SessionId { get; private set; }

    /// <summary>
    /// True when an interval is far longer than requested, e.g. after sleep or hibernation,
    /// when the stopwatch keeps counting while nothing was measured.
    /// </summary>
    public static bool IsGap(double dtSeconds, MeterSettings settings) =>
        dtSeconds > Math.Max(GapFactor * settings.Normalized().IntervalSeconds, MinGapSeconds);

    /// <summary>
    /// Drops the interval that just passed: takes fresh sensor baselines (including the
    /// CPU energy counter) without adding anything to the statistics.
    /// </summary>
    public void Rebaseline()
    {
        if (!_started)
        {
            throw new InvalidOperationException("Call Start() before Rebaseline().");
        }
        if (CpuSource == PowerSource.EnergyMeter)
        {
            EnergySnapshot? cur = _cpuEnergy?.Read();
            if (cur.HasValue)
            {
                _prevEnergy = cur;
            }
        }
        CpuTimes? times = _cpuLoad?.Read();
        if (times.HasValue)
        {
            _prevTimes = times;
        }
        _skippedGaps++;
    }

    /// <summary>Clears the session statistics; sensor baselines are kept.</summary>
    public void ResetSession()
    {
        SessionId++;
        _gpuMissingSeconds = 0;
        _skippedGaps = 0;
        _gpuStat.Reset();
        _cpuStat.Reset();
        _otherStat.Reset();
        _dcStat.Reset();
        _wallStat.Reset();
        _elapsed = 0;
        _samples = 0;
        _gpuMissing = 0;
    }

    /// <summary>Processes one interval of <paramref name="dtSeconds"/> seconds.</summary>
    public (PowerSample Sample, SessionSnapshot Session) Tick(double dtSeconds, MeterSettings settings, DateTime timestamp)
    {
        if (!_started)
        {
            throw new InvalidOperationException("Call Start() before Tick().");
        }
        if (dtSeconds <= 0 || !double.IsFinite(dtSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(dtSeconds), dtSeconds, "Interval must be positive.");
        }
        settings = settings.Normalized();

        // CPU load (also feeds the estimate).
        double? cpuLoad = null;
        CpuTimes? times = _cpuLoad?.Read();
        if (times.HasValue && _prevTimes.HasValue)
        {
            cpuLoad = PowerMath.CpuLoadPercent(_prevTimes.Value, times.Value);
        }
        if (times.HasValue)
        {
            _prevTimes = times;
        }

        // GPU: null when it could not be read; then it is left out of the totals, never counted as 0 W.
        GpuReading? gpu = _gpu?.Read();
        double? gpuW = gpu?.Watts;
        double gpuWh = 0;
        if (gpuW.HasValue)
        {
            gpuWh = PowerMath.WattHours(gpuW.Value, dtSeconds);
            _gpuStat.Add(gpuW.Value, gpuWh, dtSeconds);
        }
        else
        {
            _gpuStat.MarkMissing();
            _gpuMissing++;
            _gpuMissingSeconds += dtSeconds;
        }

        // CPU.
        double? cpuW = null;
        double cpuWh = 0, cpuSec = 0;
        if (CpuSource == PowerSource.EnergyMeter)
        {
            EnergySnapshot? cur = _cpuEnergy?.Read();
            if (cur.HasValue && _prevEnergy.HasValue)
            {
                EnergyDelta delta = PowerMath.CpuEnergyDelta(_prevEnergy.Value, cur.Value);
                switch (delta.Kind)
                {
                    case EnergyDeltaKind.Valid:
                        cpuWh = delta.EnergyWh;
                        cpuSec = delta.Seconds;
                        cpuW = delta.Watts;
                        _prevEnergy = cur;
                        break;
                    case EnergyDeltaKind.Reset:
                        _prevEnergy = cur;
                        break;
                }
            }
            else if (cur.HasValue)
            {
                _prevEnergy = cur;
            }
            // No fresh reading: show the last value but add no energy or time.
            cpuW ??= _cpuStat.Now;
        }
        else
        {
            cpuW = PowerMath.EstimateCpuWatts(cpuLoad ?? 0, settings.CpuIdleWatts, settings.CpuTdpWatts);
            cpuWh = PowerMath.WattHours(cpuW.Value, dtSeconds);
            cpuSec = dtSeconds;
        }

        double otherW = settings.OtherWatts;
        double otherWh = PowerMath.WattHours(otherW, dtSeconds);
        double dcW = (gpuW ?? 0) + (cpuW ?? 0) + otherW;
        double dcWh = gpuWh + cpuWh + otherWh;
        double wallW = PowerMath.WallWatts(dcW, settings.PsuEfficiency);
        double wallWh = PowerMath.WallWatts(dcWh, settings.PsuEfficiency);

        _elapsed += dtSeconds;
        _samples++;
        if (cpuW.HasValue)
        {
            _cpuStat.Add(cpuW.Value, cpuWh, cpuSec);
        }
        _otherStat.Add(otherW, otherWh, dtSeconds);
        _dcStat.Add(dcW, dcWh, dtSeconds);
        _wallStat.Add(wallW, wallWh, dtSeconds);

        var sample = new PowerSample(
            timestamp, _elapsed,
            gpuW, gpu?.LoadPercent, gpuW.HasValue ? GpuSource : PowerSource.None,
            cpuW, cpuLoad, cpuW.HasValue ? CpuSource : PowerSource.None,
            otherW, dcW, wallW, settings.PsuEfficiency);
        return (sample, Snapshot());
    }

    public SessionSnapshot Snapshot() => new(
        SessionId, _elapsed, _samples, _gpuMissing, _gpuMissingSeconds, _skippedGaps,
        _gpuStat.Snapshot(), _cpuStat.Snapshot(), _otherStat.Snapshot(), _dcStat.Snapshot(), _wallStat.Snapshot());
}
