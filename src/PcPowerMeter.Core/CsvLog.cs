using System.Globalization;
using System.Text;

namespace PcPowerMeter.Core;

/// <summary>
/// CSV row format. Culture-independent on purpose: comma separator, '.' decimal point,
/// local time as "yyyy-MM-dd HH:mm:ss", empty field when a value is unavailable.
/// </summary>
public static class CsvFormat
{
    public const string Header =
        "timestamp,elapsed_s,gpu_w,gpu_load_pct,gpu_source,cpu_w,cpu_load_pct,cpu_source," +
        "other_w_est,dc_w,wall_w_est,psu_efficiency,gpu_included";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Row(PowerSample s) => string.Join(',',
        s.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", Inv),
        Num(s.ElapsedSeconds),
        Num(s.GpuWatts),
        Num(s.GpuLoadPercent),
        SourceName(s.GpuSource),
        Num(s.CpuWatts),
        Num(s.CpuLoadPercent),
        SourceName(s.CpuSource),
        Num(s.OtherWatts),
        Num(s.DcWatts),
        Num(s.WallWatts),
        s.PsuEfficiency.ToString("0.00", Inv),
        s.GpuIncluded ? "1" : "0");

    public static string SourceName(PowerSource source) => source switch
    {
        PowerSource.Nvml => "nvml",
        PowerSource.NvidiaSmi => "nvidia-smi",
        PowerSource.EnergyMeter => "energy-meter",
        PowerSource.LoadEstimate => "load-estimate",
        PowerSource.Fixed => "fixed",
        _ => "none",
    };

    private static string Num(double? value) => value.HasValue ? value.Value.ToString("0.00", Inv) : "";
}

/// <summary>
/// Writes samples to a new, time-stamped CSV file. A name that already exists or is
/// locked by another program (for example Excel) is skipped, never overwritten.
/// </summary>
public sealed class CsvLogger : IDisposable
{
    private readonly StreamWriter _writer;

    private CsvLogger(StreamWriter writer, string path)
    {
        _writer = writer;
        FilePath = path;
    }

    public string FilePath { get; }

    /// <summary>Base file name for a session that started at <paramref name="start"/>.</summary>
    public static string FileNameFor(DateTime start, int attempt) =>
        attempt <= 1
            ? $"power-log_{start.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.csv"
            : $"power-log_{start.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}_{attempt}.csv";

    /// <summary>
    /// Creates the folder if needed and opens a new log file.
    /// Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when no file can be created.
    /// </summary>
    public static CsvLogger Open(string folder, DateTime start, int maxAttempts = 20)
    {
        Directory.CreateDirectory(folder);
        IOException? last = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            string path = Path.Combine(folder, FileNameFor(start, attempt));
            FileStream stream;
            try
            {
                // CreateNew never touches an existing file; FileShare.Read lets Excel open it read-only.
                stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            catch (IOException ex)
            {
                last = ex;
                continue;
            }
            var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
            writer.WriteLine(CsvFormat.Header);
            return new CsvLogger(writer, path);
        }
        throw new IOException("Could not create a CSV log file in " + folder, last);
    }

    /// <summary>Appends one row. Throws <see cref="IOException"/> when the write fails.</summary>
    public void Write(PowerSample sample) => _writer.WriteLine(CsvFormat.Row(sample));

    public void Dispose()
    {
        try
        {
            _writer.Dispose();
        }
        catch (IOException)
        {
            // The disk may be gone; there is nothing useful left to do.
        }
    }
}
