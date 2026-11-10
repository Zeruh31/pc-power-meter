using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcPowerMeter.Core;

/// <summary>User settings, stored as JSON in %APPDATA%\PcPowerMeter\settings.json.</summary>
public sealed class AppSettings
{
    public const string LanguageAuto = "auto", LanguageEnglish = "en", LanguageTurkish = "tr";

    public double IntervalSeconds { get; set; } = 1.0;
    public double PsuEfficiency { get; set; } = 0.90;
    public double OtherWatts { get; set; } = 40;
    public double CpuTdpWatts { get; set; } = 120;
    public double CpuIdleWatts { get; set; } = 25;

    /// <summary>"auto" (follow the Windows display language), "en" or "tr".</summary>
    public string Language { get; set; } = LanguageAuto;

    public bool CsvEnabled { get; set; }

    /// <summary>CSV folder; null or empty means the default folder in Documents.</summary>
    public string? CsvFolder { get; set; }

    /// <summary>Closing the window hides it to the notification area instead of exiting.</summary>
    public bool CloseToTray { get; set; }

    public MeterSettings ToMeterSettings() => new MeterSettings
    {
        IntervalSeconds = IntervalSeconds,
        PsuEfficiency = PsuEfficiency,
        OtherWatts = OtherWatts,
        CpuTdpWatts = CpuTdpWatts,
        CpuIdleWatts = CpuIdleWatts,
    }.Normalized();

    /// <summary>Returns a copy with every value forced into its valid range.</summary>
    public AppSettings Normalized()
    {
        MeterSettings m = ToMeterSettings();
        string lang = (Language ?? LanguageAuto).Trim().ToLowerInvariant();
        return new AppSettings
        {
            IntervalSeconds = m.IntervalSeconds,
            PsuEfficiency = m.PsuEfficiency,
            OtherWatts = m.OtherWatts,
            CpuTdpWatts = m.CpuTdpWatts,
            CpuIdleWatts = m.CpuIdleWatts,
            Language = lang is LanguageEnglish or LanguageTurkish ? lang : LanguageAuto,
            CsvEnabled = CsvEnabled,
            CsvFolder = string.IsNullOrWhiteSpace(CsvFolder) ? null : CsvFolder.Trim(),
            CloseToTray = CloseToTray,
        };
    }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

/// <summary>Loads and saves <see cref="AppSettings"/>. Never throws on a missing or corrupt file.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PcPowerMeter", "settings.json");

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
            return (settings ?? new AppSettings()).Normalized();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Writes the settings atomically (temp file + rename). Returns false on failure.</summary>
    public static bool Save(AppSettings settings, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings.Normalized(), Options));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
