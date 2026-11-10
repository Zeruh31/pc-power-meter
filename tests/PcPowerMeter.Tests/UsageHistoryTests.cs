using PcPowerMeter.Core;

namespace PcPowerMeter.Tests;

public sealed class UsageHistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pcpm-tests-" + Guid.NewGuid().ToString("N"));

    public UsageHistoryTests() => Directory.CreateDirectory(_dir);

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

    private string FilePath => Path.Combine(_dir, UsageHistory.FileName);

    /// <summary>A constant 360 W wall / 324 W DC interval of the given length.</summary>
    private static IntervalEnergy Interval(double seconds, bool gpu = true) => new(
        seconds, WallWh: 360 * seconds / 3600, DcWh: 324 * seconds / 3600, GpuWh: gpu ? 200 * seconds / 3600 : 0,
        CpuWh: 84 * seconds / 3600, OtherWh: 40 * seconds / 3600, GpuIncluded: gpu);

    [Fact]
    public void Interval_WithinOneHour_GoesToThatHour()
    {
        var h = new UsageHistory(FilePath);
        h.Add(new DateTime(2026, 5, 1, 14, 30, 0), Interval(10));

        HourlyUsage hour = Assert.Single(h.Snapshot());
        Assert.Equal(new DateTime(2026, 5, 1, 14, 0, 0), hour.Hour);
        Assert.Equal(1.0, hour.WallWh, 9);
        Assert.Equal(10, hour.MeasuredSeconds, 9);
    }

    [Fact]
    public void Interval_CrossingAnHour_IsSplitInProportion()
    {
        var h = new UsageHistory(FilePath);
        // 60 s ending at 15:00:15: 45 s belong to 14:00, 15 s to 15:00.
        h.Add(new DateTime(2026, 5, 1, 15, 0, 15), Interval(60));

        List<HourlyUsage> hours = h.Snapshot();
        Assert.Equal(2, hours.Count);
        Assert.Equal(new DateTime(2026, 5, 1, 14, 0, 0), hours[0].Hour);
        Assert.Equal(45, hours[0].MeasuredSeconds, 9);
        Assert.Equal(15, hours[1].MeasuredSeconds, 9);
        Assert.Equal(6.0 * 0.75, hours[0].WallWh, 9);
        Assert.Equal(6.0 * 0.25, hours[1].WallWh, 9);
        Assert.Equal(6.0, hours.Sum(x => x.WallWh), 9); // nothing lost or doubled
    }

    [Fact]
    public void LongInterval_SpanningSeveralHoursAndMidnight_IsSplitOverAll()
    {
        // 23:30 -> 01:30 (two hours): 30 min, 60 min, 30 min.
        var parts = UsageHistory.SplitByHour(new DateTime(2026, 5, 1, 23, 30, 0), new DateTime(2026, 5, 2, 1, 30, 0)).ToList();

        Assert.Equal(3, parts.Count);
        Assert.Equal(new DateTime(2026, 5, 1, 23, 0, 0), parts[0].Hour);
        Assert.Equal(new DateTime(2026, 5, 2, 0, 0, 0), parts[1].Hour);
        Assert.Equal(new DateTime(2026, 5, 2, 1, 0, 0), parts[2].Hour);
        Assert.Equal(0.25, parts[0].Fraction, 9);
        Assert.Equal(0.50, parts[1].Fraction, 9);
        Assert.Equal(0.25, parts[2].Fraction, 9);
    }

    [Fact]
    public void IntervalEndingExactlyOnTheHour_StaysInTheEarlierHour()
    {
        var parts = UsageHistory.SplitByHour(new DateTime(2026, 5, 1, 14, 59, 0), new DateTime(2026, 5, 1, 15, 0, 0)).ToList();

        var only = Assert.Single(parts);
        Assert.Equal(new DateTime(2026, 5, 1, 14, 0, 0), only.Hour);
        Assert.Equal(1.0, only.Fraction, 9);
    }

    [Fact]
    public void IntervalsWithoutGpu_AreCountedAsNoGpuTime()
    {
        var h = new UsageHistory(FilePath);
        h.Add(new DateTime(2026, 5, 1, 14, 0, 10), Interval(5, gpu: false));
        h.Add(new DateTime(2026, 5, 1, 14, 0, 15), Interval(5));

        HourlyUsage hour = Assert.Single(h.Snapshot());
        Assert.Equal(10, hour.MeasuredSeconds, 9);
        Assert.Equal(5, hour.NoGpuSeconds, 9);
    }

    [Fact]
    public void Flush_ThenLoad_RoundTrips_AndMergesWithNewData()
    {
        var first = new UsageHistory(FilePath);
        Assert.True(first.Load());
        first.Add(new DateTime(2026, 5, 1, 14, 30, 0), Interval(3600 * 0.5));
        Assert.True(first.Flush());

        var second = new UsageHistory(FilePath);
        second.Add(new DateTime(2026, 5, 1, 14, 59, 0), Interval(60)); // added before loading
        Assert.True(second.Load());

        HourlyUsage hour = Assert.Single(second.Snapshot());
        Assert.Equal(180 + 6, hour.WallWh, 6);
        Assert.Equal(1800 + 60, hour.MeasuredSeconds, 6);
    }

    [Fact]
    public void CorruptFile_DoesNotThrow_KeepsReadableLines_AndACopy()
    {
        File.WriteAllText(FilePath,
            UsageHistory.Header + "\n" +
            "2026-05-01 14:00,100,90,50,30,10,1800,0\n" +
            "garbage,,,\n" +
            "2026-05-01 15:00,-5,90,50,30,10,1800,0\n" +   // negative energy
            "2026-05-01 16:30,100,90,50,30,10,1800,0\n" +  // not a full hour
            "2026-05-01 17:00,100,90,50,30,10,99999,0\n" + // more than an hour of time
            "\u0000\u0001binary\n");

        var h = new UsageHistory(FilePath);
        Assert.True(h.Load());

        HourlyUsage hour = Assert.Single(h.Snapshot());
        Assert.Equal(100, hour.WallWh);
        Assert.Equal(5, h.SkippedLines);
        Assert.Single(Directory.GetFiles(_dir, UsageHistory.FileName + ".corrupt-*"));

        Assert.True(h.Flush()); // rewritten with only the good line
        var reread = new UsageHistory(FilePath);
        Assert.True(reread.Load());
        Assert.Equal(0, reread.SkippedLines);
        Assert.Single(reread.Snapshot());
    }

    [Fact]
    public void EntirelyBinaryFile_GivesEmptyHistory_WithoutThrowing()
    {
        File.WriteAllBytes(FilePath, new byte[] { 0xFF, 0xFE, 0x00, 0x13, 0x37, 0x0A, 0x80, 0x81 });

        var h = new UsageHistory(FilePath);

        Assert.True(h.Load());
        Assert.Empty(h.Snapshot());
    }

    [Fact]
    public void LockedFile_IsNotOverwritten_AndIsMergedOnceReadable()
    {
        var seed = new UsageHistory(FilePath);
        seed.Add(new DateTime(2026, 5, 1, 10, 30, 0), Interval(1800));
        Assert.True(seed.Flush());

        var h = new UsageHistory(FilePath);
        using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(h.Load());
            h.Add(new DateTime(2026, 5, 1, 11, 30, 0), Interval(1800));
            Assert.False(h.Flush()); // must not replace the file it could not read
            Assert.NotNull(h.LastError);
        }

        Assert.True(h.Flush());
        var reread = new UsageHistory(FilePath);
        Assert.True(reread.Load());
        Assert.Equal(2, reread.Snapshot().Count);
    }

    [Fact]
    public void Clear_RemovesEverything_OnDiskToo()
    {
        var h = new UsageHistory(FilePath);
        h.Add(new DateTime(2026, 5, 1, 10, 30, 0), Interval(60));
        Assert.True(h.Flush());

        Assert.True(h.Clear());

        var reread = new UsageHistory(FilePath);
        Assert.True(reread.Load());
        Assert.Empty(reread.Snapshot());
        Assert.Empty(h.Snapshot());
    }

    [Fact]
    public void EverySave_KeepsThePreviousVersionAsBackup()
    {
        var h = new UsageHistory(FilePath);
        h.Add(new DateTime(2026, 5, 1, 10, 30, 0), Interval(60));
        Assert.True(h.Flush());
        Assert.False(File.Exists(h.BackupPath)); // nothing to back up yet

        h.Add(new DateTime(2026, 5, 1, 11, 30, 0), Interval(60));
        Assert.True(h.Flush());

        var backup = UsageHistory.Parse(File.ReadAllLines(h.BackupPath));
        Assert.Single(backup.Records); // the first version
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Theory]
    [InlineData("")]                       // empty file, e.g. a power cut before the data reached the disk
    [InlineData("\0\0\0\0\0\0\0\0")]       // zero-filled file
    [InlineData("not,a,history\nfile\n")]  // no header, no readable line
    public void DamagedFile_IsRecoveredFromTheBackup(string damaged)
    {
        var h = new UsageHistory(FilePath);
        h.Add(new DateTime(2026, 5, 1, 10, 30, 0), Interval(60));
        Assert.True(h.Flush());
        h.Add(new DateTime(2026, 5, 1, 11, 30, 0), Interval(60));
        Assert.True(h.Flush()); // backup now holds the 10:00 hour
        File.WriteAllText(FilePath, damaged);

        var reread = new UsageHistory(FilePath);
        Assert.True(reread.Load());

        Assert.True(reread.RecoveredFromBackup);
        HourlyUsage hour = Assert.Single(reread.Snapshot());
        Assert.Equal(new DateTime(2026, 5, 1, 10, 0, 0), hour.Hour);
        Assert.True(reread.Flush()); // writes the recovered data back
        Assert.Single(UsageHistory.Parse(File.ReadAllLines(FilePath)).Records);
    }

    [Fact]
    public void MissingFileWithBackup_IsRestored_AndTheBackupSurvivesLaterSaves()
    {
        var h = new UsageHistory(FilePath);
        h.Add(new DateTime(2026, 5, 1, 10, 30, 0), Interval(60));
        Assert.True(h.Flush());
        h.Add(new DateTime(2026, 5, 1, 11, 30, 0), Interval(60));
        Assert.True(h.Flush()); // backup holds the 10:00 hour
        File.Delete(FilePath);  // an interrupted swap: old file already moved to .bak, new one not in place

        var reread = new UsageHistory(FilePath);
        Assert.True(reread.Load());

        Assert.True(reread.RecoveredFromBackup);
        Assert.Equal(new DateTime(2026, 5, 1, 10, 0, 0), Assert.Single(reread.Snapshot()).Hour);
        Assert.True(File.Exists(FilePath)); // recreated at once from the backup

        // Two more saves: the recovered hour must still be there and in the backup chain.
        reread.Add(new DateTime(2026, 5, 1, 12, 30, 0), Interval(60));
        Assert.True(reread.Flush());
        reread.Add(new DateTime(2026, 5, 1, 13, 30, 0), Interval(60));
        Assert.True(reread.Flush());
        Assert.Equal(3, UsageHistory.Parse(File.ReadAllLines(FilePath)).Records.Count);
        Assert.Contains(UsageHistory.Parse(File.ReadAllLines(reread.BackupPath)).Records,
            r => r.Hour == new DateTime(2026, 5, 1, 10, 0, 0));
    }

    [Fact]
    public void MissingFileWithoutBackup_IsANewEmptyHistory()
    {
        var h = new UsageHistory(FilePath);

        Assert.True(h.Load());
        Assert.False(h.RecoveredFromBackup);
        Assert.Empty(h.Snapshot());
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void HeaderOnlyFile_IsNotReplacedByABackup_EvenIfOneIsLeft()
    {
        // A reset writes a header-only file; if deleting the backup failed, the old
        // history must not come back.
        File.WriteAllText(FilePath + ".bak", UsageHistory.Header + "\n2026-05-01 10:00,100,90,50,30,10,1800,0\n");
        File.WriteAllText(FilePath, "# comment\n" + UsageHistory.Header + "\n");

        var h = new UsageHistory(FilePath);
        Assert.True(h.Load());

        Assert.False(h.RecoveredFromBackup);
        Assert.Empty(h.Snapshot());
    }

    [Fact]
    public void EmptyFileWithoutBackup_GivesEmptyHistory()
    {
        File.WriteAllText(FilePath, "");

        var h = new UsageHistory(FilePath);

        Assert.True(h.Load());
        Assert.False(h.RecoveredFromBackup);
        Assert.Empty(h.Snapshot());
    }

    [Fact]
    public void HeaderOnlyFile_IsAValidEmptyHistory_NotRecovered()
    {
        var h = new UsageHistory(FilePath);
        h.Add(new DateTime(2026, 5, 1, 10, 30, 0), Interval(60));
        Assert.True(h.Flush());
        Assert.True(h.Clear()); // writes a header-only file

        var reread = new UsageHistory(FilePath);
        Assert.True(reread.Load());

        Assert.False(reread.RecoveredFromBackup);
        Assert.Empty(reread.Snapshot());
        Assert.False(File.Exists(h.BackupPath)); // reset leaves no copy of the deleted history
    }

    [Fact]
    public async Task AddingWhileFlushing_FromSeveralThreads_LosesNothing()
    {
        var h = new UsageHistory(FilePath);
        var start = new DateTime(2026, 5, 1, 10, 0, 0);
        Task adders = Task.Run(() =>
        {
            for (int i = 1; i <= 2000; i++)
            {
                h.Add(start.AddSeconds(i), Interval(1));
            }
        });
        Task flushers = Task.Run(() =>
        {
            while (!adders.IsCompleted)
            {
                h.Flush();
            }
        });
        await Task.WhenAll(adders, flushers);
        Assert.True(h.Flush());

        var reread = new UsageHistory(FilePath);
        Assert.True(reread.Load());
        Assert.Equal(2000, reread.Snapshot().Sum(x => x.MeasuredSeconds), 6);
    }

    [Fact]
    public void ZeroOrNegativeInterval_IsIgnored()
    {
        var h = new UsageHistory(FilePath);
        h.Add(new DateTime(2026, 5, 1, 10, 30, 0), Interval(0));
        h.Add(new DateTime(2026, 5, 1, 10, 30, 0), Interval(double.NaN));

        Assert.Empty(h.Snapshot());
    }

    [Fact]
    public void Engine_ReportsIntervalEnergy_MatchingTheSessionTotals()
    {
        var engine = new PowerMeterEngine(new FakeGpu(new GpuReading(200, 50), null), null, null);
        engine.Start();
        var settings = new MeterSettings { PsuEfficiency = 0.9, OtherWatts = 40, CpuIdleWatts = 20, CpuTdpWatts = 20 };

        var (s1, _) = engine.Tick(2, settings, new DateTime(2026, 5, 1, 10, 0, 2));
        var (s2, session) = engine.Tick(2, settings, new DateTime(2026, 5, 1, 10, 0, 4));

        Assert.True(s1.Energy.GpuIncluded);
        Assert.False(s2.Energy.GpuIncluded); // GPU failed: no GPU energy, never 0 W as a measurement
        Assert.Equal(0, s2.Energy.GpuWh);
        Assert.Equal(session.Wall.EnergyWh, s1.Energy.WallWh + s2.Energy.WallWh, 9);
        Assert.Equal(session.Dc.EnergyWh, s1.Energy.DcWh + s2.Energy.DcWh, 9);
        Assert.Equal(2, s1.Energy.Seconds);
    }
}
