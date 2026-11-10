namespace PcPowerMeter.Core;

/// <summary>Where a power value comes from.</summary>
public enum PowerSource
{
    /// <summary>No value could be obtained.</summary>
    None,
    /// <summary>NVIDIA Management Library (nvml.dll), board power.</summary>
    Nvml,
    /// <summary>nvidia-smi command line tool (fallback when nvml.dll cannot be loaded).</summary>
    NvidiaSmi,
    /// <summary>Windows Energy Meter counters (CPU RAPL package energy).</summary>
    EnergyMeter,
    /// <summary>Estimated from CPU load and the configured TDP.</summary>
    LoadEstimate,
    /// <summary>A fixed, user-configured value.</summary>
    Fixed,
}

public static class PowerSourceExtensions
{
    /// <summary>True when the value is read from hardware rather than estimated.</summary>
    public static bool IsMeasured(this PowerSource source) =>
        source is PowerSource.Nvml or PowerSource.NvidiaSmi or PowerSource.EnergyMeter;
}

/// <summary>Board power of all detected GPUs (summed) and their average load.</summary>
public readonly record struct GpuReading(double Watts, double? LoadPercent);

/// <summary>Reads GPU power. Implementations return null when the value is unavailable.</summary>
public interface IGpuSensor : IDisposable
{
    PowerSource Source { get; }
    string? Name { get; }
    GpuReading? Read();
}

/// <summary>
/// A raw CPU energy counter sample: cumulative energy in picowatt-hours and the
/// meter's own timestamp in milliseconds.
/// </summary>
public readonly record struct EnergySnapshot(double EnergyPwh, double TimeMs);

/// <summary>Reads the cumulative CPU package energy counter. Returns null when unavailable.</summary>
public interface ICpuEnergySensor : IDisposable
{
    EnergySnapshot? Read();
}

/// <summary>Cumulative system CPU times (100 ns units); Total includes idle time.</summary>
public readonly record struct CpuTimes(long Idle, long Total);

/// <summary>Reads cumulative CPU times for the load percentage. Returns null on failure.</summary>
public interface ICpuLoadSensor
{
    CpuTimes? Read();
}
