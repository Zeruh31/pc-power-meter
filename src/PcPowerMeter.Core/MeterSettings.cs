namespace PcPowerMeter.Core;

/// <summary>The settings the measurement engine needs, as an immutable snapshot.</summary>
public sealed record MeterSettings
{
    public const double MinInterval = 0.25, MaxInterval = 60;
    public const double MinPsuEfficiency = 0.5, MaxPsuEfficiency = 1.0;
    public const double MaxWatts = 2000;

    /// <summary>Sampling interval in seconds.</summary>
    public double IntervalSeconds { get; init; } = 1.0;

    /// <summary>PSU efficiency used for the wall power estimate (0.5..1.0).</summary>
    public double PsuEfficiency { get; init; } = 0.90;

    /// <summary>Estimated power of everything else inside the case, in watts.</summary>
    public double OtherWatts { get; init; } = 40;

    /// <summary>CPU TDP used by the load-based estimate when no energy counter exists.</summary>
    public double CpuTdpWatts { get; init; } = 120;

    /// <summary>CPU idle power used by the load-based estimate.</summary>
    public double CpuIdleWatts { get; init; } = 25;

    /// <summary>Returns a copy with every value forced into its valid range.</summary>
    public MeterSettings Normalized()
    {
        double tdp = Clamp(CpuTdpWatts, 1, MaxWatts, 120);
        return this with
        {
            IntervalSeconds = Clamp(IntervalSeconds, MinInterval, MaxInterval, 1.0),
            PsuEfficiency = Clamp(PsuEfficiency, MinPsuEfficiency, MaxPsuEfficiency, 0.90),
            OtherWatts = Clamp(OtherWatts, 0, MaxWatts, 40),
            CpuTdpWatts = tdp,
            CpuIdleWatts = Math.Min(Clamp(CpuIdleWatts, 0, MaxWatts, 25), tdp),
        };
    }

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
