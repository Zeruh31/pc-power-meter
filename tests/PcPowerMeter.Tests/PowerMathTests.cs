using PcPowerMeter.Core;

namespace PcPowerMeter.Tests;

public class PowerMathTests
{
    [Fact]
    public void EnergyDelta_ConvertsPicowattHoursAndMilliseconds()
    {
        var prev = new EnergySnapshot(EnergyPwh: 1e15, TimeMs: 10_000);
        var cur = new EnergySnapshot(EnergyPwh: 1e15 + Energy.Pwh(50, 2), TimeMs: 12_000);

        EnergyDelta d = PowerMath.CpuEnergyDelta(prev, cur);

        Assert.Equal(EnergyDeltaKind.Valid, d.Kind);
        Assert.Equal(2.0, d.Seconds, 9);
        Assert.Equal(50.0 * 2 / 3600, d.EnergyWh, 9);
        Assert.Equal(50.0, d.Watts!.Value, 6);
    }

    [Fact]
    public void EnergyDelta_SameTimestamp_IsStale()
    {
        var prev = new EnergySnapshot(100, 5000);
        EnergyDelta d = PowerMath.CpuEnergyDelta(prev, prev);

        Assert.Equal(EnergyDeltaKind.Stale, d.Kind);
        Assert.Null(d.Watts);
    }

    [Theory]
    [InlineData(1000, 5000, 900, 6000)]  // energy went backwards
    [InlineData(1000, 5000, 1100, 4000)] // time went backwards
    public void EnergyDelta_CounterGoingBackwards_IsReset(double e0, double t0, double e1, double t1)
    {
        EnergyDelta d = PowerMath.CpuEnergyDelta(new EnergySnapshot(e0, t0), new EnergySnapshot(e1, t1));

        Assert.Equal(EnergyDeltaKind.Reset, d.Kind);
        Assert.Equal(0, d.EnergyWh);
    }

    [Fact]
    public void WattHours_IsWattsTimesHours()
    {
        Assert.Equal(1.0, PowerMath.WattHours(3600, 1), 12);
        Assert.Equal(100.0, PowerMath.WattHours(100, 3600), 12);
    }

    [Fact]
    public void WallWatts_DividesByPsuEfficiency()
    {
        Assert.Equal(300.0, PowerMath.WallWatts(270, 0.90), 9);
        Assert.Equal(270.0, PowerMath.WallWatts(270, 1.0), 9);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    [InlineData(1.01)]
    public void WallWatts_RejectsInvalidEfficiency(double efficiency)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PowerMath.WallWatts(100, efficiency));
    }

    [Fact]
    public void CpuLoadPercent_UsesIdleShareOfTotal()
    {
        var prev = new CpuTimes(Idle: 1000, Total: 2000);
        var cur = new CpuTimes(Idle: 1075, Total: 2100); // 75 of 100 ticks idle

        Assert.Equal(25.0, PowerMath.CpuLoadPercent(prev, cur)!.Value, 9);
        Assert.Null(PowerMath.CpuLoadPercent(cur, cur));
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(50, 75)]
    [InlineData(100, 125)]
    [InlineData(150, 125)] // clamped
    public void EstimateCpuWatts_InterpolatesBetweenIdleAndTdp(double load, double expected)
    {
        Assert.Equal(expected, PowerMath.EstimateCpuWatts(load, idleWatts: 25, tdpWatts: 125), 9);
    }
}
