namespace PcPowerMeter.Core;

/// <summary>One measurement interval.</summary>
/// <param name="GpuWatts">Null when the GPU could not be read; it is then left out of the totals.</param>
/// <param name="CpuWatts">Null only when the CPU counter has produced no value yet.</param>
public sealed record PowerSample(
    DateTime Timestamp,
    double ElapsedSeconds,
    double? GpuWatts,
    double? GpuLoadPercent,
    PowerSource GpuSource,
    double? CpuWatts,
    double? CpuLoadPercent,
    PowerSource CpuSource,
    double OtherWatts,
    double DcWatts,
    double WallWatts,
    double PsuEfficiency)
{
    /// <summary>True when the totals include a GPU reading.</summary>
    public bool GpuIncluded => GpuWatts.HasValue;

    /// <summary>True when the totals include a CPU value.</summary>
    public bool CpuIncluded => CpuWatts.HasValue;
}

/// <summary>Session statistics after a sample.</summary>
/// <param name="SessionId">Changes with every session reset.</param>
/// <param name="GpuMissingSamples">
/// Intervals in which the GPU could not be read. The DC and wall totals cover those
/// intervals too, but without any GPU energy.
/// </param>
/// <param name="GpuMissingSeconds">Total length of those intervals.</param>
/// <param name="SkippedGaps">Intervals dropped as gaps (sleep, hibernation, a stalled read).</param>
public sealed record SessionSnapshot(
    int SessionId,
    double ElapsedSeconds,
    int Samples,
    int GpuMissingSamples,
    double GpuMissingSeconds,
    int SkippedGaps,
    StatSnapshot Gpu,
    StatSnapshot Cpu,
    StatSnapshot Other,
    StatSnapshot Dc,
    StatSnapshot Wall);
