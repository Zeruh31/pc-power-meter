namespace PcPowerMeter.Core;

/// <summary>Result of comparing two energy counter samples.</summary>
public enum EnergyDeltaKind
{
    /// <summary>Energy and time both advanced; the delta is usable.</summary>
    Valid,
    /// <summary>The meter has not updated yet (same timestamp); keep the old baseline.</summary>
    Stale,
    /// <summary>The counter went backwards (reset or wrap); restart from the new sample.</summary>
    Reset,
}

public readonly record struct EnergyDelta(EnergyDeltaKind Kind, double EnergyWh, double Seconds)
{
    /// <summary>Average power over the delta, or null when the delta is not valid.</summary>
    public double? Watts => Kind == EnergyDeltaKind.Valid && Seconds > 0 ? EnergyWh * 3600.0 / Seconds : null;
}

/// <summary>Pure power and energy arithmetic, kept separate so it can be unit tested.</summary>
public static class PowerMath
{
    public const double PicowattHoursPerWattHour = 1e12;

    /// <summary>
    /// Energy and elapsed time between two cumulative counter samples. Power is
    /// delta-energy over delta-time, using the meter's own timestamps.
    /// </summary>
    public static EnergyDelta CpuEnergyDelta(EnergySnapshot previous, EnergySnapshot current)
    {
        double dE = current.EnergyPwh - previous.EnergyPwh;
        double dT = (current.TimeMs - previous.TimeMs) / 1000.0;
        if (dE < 0 || dT < 0)
        {
            return new EnergyDelta(EnergyDeltaKind.Reset, 0, 0);
        }
        if (dT == 0)
        {
            // A stale snapshot: keeping the baseline lets the next delta cover this
            // interval as well, so nothing is lost or counted twice.
            return new EnergyDelta(EnergyDeltaKind.Stale, 0, 0);
        }
        return new EnergyDelta(EnergyDeltaKind.Valid, dE / PicowattHoursPerWattHour, dT);
    }

    /// <summary>Energy in watt-hours for a constant power over a number of seconds.</summary>
    public static double WattHours(double watts, double seconds) => watts * seconds / 3600.0;

    /// <summary>Estimated wall (AC) power from DC power and PSU efficiency (0..1].</summary>
    public static double WallWatts(double dcWatts, double psuEfficiency)
    {
        if (psuEfficiency <= 0 || psuEfficiency > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(psuEfficiency), psuEfficiency, "PSU efficiency must be in (0, 1].");
        }
        return dcWatts / psuEfficiency;
    }

    /// <summary>CPU load in percent between two CPU time samples, or null if undefined.</summary>
    public static double? CpuLoadPercent(CpuTimes previous, CpuTimes current)
    {
        long dTotal = current.Total - previous.Total;
        if (dTotal <= 0)
        {
            return null;
        }
        double idleShare = (double)(current.Idle - previous.Idle) / dTotal;
        return Math.Clamp(100.0 * (1.0 - idleShare), 0.0, 100.0);
    }

    /// <summary>Load-based CPU estimate: idle + load x (TDP - idle).</summary>
    public static double EstimateCpuWatts(double loadPercent, double idleWatts, double tdpWatts)
    {
        double load = Math.Clamp(loadPercent, 0.0, 100.0) / 100.0;
        return idleWatts + load * (tdpWatts - idleWatts);
    }
}
