using PcPowerMeter.Core;

namespace PcPowerMeter.Tests;

public class PowerMeterEngineTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);
    private static readonly MeterSettings Settings = new() { PsuEfficiency = 0.90, OtherWatts = 40 };

    /// <summary>Energy counter samples for consecutive 1-second intervals at the given powers.</summary>
    private static EnergySnapshot?[] CpuCounter(params double[] watts)
    {
        var list = new List<EnergySnapshot?>();
        double e = 5e14, t = 1_000_000;
        list.Add(new EnergySnapshot(e, t)); // baseline read by Start()
        foreach (double w in watts)
        {
            e += Energy.Pwh(w, 1);
            t += 1000;
            list.Add(new EnergySnapshot(e, t));
        }
        return list.ToArray();
    }

    [Fact]
    public void MeasuredCpu_PowerIsEnergyDeltaOverTimeDelta()
    {
        var engine = new PowerMeterEngine(null, new FakeCpuEnergy(CpuCounter(40, 60)), null);
        engine.Start();

        engine.Tick(1, Settings, T0);
        var (sample, session) = engine.Tick(1, Settings, T0.AddSeconds(1));

        Assert.Equal(PowerSource.EnergyMeter, engine.CpuSource);
        Assert.Equal(60.0, sample.CpuWatts!.Value, 6);
        Assert.Equal(50.0, session.Cpu.Average!.Value, 6);
        Assert.Equal(60.0, session.Cpu.Max!.Value, 6);
        Assert.Equal(100.0 / 3600, session.Cpu.EnergyWh, 9);
    }

    [Fact]
    public void NoGpu_TotalsExcludeGpu_AndGpuIsNeverZeroWatts()
    {
        var engine = new PowerMeterEngine(null, new FakeCpuEnergy(CpuCounter(50, 50)), null);
        engine.Start();

        engine.Tick(1, Settings, T0);
        var (sample, session) = engine.Tick(1, Settings, T0.AddSeconds(1));

        Assert.Null(sample.GpuWatts);
        Assert.False(sample.GpuIncluded);
        Assert.Equal(PowerSource.None, sample.GpuSource);
        Assert.Equal(50 + 40, sample.DcWatts, 6);
        Assert.Equal(2, session.GpuMissingSamples);
        Assert.Null(session.Gpu.Now);
        Assert.Null(session.Gpu.Average); // no GPU time covered, so no average either
    }

    [Fact]
    public void GpuReadFailure_LeavesThatSampleOutOfGpuStats()
    {
        var gpu = new FakeGpu(new GpuReading(200, 90), null, new GpuReading(200, 95));
        var engine = new PowerMeterEngine(gpu, new FakeCpuEnergy(CpuCounter(50, 50, 50)), null);
        engine.Start();

        var first = engine.Tick(1, Settings, T0);
        var middle = engine.Tick(1, Settings, T0.AddSeconds(1));
        var (_, session) = engine.Tick(1, Settings, T0.AddSeconds(2));

        Assert.True(first.Sample.GpuIncluded);
        Assert.Equal(200 + 50 + 40, first.Sample.DcWatts, 6);
        Assert.False(middle.Sample.GpuIncluded);
        Assert.Equal(50 + 40, middle.Sample.DcWatts, 6);
        Assert.Equal(1, session.GpuMissingSamples);
        Assert.Equal(200.0, session.Gpu.Average!.Value, 6);
        Assert.Equal(2.0, session.Gpu.Seconds, 9);
    }

    [Fact]
    public void Wall_IsDcDividedByPsuEfficiency()
    {
        var engine = new PowerMeterEngine(new FakeGpu(new GpuReading(300, 99)), new FakeCpuEnergy(CpuCounter(100)), null);
        engine.Start();

        var (sample, session) = engine.Tick(1, new MeterSettings { PsuEfficiency = 0.88, OtherWatts = 40 }, T0);

        Assert.Equal(440.0, sample.DcWatts, 6);
        Assert.Equal(500.0, sample.WallWatts, 6);
        Assert.Equal(500.0 / 3600, session.Wall.EnergyWh, 9);
        Assert.Equal(0.88, sample.PsuEfficiency, 9);
    }

    [Fact]
    public void WattHours_AccumulateOverTicks()
    {
        var gpu = new FakeGpu(new GpuReading(100, 0), new GpuReading(100, 0), new GpuReading(100, 0));
        var engine = new PowerMeterEngine(gpu, new FakeCpuEnergy(CpuCounter(60, 60, 60)), null);
        engine.Start();

        SessionSnapshot? session = null;
        for (int i = 0; i < 3; i++)
        {
            session = engine.Tick(1, Settings, T0.AddSeconds(i)).Session;
        }

        Assert.Equal(300.0 / 3600, session!.Gpu.EnergyWh, 9);
        Assert.Equal(180.0 / 3600, session.Cpu.EnergyWh, 9);
        Assert.Equal(120.0 / 3600, session.Other.EnergyWh, 9);
        Assert.Equal(600.0 / 3600, session.Dc.EnergyWh, 9);
        Assert.Equal(600.0 / 3600 / 0.9, session.Wall.EnergyWh, 9);
        Assert.Equal(200.0, session.Dc.Average!.Value, 6);
        Assert.Equal(3, session.Samples);
        Assert.Equal(3.0, session.ElapsedSeconds, 9);
    }

    [Fact]
    public void StaleCpuCounter_ShowsLastValue_AndNextDeltaCoversBothIntervals()
    {
        var c = CpuCounter(40);
        EnergySnapshot last = c[1]!.Value;
        var after = new EnergySnapshot(last.EnergyPwh + Energy.Pwh(80, 2), last.TimeMs + 2000);
        var engine = new PowerMeterEngine(null, new FakeCpuEnergy(c[0], c[1], last, after), null);
        engine.Start();

        engine.Tick(1, Settings, T0);
        var stale = engine.Tick(1, Settings, T0.AddSeconds(1));
        var (sample, session) = engine.Tick(1, Settings, T0.AddSeconds(2));

        Assert.Equal(40.0, stale.Sample.CpuWatts!.Value, 6);           // last value shown
        Assert.Equal(40.0 / 3600, stale.Session.Cpu.EnergyWh, 9);       // nothing added
        Assert.Equal(80.0, sample.CpuWatts!.Value, 6);
        Assert.Equal((40.0 + 160.0) / 3600, session.Cpu.EnergyWh, 9); // no energy lost
        Assert.Equal(3.0, session.Cpu.Seconds, 9);
    }

    [Fact]
    public void NoEnergyCounter_UsesLoadEstimate()
    {
        var load = new FakeCpuLoad(new CpuTimes(0, 0), new CpuTimes(50, 100));
        var engine = new PowerMeterEngine(null, new FakeCpuEnergy(/* always null */), load);
        engine.Start();

        var settings = new MeterSettings { CpuIdleWatts = 25, CpuTdpWatts = 125, OtherWatts = 40 };
        var (sample, _) = engine.Tick(1, settings, T0);

        Assert.Equal(PowerSource.LoadEstimate, engine.CpuSource);
        Assert.Equal(PowerSource.LoadEstimate, sample.CpuSource);
        Assert.Equal(50.0, sample.CpuLoadPercent!.Value, 6);
        Assert.Equal(75.0, sample.CpuWatts!.Value, 6);
    }

    [Fact]
    public void NoCpuValueYet_CpuIsLeftOutOfTotals()
    {
        var c = CpuCounter();
        var engine = new PowerMeterEngine(null, new FakeCpuEnergy(c[0], c[0]), null); // second read is stale
        engine.Start();

        var (sample, session) = engine.Tick(1, Settings, T0);

        Assert.Null(sample.CpuWatts);
        Assert.False(sample.CpuIncluded);
        Assert.Equal(40.0, sample.DcWatts, 6);
        Assert.Null(session.Cpu.Now);
    }

    [Fact]
    public void ResetSession_ClearsStatistics()
    {
        var engine = new PowerMeterEngine(new FakeGpu(new GpuReading(100, 0), new GpuReading(150, 0)),
            new FakeCpuEnergy(CpuCounter(50, 50)), null);
        engine.Start();
        engine.Tick(1, Settings, T0);

        engine.ResetSession();
        var (sample, session) = engine.Tick(1, Settings, T0.AddSeconds(1));

        Assert.Equal(1, session.Samples);
        Assert.Equal(1.0, sample.ElapsedSeconds, 9);
        Assert.Equal(150.0, session.Gpu.Max!.Value, 6);
        Assert.Equal(150.0 / 3600, session.Gpu.EnergyWh, 9);
    }

    [Theory]
    [InlineData(1.0, 1.0, false)]
    [InlineData(1.0, 4.9, false)]   // below the 5 s floor, e.g. a slow first WMI query
    [InlineData(1.0, 6.0, true)]
    [InlineData(1.0, 8 * 3600.0, true)] // a night of sleep
    [InlineData(10.0, 29.0, false)]
    [InlineData(10.0, 31.0, true)]  // more than 3 intervals
    public void IsGap_FlagsIntervalsFarLongerThanRequested(double interval, double dt, bool expected)
    {
        Assert.Equal(expected, PowerMeterEngine.IsGap(dt, new MeterSettings { IntervalSeconds = interval }));
    }

    [Fact]
    public void Rebaseline_DropsTheGap_IncludingCpuEnergyUsedDuringIt()
    {
        var c = CpuCounter(50);
        EnergySnapshot beforeSleep = c[1]!.Value;
        // Eight hours pass; the counter keeps running (100 W while "asleep").
        var afterSleep = new EnergySnapshot(beforeSleep.EnergyPwh + Energy.Pwh(100, 8 * 3600), beforeSleep.TimeMs + 8 * 3600_000.0);
        var next = new EnergySnapshot(afterSleep.EnergyPwh + Energy.Pwh(50, 1), afterSleep.TimeMs + 1000);
        var engine = new PowerMeterEngine(null, new FakeCpuEnergy(c[0], c[1], afterSleep, next), null);
        engine.Start();

        engine.Tick(1, Settings, T0);
        engine.Rebaseline();
        var (sample, session) = engine.Tick(1, Settings, T0.AddHours(8));

        Assert.Equal(50.0, sample.CpuWatts!.Value, 6);
        Assert.Equal(100.0 / 3600, session.Cpu.EnergyWh, 9); // 2 x 50 W x 1 s, no sleep energy
        Assert.Equal(2.0, session.Cpu.Seconds, 9);
        Assert.Equal(2.0, session.ElapsedSeconds, 9);
        Assert.Equal(50.0 + 40.0, session.Dc.Average!.Value, 6);
        Assert.Equal(1, session.SkippedGaps);
    }

    [Fact]
    public void GpuMissingIntervals_AreCountedWithTheirDuration()
    {
        var gpu = new FakeGpu(new GpuReading(200, 0), null, null);
        var engine = new PowerMeterEngine(gpu, new FakeCpuEnergy(CpuCounter(50, 50, 50)), null);
        engine.Start();

        engine.Tick(1, Settings, T0);
        engine.Tick(2, Settings, T0.AddSeconds(1));
        var (_, session) = engine.Tick(0.5, Settings, T0.AddSeconds(3));

        Assert.Equal(2, session.GpuMissingSamples);
        Assert.Equal(2.5, session.GpuMissingSeconds, 9);
        Assert.Equal(3.5, session.Dc.Seconds, 9);
    }

    [Fact]
    public void ResetSession_ChangesSessionId_AndClearsGapCounters()
    {
        var engine = new PowerMeterEngine(null, new FakeCpuEnergy(CpuCounter(50, 50, 50)), null);
        engine.Start();
        int before = engine.Tick(1, Settings, T0).Session.SessionId;
        engine.Rebaseline();

        engine.ResetSession();
        SessionSnapshot after = engine.Tick(1, Settings, T0.AddSeconds(1)).Session;

        Assert.Equal(before + 1, after.SessionId);
        Assert.Equal(0, after.SkippedGaps);
        Assert.Equal(1.0, after.GpuMissingSeconds, 9); // only the new interval (no GPU sensor)
    }

    [Fact]
    public void Tick_BeforeStart_Throws()
    {
        var engine = new PowerMeterEngine(null, null, null);
        Assert.Throws<InvalidOperationException>(() => engine.Tick(1, Settings, T0));
    }
}
