using PcPowerMeter.Core;

namespace PcPowerMeter.Tests;

public sealed class ComponentAndMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pcpm-tests-" + Guid.NewGuid().ToString("N"));

    public ComponentAndMigrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ------------------------------------------------------------------ component estimate

    [Fact]
    public void DefaultEstimate_IsTheSumOfItsTypicalValues()
    {
        var c = new ComponentEstimate();

        // board 20 + 2 x DDR5 4 + 3 fans x 1.5 + air cooler 2 + 1 NVMe 2 + USB 2.5
        Assert.Equal(20 + 8 + 4.5 + 2 + 2 + 2.5, c.TotalWatts, 9);
        Assert.Equal(c.TotalWatts, c.Breakdown().Sum(i => i.Watts), 9);
    }

    [Fact]
    public void CustomEstimate_MultipliesCountsByUnitWatts()
    {
        var c = new ComponentEstimate
        {
            CaseFans = 6, CaseFanWatts = 2,
            Cooler = ComponentEstimate.CoolerAio, CoolerWatts = 5,
            NvmeDrives = 2, NvmeWatts = 2.5,
            SataSsds = 1, SataSsdWatts = 1,
            Hdds = 2, HddWatts = 6,
            RamModules = 4, RamType = ComponentEstimate.RamDdr4, RamModuleWatts = 3,
            Board = ComponentEstimate.BoardHighEnd, BoardWatts = 30,
            Rgb = ComponentEstimate.RgbMany, RgbWatts = 12,
            UsbWatts = 3, ExtraWatts = 7,
        };

        Assert.Equal(12 + 5 + 5 + 1 + 12 + 12 + 30 + 12 + 3 + 7, c.TotalWatts, 9);
        Assert.Equal(12, c.Breakdown().Single(i => i.Key == "CaseFans").Watts, 9);
        Assert.Equal(4, c.Breakdown().Single(i => i.Key == "Ram").Count);
    }

    [Fact]
    public void Estimate_IsClampedIntoValidRanges()
    {
        var c = new ComponentEstimate
        {
            CaseFans = -3, Hdds = 1000, HddWatts = double.NaN, ExtraWatts = -50, Cooler = "liquid-nitrogen",
        }.Normalized();

        Assert.Equal(0, c.CaseFans);
        Assert.Equal(ComponentEstimate.MaxCount, c.Hdds);
        Assert.Equal(6.0, c.HddWatts);
        Assert.Equal(0, c.ExtraWatts);
        Assert.Equal(ComponentEstimate.CoolerAir, c.Cooler);
    }

    [Fact]
    public void ComponentMode_FeedsTheEngine()
    {
        var s = new AppSettings { OtherMode = AppSettings.OtherComponents, Components = new ComponentEstimate { ExtraWatts = 10 } };

        Assert.Equal(49, s.ToMeterSettings().OtherWatts, 9);
        Assert.Equal(40, new AppSettings { OtherMode = AppSettings.OtherFixed }.ToMeterSettings().OtherWatts);
    }

    // ------------------------------------------------------------------ settings migration

    [Fact]
    public void Version1File_KeepsItsFixedOtherValue()
    {
        // A settings.json as written by 0.1.0: no version, no mode, no components.
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """
            {
              "intervalSeconds": 1,
              "psuEfficiency": 0.88,
              "otherWatts": 55,
              "cpuTdpWatts": 120,
              "cpuIdleWatts": 25,
              "language": "tr",
              "csvEnabled": false,
              "closeToTray": true
            }
            """);

        AppSettings s = SettingsStore.Load(path);

        Assert.Equal(AppSettings.OtherFixed, s.OtherMode);
        Assert.Equal(55, s.OtherWatts);
        Assert.Equal(55, s.ToMeterSettings().OtherWatts);
        Assert.Equal(AppSettings.CurrentVersion, s.SettingsVersion);
        Assert.Equal(0.88, s.PsuEfficiency);
        Assert.False(s.OnlineTariffUpdate);
    }

    [Fact]
    public void MigratedFile_StaysMigrated_AndAComponentChoiceIsKept()
    {
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "otherWatts": 55 }""");
        AppSettings migrated = SettingsStore.Load(path);
        migrated.OtherMode = AppSettings.OtherComponents; // the user switches in Settings
        Assert.True(SettingsStore.Save(migrated, path));

        AppSettings reloaded = SettingsStore.Load(path);

        Assert.Equal(AppSettings.OtherComponents, reloaded.OtherMode);
        Assert.Equal(55, reloaded.OtherWatts); // the old value is still there to switch back to
    }

    [Fact]
    public void NewInstallation_UsesTheComponentEstimate_AndNoNetwork()
    {
        AppSettings s = SettingsStore.Load(Path.Combine(_dir, "missing.json"));

        Assert.Equal(AppSettings.OtherComponents, s.OtherMode);
        Assert.False(s.OnlineTariffUpdate);
        Assert.Null(s.TariffId);
    }

    [Fact]
    public void NewSettings_RoundTripThroughJson()
    {
        string path = Path.Combine(_dir, "settings.json");
        var s = new AppSettings
        {
            Components = new ComponentEstimate { Hdds = 3, RamType = ComponentEstimate.RamDdr4 },
            TariffId = "tr-residential-three-time", TariffTier = "high",
            ManualCurrency = "try", ManualTimeOfUse = true, ManualDayPrice = 5, ManualPeakPrice = 7, ManualNightPrice = 3,
            OnlineTariffUpdate = true,
        };

        Assert.True(SettingsStore.Save(s, path));
        AppSettings r = SettingsStore.Load(path);

        Assert.Equal(3, r.Components.Hdds);
        Assert.Equal(ComponentEstimate.RamDdr4, r.Components.RamType);
        Assert.Equal("tr-residential-three-time", r.TariffId);
        Assert.Equal("high", r.TariffTier);
        Assert.Equal("TRY", r.ManualCurrency);
        Assert.Equal(7, r.ManualPeakPrice);
        Assert.True(r.OnlineTariffUpdate);
        Assert.DoesNotContain("totalWatts", File.ReadAllText(path));
    }

    [Fact]
    public void Clone_DoesNotShareTheComponentEstimate()
    {
        var a = new AppSettings();
        AppSettings b = a.Clone();
        b.Components.Hdds = 4;

        Assert.Equal(0, a.Components.Hdds);
    }

    // ------------------------------------------------------------------ usage report

    private static HourlyUsage Hour(DateTime h, double wallWh) => new(h, wallWh, wallWh * 0.9, 0, 0, 0, 3600, 0);

    [Fact]
    public void Projection_UsesTheLastSevenDays()
    {
        var now = new DateTime(2026, 6, 20, 15, 0, 0);
        var hours = new List<HourlyUsage>
        {
            Hour(new DateTime(2026, 6, 1, 10, 0, 0), 99000), // older than 7 days: ignored
            Hour(new DateTime(2026, 6, 14, 10, 0, 0), 7000),
            Hour(new DateTime(2026, 6, 20, 10, 0, 0), 7000),
        };

        MonthlyProjection p = UsageReport.Projection(hours, now, PriceSchedule.Flat("EUR", 0.5))!;

        // Six full days (14-19 June) and 15 of today's 24 hours.
        Assert.Equal(7, p.Days);
        Assert.Equal(6.625, p.EffectiveDays, 9);
        Assert.Equal(14.0 / 6.625 * UsageReport.DaysPerMonth, p.WallKwh, 9);
        Assert.Equal(p.WallKwh * 0.5, p.Cost!.Value, 9);
    }

    [Fact]
    public void Projection_YoungHistory_AveragesOverItsOwnDays()
    {
        var now = new DateTime(2026, 6, 20, 15, 0, 0);
        var hours = new List<HourlyUsage> { Hour(new DateTime(2026, 6, 19, 10, 0, 0), 2000) };

        MonthlyProjection p = UsageReport.Projection(hours, now, null)!;

        Assert.Equal(2, p.Days);
        Assert.Equal(1.625, p.EffectiveDays, 9);
        Assert.Equal(2.0 / 1.625 * UsageReport.DaysPerMonth, p.WallKwh, 9);
        Assert.Null(p.Cost);
        Assert.Null(UsageReport.Projection(new List<HourlyUsage>(), now, null));
    }

    [Fact]
    public void Projection_PartialToday_IsNotCountedAsAFullDay_ButTheSpanIsAtLeastOneDay()
    {
        var hours = new List<HourlyUsage> { Hour(new DateTime(2026, 6, 20, 0, 0, 0), 1000) };

        MonthlyProjection early = UsageReport.Projection(hours, new DateTime(2026, 6, 20, 0, 30, 0), null)!;
        MonthlyProjection evening = UsageReport.Projection(hours, new DateTime(2026, 6, 20, 18, 0, 0), null)!;

        Assert.Equal(1.0, early.EffectiveDays, 9);   // not 1 kWh in half an hour x 48
        Assert.Equal(1.0, evening.EffectiveDays, 9);
        Assert.Equal(1.0 * UsageReport.DaysPerMonth, early.WallKwh, 9);
    }

    // ------------------------------------------------------------------ data folder and instance lock

    [Theory]
    [InlineData(@"C:\PcpmData", @"C:\PcpmData\")]
    [InlineData(@"C:\PcpmData", @"c:\pcpmdata")]
    [InlineData(@"C:\PcpmData", @"C:\PcpmData\sub\..")]
    [InlineData(@"C:\PcpmData", @"C:/PcpmData/")]
    public void SameFolder_SpelledDifferently_GetsTheSameLock(string a, string b)
    {
        string lockA = DataPaths.InstanceLockName(Path.Combine(DataPaths.NormalizeFolder(a), UsageHistory.FileName));
        string lockB = DataPaths.InstanceLockName(Path.Combine(DataPaths.NormalizeFolder(b), UsageHistory.FileName));

        Assert.Equal(lockA, lockB);
        Assert.StartsWith(@"Local\PcPowerMeter.History.", lockA);
    }

    [Fact]
    public void DifferentFolders_GetDifferentLocks()
    {
        Assert.NotEqual(
            DataPaths.InstanceLockName(Path.Combine(@"C:\PcpmA", UsageHistory.FileName)),
            DataPaths.InstanceLockName(Path.Combine(@"C:\PcpmB", UsageHistory.FileName)));
    }

    [Fact]
    public void NormalizeFolder_RemovesTheTrailingSeparator_AndResolvesRelativePaths()
    {
        Assert.Equal(@"C:\PcpmData", DataPaths.NormalizeFolder(@"C:\PcpmData\"));
        Assert.Equal(Path.Combine(Environment.CurrentDirectory, "x"), DataPaths.NormalizeFolder(@".\x\"));
        Assert.Equal(@"C:\", DataPaths.NormalizeFolder(@"C:\")); // a root keeps its separator
    }

    [Fact]
    public void NormalizeFolder_RejectsAQuoteLeftByCommandLineEscaping()
    {
        // --data-dir "C:\X\" --lang en arrives as one argument: C:\X" --lang en
        Assert.Throws<ArgumentException>(() => DataPaths.NormalizeFolder("C:\\X\" --lang en"));
        Assert.Throws<ArgumentException>(() => DataPaths.NormalizeFolder("C:\\X|Y"));
    }

    [Fact]
    public void DailyAndMonthly_IncludeEmptyBuckets()
    {
        var hours = new List<HourlyUsage>
        {
            Hour(new DateTime(2026, 6, 1, 10, 0, 0), 1000),
            Hour(new DateTime(2026, 6, 1, 11, 0, 0), 1000),
            Hour(new DateTime(2026, 6, 3, 11, 0, 0), 500),
            Hour(new DateTime(2026, 4, 3, 11, 0, 0), 500),
        };

        List<UsageBucket> days = UsageReport.Daily(hours, new DateTime(2026, 6, 1), new DateTime(2026, 6, 3), null);
        List<UsageBucket> months = UsageReport.Monthly(hours, new DateTime(2026, 6, 20), 3, null);

        Assert.Equal(new[] { 2000.0, 0, 500 }, days.Select(d => d.Totals.WallWh));
        Assert.Equal(new[] { 2500.0, 0, 500 }, months.Select(m => m.Totals.WallWh));
        Assert.Equal(new DateTime(2026, 4, 1), months[2].Start);
    }

    [Fact]
    public void WeekStart_FollowsTheCulturesFirstDay()
    {
        var wednesday = new DateTime(2026, 6, 17, 9, 0, 0);

        Assert.Equal(new DateTime(2026, 6, 15), UsageReport.WeekStart(wednesday, DayOfWeek.Monday));
        Assert.Equal(new DateTime(2026, 6, 14), UsageReport.WeekStart(wednesday, DayOfWeek.Sunday));
    }

    [Fact]
    public void Export_IsCultureIndependent_AndPriced()
    {
        var hours = new List<HourlyUsage> { Hour(new DateTime(2026, 6, 1, 10, 0, 0), 1500) };

        string hourly = UsageReport.ExportHourly(hours, PriceSchedule.Flat("TRY", 3.238));
        string daily = UsageReport.ExportDaily(hours, null);

        string[] hourlyLines = hourly.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(UsageReport.HourlyHeader, hourlyLines[0]);
        Assert.Equal("2026-06-01 10:00,1.5,1.35,0,0,0,1,0,3.238,4.857,TRY", hourlyLines[1]);
        Assert.Equal("2026-06-01,1.5,1.35,0,0,0,1,0,,", daily.Split("\r\n")[1]);
    }
}
