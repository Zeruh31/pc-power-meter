using System.Globalization;
using PcPowerMeter.Core;

namespace PcPowerMeter.Tests;

public sealed class CsvAndSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pcpm-tests-" + Guid.NewGuid().ToString("N"));

    public CsvAndSettingsTests() => Directory.CreateDirectory(_dir);

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

    private static PowerSample Sample(double? gpu) => new(
        new DateTime(2026, 3, 4, 5, 6, 7), 12.5,
        gpu, gpu is null ? null : 97.25, gpu is null ? PowerSource.None : PowerSource.Nvml,
        65.4321, 12.0, PowerSource.EnergyMeter,
        40, (gpu ?? 0) + 105.4321, ((gpu ?? 0) + 105.4321) / 0.9, 0.9);

    [Fact]
    public void Row_IsCultureIndependent()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR"); // comma decimal separator
            string row = CsvFormat.Row(Sample(250.5));

            Assert.Equal("2026-03-04 05:06:07,12.50,250.50,97.25,nvml,65.43,12.00,energy-meter,40.00,355.93,395.48,0.90,1", row);
            Assert.Equal(CsvFormat.Header.Count(c => c == ','), row.Count(c => c == ','));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Row_LeavesUnavailableGpuEmpty_AndMarksItExcluded()
    {
        string[] fields = CsvFormat.Row(Sample(null)).Split(',');

        Assert.Equal("", fields[2]);
        Assert.Equal("", fields[3]);
        Assert.Equal("none", fields[4]);
        Assert.Equal("0", fields[^1]);
    }

    [Fact]
    public void Logger_WritesHeaderAndRows()
    {
        var start = new DateTime(2026, 3, 4, 5, 6, 7);
        string path;
        using (CsvLogger log = CsvLogger.Open(_dir, start))
        {
            path = log.FilePath;
            log.Write(Sample(100));
        }

        string[] lines = File.ReadAllLines(path);
        Assert.Equal(CsvLogger.FileNameFor(start, 1), Path.GetFileName(path));
        Assert.Equal(CsvFormat.Header, lines[0]);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void Logger_SkipsLockedFile_InsteadOfFailing()
    {
        var start = new DateTime(2026, 3, 4, 5, 6, 7);
        string first = Path.Combine(_dir, CsvLogger.FileNameFor(start, 1));
        using var locked = new FileStream(first, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        using CsvLogger log = CsvLogger.Open(_dir, start);

        Assert.Equal(CsvLogger.FileNameFor(start, 2), Path.GetFileName(log.FilePath));
        log.Write(Sample(100));
    }

    [Fact]
    public void Logger_ThrowsIOException_WhenNoNameIsFree()
    {
        var start = new DateTime(2026, 3, 4, 5, 6, 7);
        File.WriteAllText(Path.Combine(_dir, CsvLogger.FileNameFor(start, 1)), "x");
        File.WriteAllText(Path.Combine(_dir, CsvLogger.FileNameFor(start, 2)), "x");

        Assert.Throws<IOException>(() => CsvLogger.Open(_dir, start, maxAttempts: 2));
    }

    [Fact]
    public void Settings_RoundTripThroughJson()
    {
        string path = Path.Combine(_dir, "sub", "settings.json");
        var settings = new AppSettings
        {
            IntervalSeconds = 2, PsuEfficiency = 0.87, OtherWatts = 55, CpuTdpWatts = 105, CpuIdleWatts = 30,
            Language = "tr", CsvEnabled = true, CsvFolder = _dir, CloseToTray = true,
        };

        Assert.True(SettingsStore.Save(settings, path));
        AppSettings loaded = SettingsStore.Load(path);

        Assert.Equal(2, loaded.IntervalSeconds);
        Assert.Equal(0.87, loaded.PsuEfficiency);
        Assert.Equal(55, loaded.OtherWatts);
        Assert.Equal(105, loaded.CpuTdpWatts);
        Assert.Equal(30, loaded.CpuIdleWatts);
        Assert.Equal("tr", loaded.Language);
        Assert.True(loaded.CsvEnabled);
        Assert.Equal(_dir, loaded.CsvFolder);
        Assert.True(loaded.CloseToTray);
    }

    [Fact]
    public void Settings_CorruptOrMissingFile_GivesDefaults()
    {
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ not json");

        AppSettings corrupt = SettingsStore.Load(path);
        AppSettings missing = SettingsStore.Load(Path.Combine(_dir, "nope.json"));

        Assert.Equal(0.90, corrupt.PsuEfficiency);
        Assert.Equal(40, missing.OtherWatts);
        Assert.Equal(AppSettings.LanguageAuto, missing.Language);
    }

    [Fact]
    public void Settings_AreClampedIntoValidRanges()
    {
        var s = new AppSettings
        {
            IntervalSeconds = 0, PsuEfficiency = 1.5, OtherWatts = -5, CpuTdpWatts = 100, CpuIdleWatts = 500,
            Language = "de",
        }.Normalized();

        Assert.Equal(MeterSettings.MinInterval, s.IntervalSeconds);
        Assert.Equal(1.0, s.PsuEfficiency);
        Assert.Equal(0, s.OtherWatts);
        Assert.Equal(100, s.CpuIdleWatts); // never above TDP
        Assert.Equal(AppSettings.LanguageAuto, s.Language);
    }

    [Fact]
    public void History_KeepsOnlyTheWindow_AndRestartsAfterReset()
    {
        var h = new PowerHistory(windowSeconds: 10);
        for (int t = 1; t <= 30; t++)
        {
            h.Add(new HistoryPoint(t, 100, 90, 50, 30, 10));
        }

        Assert.Equal(20, h.Points[0].Time); // the point at the left edge is kept
        Assert.Equal(30, h.Points[^1].Time);

        h.Add(new HistoryPoint(1, 100, 90, null, 30, 10)); // session reset
        Assert.Single(h.Points);
    }
}
