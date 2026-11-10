using PcPowerMeter.Core;

namespace PcPowerMeter.Tests;

public class RunningStatTests
{
    [Fact]
    public void Empty_HasNoValues()
    {
        var stat = new RunningStat();

        Assert.Null(stat.Now);
        Assert.Null(stat.Average);
        Assert.Null(stat.Max);
        Assert.Equal(0, stat.EnergyWh);
    }

    [Fact]
    public void Average_IsEnergyOverCoveredTime_NotMeanOfSamples()
    {
        var stat = new RunningStat();
        stat.Add(100, PowerMath.WattHours(100, 1), 1); // 1 s at 100 W
        stat.Add(200, PowerMath.WattHours(200, 3), 3); // 3 s at 200 W

        Assert.Equal(175.0, stat.Average!.Value, 9);    // (100 + 600) / 4
        Assert.Equal(200.0, stat.Max!.Value, 9);
        Assert.Equal(200.0, stat.Now!.Value, 9);
        Assert.Equal(700.0 / 3600, stat.EnergyWh, 12);
    }

    [Fact]
    public void Max_KeepsHighestValue()
    {
        var stat = new RunningStat();
        foreach (double w in new[] { 80.0, 310.0, 120.0 })
        {
            stat.Add(w, PowerMath.WattHours(w, 1), 1);
        }

        Assert.Equal(310.0, stat.Max!.Value);
        Assert.Equal(120.0, stat.Now!.Value);
    }

    [Fact]
    public void MarkMissing_ClearsNowButKeepsTotals()
    {
        var stat = new RunningStat();
        stat.Add(150, PowerMath.WattHours(150, 2), 2);
        stat.MarkMissing();

        Assert.Null(stat.Now);
        Assert.Equal(150.0, stat.Average!.Value, 9);
        Assert.Equal(150.0, stat.Max!.Value);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var stat = new RunningStat();
        stat.Add(150, 1, 2);
        stat.Reset();

        Assert.Null(stat.Now);
        Assert.Null(stat.Max);
        Assert.Null(stat.Average);
        Assert.Equal(0, stat.EnergyWh);
        Assert.Equal(0, stat.Seconds);
    }
}
