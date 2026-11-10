namespace PcPowerMeter.Core;

/// <summary>Immutable view of a <see cref="RunningStat"/>.</summary>
public readonly record struct StatSnapshot(double? Now, double? Average, double? Max, double EnergyWh, double Seconds);

/// <summary>
/// Running statistics for one power row. Each row keeps its own covered time, so the
/// average is energy / time over exactly the intervals that row measured.
/// </summary>
public sealed class RunningStat
{
    public double? Now { get; private set; }
    public double? Max { get; private set; }
    public double EnergyWh { get; private set; }
    public double Seconds { get; private set; }

    public double? Average => Seconds > 0 ? EnergyWh * 3600.0 / Seconds : null;

    /// <summary>Records a power value and the energy and time it covers.</summary>
    public void Add(double watts, double energyWh, double seconds)
    {
        Now = watts;
        EnergyWh += energyWh;
        Seconds += seconds;
        if (Max is null || watts > Max)
        {
            Max = watts;
        }
    }

    /// <summary>Marks the current value as unavailable without touching the totals.</summary>
    public void MarkMissing() => Now = null;

    public void Reset()
    {
        Now = null;
        Max = null;
        EnergyWh = 0;
        Seconds = 0;
    }

    public StatSnapshot Snapshot() => new(Now, Average, Max, EnergyWh, Seconds);
}
