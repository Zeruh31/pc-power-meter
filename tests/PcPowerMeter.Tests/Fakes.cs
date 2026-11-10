using PcPowerMeter.Core;

namespace PcPowerMeter.Tests;

/// <summary>Returns queued readings in order; null entries simulate a failed read.</summary>
internal sealed class FakeGpu : IGpuSensor
{
    private readonly Queue<GpuReading?> _readings;

    public FakeGpu(params GpuReading?[] readings) => _readings = new Queue<GpuReading?>(readings);

    public PowerSource Source => PowerSource.Nvml;
    public string? Name => "Fake GPU";
    public GpuReading? Read() => _readings.Count > 0 ? _readings.Dequeue() : null;
    public void Dispose() { }
}

internal sealed class FakeCpuEnergy : ICpuEnergySensor
{
    private readonly Queue<EnergySnapshot?> _readings;

    public FakeCpuEnergy(params EnergySnapshot?[] readings) => _readings = new Queue<EnergySnapshot?>(readings);

    public EnergySnapshot? Read() => _readings.Count > 0 ? _readings.Dequeue() : null;
    public void Dispose() { }
}

internal sealed class FakeCpuLoad : ICpuLoadSensor
{
    private readonly Queue<CpuTimes?> _readings;

    public FakeCpuLoad(params CpuTimes?[] readings) => _readings = new Queue<CpuTimes?>(readings);

    public CpuTimes? Read() => _readings.Count > 0 ? _readings.Dequeue() : null;
}

internal static class Energy
{
    /// <summary>Picowatt-hours for a power held for a number of seconds.</summary>
    public static double Pwh(double watts, double seconds) => watts * seconds / 3600.0 * 1e12;
}
