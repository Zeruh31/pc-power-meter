using System.Globalization;
using System.Text;

namespace PcPowerMeter.Core;

/// <summary>Energy of one measured interval, as added to the session statistics.</summary>
/// <param name="Seconds">Length of the interval.</param>
/// <param name="GpuIncluded">False when the GPU could not be read; GpuWh is then 0 and not a measurement.</param>
public readonly record struct IntervalEnergy(
    double Seconds, double WallWh, double DcWh, double GpuWh, double CpuWh, double OtherWh, bool GpuIncluded);

/// <summary>
/// Totals of one clock hour (local time). <see cref="Hour"/> is the start of the hour.
/// Only the time in which the app was measuring is covered (<see cref="MeasuredSeconds"/>).
/// </summary>
/// <param name="NoGpuSeconds">Part of the measured time in which the GPU could not be read.</param>
public sealed record HourlyUsage(
    DateTime Hour, double WallWh, double DcWh, double GpuWh, double CpuWh, double OtherWh,
    double MeasuredSeconds, double NoGpuSeconds)
{
    public HourlyUsage Plus(HourlyUsage o) => this with
    {
        WallWh = WallWh + o.WallWh,
        DcWh = DcWh + o.DcWh,
        GpuWh = GpuWh + o.GpuWh,
        CpuWh = CpuWh + o.CpuWh,
        OtherWh = OtherWh + o.OtherWh,
        MeasuredSeconds = MeasuredSeconds + o.MeasuredSeconds,
        NoGpuSeconds = NoGpuSeconds + o.NoGpuSeconds,
    };

    public static HourlyUsage Empty(DateTime hour) => new(hour, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// The persistent hourly usage history (usage-hourly.csv in the data folder). Thread-safe:
/// the sampler thread adds intervals and flushes, the UI reads snapshots.
/// </summary>
/// <remarks>
/// File format: a plain, culture-independent CSV file, rewritten on every flush: the new
/// version is written to a temp file and flushed to the disk, then swapped in with
/// <see cref="File.Replace(string, string, string?)"/>, which keeps the previous version as
/// ".bak". A crash or power cut therefore loses at most the data since the last flush; if
/// the file is still found empty or unreadable, the backup is used. A year of hourly rows is
/// a few hundred kilobytes. Lines that cannot be read are skipped and the original file is
/// kept as a ".corrupt-*" copy before it is rewritten.
/// </remarks>
public sealed class UsageHistory
{
    public const string FileName = "usage-hourly.csv";
    public const string Header = "hour,wall_wh,dc_wh,gpu_wh,cpu_wh,other_wh,measured_s,no_gpu_s";
    private const string Comment = "# PC Power Meter hourly usage, format 1. Local time; energy in Wh, time in seconds.";
    private const string HourFormat = "yyyy-MM-dd HH:mm";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // _lock guards the data and is only held briefly, so Add never waits for the disk.
    // _fileLock serializes reads and writes of the file; take it before _lock, never after.
    private readonly object _lock = new();
    private readonly object _fileLock = new();
    private readonly Dictionary<DateTime, HourlyUsage> _hours = new();
    private bool _loaded;
    private bool _dirty;
    private int _revision;

    public UsageHistory(string path) => FilePath = path;

    public string FilePath { get; }

    /// <summary>Last load or save problem, or null.</summary>
    public string? LastError { get; private set; }

    /// <summary>Lines skipped as unreadable when the file was loaded.</summary>
    public int SkippedLines { get; private set; }

    /// <summary>Changes with every added interval, load or clear.</summary>
    public int Revision
    {
        get
        {
            lock (_lock)
            {
                return _revision;
            }
        }
    }

    /// <summary>The previous version of the file, kept by every save.</summary>
    public string BackupPath => FilePath + ".bak";

    /// <summary>True when the file was empty or unreadable and the history came from the backup.</summary>
    public bool RecoveredFromBackup { get; private set; }

    /// <summary>
    /// Reads the file and merges it with anything added so far. Returns false (and keeps
    /// the data in memory) if the file exists but cannot be opened, e.g. while it is locked;
    /// the next <see cref="Flush"/> tries again, so the file is never overwritten unread.
    /// If the file is empty or has no readable line, the backup of the previous save is used.
    /// </summary>
    public bool Load()
    {
        lock (_fileLock)
        {
            return LoadFromDisk();
        }
    }

    /// <summary>Reads the file without holding the data lock. The caller holds <see cref="_fileLock"/>.</summary>
    private bool LoadFromDisk()
    {
        lock (_lock)
        {
            if (_loaded)
            {
                return true;
            }
        }
        string[]? lines;
        try
        {
            lines = File.Exists(FilePath) ? File.ReadAllLines(FilePath, Encoding.UTF8) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError(ex.Message);
            return false;
        }

        var records = new List<HourlyUsage>();
        int skipped = 0;
        bool fromBackup = false;
        if (lines is not null)
        {
            (records, skipped, bool header) = Parse(lines);
            // An empty file, or one without a single readable line, is a damaged save
            // (e.g. a power cut while the disk cache was being written): use the backup.
            if (records.Count == 0 && (skipped > 0 || !header))
            {
                List<HourlyUsage>? backup = ReadBackup();
                if (backup is { Count: > 0 })
                {
                    records = backup;
                    fromBackup = true;
                }
            }
            if (skipped > 0 || fromBackup)
            {
                KeepCorruptCopy();
            }
        }
        else
        {
            // No file but a backup: a save was interrupted between moving the old file to
            // the backup and moving the new one in. Restore the file from the backup now,
            // so a later save cannot replace the backup before the data is back.
            List<HourlyUsage>? backup = ReadBackup();
            if (backup is { Count: > 0 })
            {
                records = backup;
                fromBackup = true;
                try
                {
                    File.Copy(BackupPath, FilePath, overwrite: false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The data is in memory and marked dirty; the next flush writes the file.
                }
            }
        }

        lock (_lock)
        {
            foreach (HourlyUsage r in records)
            {
                Merge(r);
            }
            _loaded = true;
            _dirty |= skipped > 0 || fromBackup;
            _revision++;
            SkippedLines = skipped;
            RecoveredFromBackup = fromBackup;
            LastError = null;
        }
        return true;
    }

    private List<HourlyUsage>? ReadBackup()
    {
        try
        {
            return File.Exists(BackupPath) ? Parse(File.ReadAllLines(BackupPath, Encoding.UTF8)).Records : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void SetError(string? error)
    {
        lock (_lock)
        {
            LastError = error;
        }
    }

    /// <summary>Parses file lines. Unreadable lines are counted, not thrown.</summary>
    public static (List<HourlyUsage> Records, int Skipped, bool HeaderSeen) Parse(IEnumerable<string> lines)
    {
        var records = new List<HourlyUsage>();
        int skipped = 0;
        bool headerSeen = false;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            if (!headerSeen && line == Header)
            {
                headerSeen = true;
                continue;
            }
            HourlyUsage? record = ParseLine(line);
            if (record is null)
            {
                skipped++;
            }
            else
            {
                records.Add(record);
            }
        }
        return (records, skipped, headerSeen);
    }

    private static HourlyUsage? ParseLine(string line)
    {
        string[] f = line.Split(',');
        if (f.Length != 8 ||
            !DateTime.TryParseExact(f[0], HourFormat, Inv, DateTimeStyles.None, out DateTime hour) ||
            hour.Minute != 0)
        {
            return null;
        }
        var v = new double[7];
        for (int i = 0; i < 7; i++)
        {
            if (!double.TryParse(f[i + 1], NumberStyles.Float, Inv, out v[i]) || !double.IsFinite(v[i]) || v[i] < 0)
            {
                return null;
            }
        }
        // An hour cannot hold more than an hour of measuring (a little slack for rounding
        // and for the doubled hour when daylight saving time ends).
        if (v[5] > 7300 || v[6] > v[5] + 1)
        {
            return null;
        }
        return new HourlyUsage(hour, v[0], v[1], v[2], v[3], v[4], v[5], v[6]);
    }

    public static string FormatLine(HourlyUsage h) => string.Join(',',
        h.Hour.ToString(HourFormat, Inv),
        h.WallWh.ToString("0.####", Inv), h.DcWh.ToString("0.####", Inv), h.GpuWh.ToString("0.####", Inv),
        h.CpuWh.ToString("0.####", Inv), h.OtherWh.ToString("0.####", Inv),
        h.MeasuredSeconds.ToString("0.###", Inv), h.NoGpuSeconds.ToString("0.###", Inv));

    private void KeepCorruptCopy()
    {
        try
        {
            string copy = FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", Inv);
            File.Copy(FilePath, copy, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort only; the readable lines are kept either way.
        }
    }

    /// <summary>
    /// Adds an interval that ended at <paramref name="end"/>. An interval that crosses one
    /// or more hour boundaries is split between those hours in proportion to its time in each.
    /// </summary>
    public void Add(DateTime end, IntervalEnergy e)
    {
        if (!(e.Seconds > 0) || !double.IsFinite(e.Seconds))
        {
            return;
        }
        DateTime start = end - TimeSpan.FromSeconds(e.Seconds);
        lock (_lock)
        {
            foreach ((DateTime hour, double fraction) in SplitByHour(start, end))
            {
                Merge(new HourlyUsage(hour,
                    e.WallWh * fraction, e.DcWh * fraction, e.GpuWh * fraction, e.CpuWh * fraction, e.OtherWh * fraction,
                    e.Seconds * fraction, e.GpuIncluded ? 0 : e.Seconds * fraction));
            }
            _dirty = true;
            _revision++;
        }
    }

    /// <summary>The hours touched by [start, end) and the fraction of the interval in each.</summary>
    public static IEnumerable<(DateTime Hour, double Fraction)> SplitByHour(DateTime start, DateTime end)
    {
        double total = (end - start).TotalSeconds;
        if (total <= 0)
        {
            yield break;
        }
        DateTime hour = HourOf(start);
        while (hour < end)
        {
            DateTime next = hour.AddHours(1);
            DateTime from = start > hour ? start : hour;
            DateTime to = end < next ? end : next;
            double part = (to - from).TotalSeconds;
            if (part > 0)
            {
                yield return (hour, part / total);
            }
            hour = next;
        }
    }

    public static DateTime HourOf(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Kind);

    private void Merge(HourlyUsage r)
    {
        _hours[r.Hour] = _hours.TryGetValue(r.Hour, out HourlyUsage? existing) ? existing.Plus(r) : r;
    }

    /// <summary>All hours, oldest first.</summary>
    public List<HourlyUsage> Snapshot()
    {
        lock (_lock)
        {
            return _hours.Values.OrderBy(h => h.Hour).ToList();
        }
    }

    /// <summary>
    /// Writes the file if anything changed. Returns false on failure (data stays in memory
    /// and is written by the next flush). The data lock is only held to take a snapshot.
    /// </summary>
    public bool Flush()
    {
        lock (_fileLock)
        {
            if (!LoadFromDisk())
            {
                return false; // never overwrite a file we could not read
            }
            List<HourlyUsage> snapshot;
            lock (_lock)
            {
                if (!_dirty)
                {
                    return true;
                }
                snapshot = _hours.Values.OrderBy(h => h.Hour).ToList();
                _dirty = false;
            }
            if (Write(FilePath, snapshot))
            {
                return true;
            }
            lock (_lock)
            {
                _dirty = true;
            }
            return false;
        }
    }

    /// <summary>Deletes all history, in memory and on disk (including the backup). Blocks on disk I/O.</summary>
    public bool Clear()
    {
        lock (_fileLock)
        {
            lock (_lock)
            {
                _hours.Clear();
                _loaded = true;
                _dirty = false;
                _revision++;
            }
            if (!Write(FilePath, Array.Empty<HourlyUsage>()))
            {
                lock (_lock)
                {
                    _dirty = true;
                }
                return false;
            }
            try
            {
                File.Delete(BackupPath); // the backup now holds the deleted history
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetError(ex.Message);
            }
            return true;
        }
    }

    /// <summary>
    /// Writes a temp file, forces it to the disk, then swaps it in. File.Replace keeps the
    /// previous version as the backup; where it is not available, copy + move does the same.
    /// </summary>
    private bool Write(string path, IReadOnlyCollection<HourlyUsage> hours)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.WriteLine(Comment);
                writer.WriteLine(Header);
                foreach (HourlyUsage h in hours)
                {
                    writer.WriteLine(FormatLine(h));
                }
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            if (!File.Exists(path))
            {
                File.Move(temp, path);
            }
            else
            {
                try
                {
                    File.Replace(temp, path, BackupPath, ignoreMetadataErrors: true);
                }
                catch (Exception ex) when (ex is PlatformNotSupportedException or IOException)
                {
                    // e.g. a file system without ReplaceFile support. If Replace got as far as
                    // moving the old file to the backup, only the move of the new one is left.
                    if (File.Exists(path))
                    {
                        File.Copy(path, BackupPath, overwrite: true);
                    }
                    File.Move(temp, path, overwrite: true);
                }
            }
            SetError(null);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError(ex.Message);
            return false;
        }
    }
}
