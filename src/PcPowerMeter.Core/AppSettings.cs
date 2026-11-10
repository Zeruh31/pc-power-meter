using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcPowerMeter.Core;

/// <summary>User settings, stored as JSON in %APPDATA%\PcPowerMeter\settings.json.</summary>
public sealed class AppSettings
{
    public const string LanguageAuto = "auto", LanguageEnglish = "en", LanguageTurkish = "tr";
    public const string OtherFixed = "fixed", OtherComponents = "components";

    /// <summary>Tariff ids with a special meaning; null means "pick by the Windows region".</summary>
    public const string TariffOff = "off", TariffManual = "manual";

    /// <summary>
    /// Version of the settings file. Version 1 (0.1.x) had no such field and only a fixed
    /// "other components" value; see <see cref="SettingsStore.Load"/> for the migration.
    /// </summary>
    public const int CurrentVersion = 2;

    /// <summary>Written by <see cref="Normalized"/>; 0 when read from a file without the field.</summary>
    public int SettingsVersion { get; set; }

    public double IntervalSeconds { get; set; } = 1.0;
    public double PsuEfficiency { get; set; } = 0.90;

    /// <summary>"fixed" uses <see cref="OtherWatts"/>; "components" uses <see cref="Components"/>.</summary>
    public string OtherMode { get; set; } = OtherComponents;

    /// <summary>The fixed "other components" value (the only option before 0.2).</summary>
    public double OtherWatts { get; set; } = 40;

    public ComponentEstimate Components { get; set; } = new();

    public double CpuTdpWatts { get; set; } = 120;
    public double CpuIdleWatts { get; set; } = 25;

    /// <summary>"auto" (follow the Windows display language), "en" or "tr".</summary>
    public string Language { get; set; } = LanguageAuto;

    public bool CsvEnabled { get; set; }

    /// <summary>CSV folder; null or empty means the default folder in Documents.</summary>
    public string? CsvFolder { get; set; }

    /// <summary>Closing the window hides it to the notification area instead of exiting.</summary>
    public bool CloseToTray { get; set; }

    /// <summary>Tariff id from the table, "manual", "off", or null for automatic by region.</summary>
    public string? TariffId { get; set; }

    /// <summary>Selected tier of a tiered tariff (e.g. "low" or "high"); null means the first tier.</summary>
    public string? TariffTier { get; set; }

    /// <summary>ISO 4217 currency of the manual price; null means the Windows region's currency.</summary>
    public string? ManualCurrency { get; set; }

    /// <summary>Manual price uses day / peak / night prices instead of one flat price.</summary>
    public bool ManualTimeOfUse { get; set; }

    public double ManualPrice { get; set; }
    public double ManualDayPrice { get; set; }
    public double ManualPeakPrice { get; set; }
    public double ManualNightPrice { get; set; }

    /// <summary>Off by default: the only network access of the app, see the README.</summary>
    public bool OnlineTariffUpdate { get; set; }

    /// <summary>The "other components" watts actually used for the measurements.</summary>
    [JsonIgnore]
    public double EffectiveOtherWatts => OtherMode == OtherFixed ? OtherWatts : (Components ?? new ComponentEstimate()).TotalWatts;

    public MeterSettings ToMeterSettings() => new MeterSettings
    {
        IntervalSeconds = IntervalSeconds,
        PsuEfficiency = PsuEfficiency,
        OtherWatts = EffectiveOtherWatts,
        CpuTdpWatts = CpuTdpWatts,
        CpuIdleWatts = CpuIdleWatts,
    }.Normalized();

    /// <summary>Returns a copy with every value forced into its valid range.</summary>
    public AppSettings Normalized()
    {
        var m = new MeterSettings
        {
            IntervalSeconds = IntervalSeconds,
            PsuEfficiency = PsuEfficiency,
            OtherWatts = OtherWatts,
            CpuTdpWatts = CpuTdpWatts,
            CpuIdleWatts = CpuIdleWatts,
        }.Normalized();
        string lang = (Language ?? LanguageAuto).Trim().ToLowerInvariant();
        string? currency = ManualCurrency?.Trim().ToUpperInvariant();
        return new AppSettings
        {
            SettingsVersion = CurrentVersion,
            IntervalSeconds = m.IntervalSeconds,
            PsuEfficiency = m.PsuEfficiency,
            OtherMode = OtherMode == OtherFixed ? OtherFixed : OtherComponents,
            OtherWatts = m.OtherWatts,
            Components = (Components ?? new ComponentEstimate()).Normalized(),
            CpuTdpWatts = m.CpuTdpWatts,
            CpuIdleWatts = m.CpuIdleWatts,
            Language = lang is LanguageEnglish or LanguageTurkish ? lang : LanguageAuto,
            CsvEnabled = CsvEnabled,
            CsvFolder = string.IsNullOrWhiteSpace(CsvFolder) ? null : CsvFolder.Trim(),
            CloseToTray = CloseToTray,
            TariffId = string.IsNullOrWhiteSpace(TariffId) ? null : TariffId.Trim(),
            TariffTier = string.IsNullOrWhiteSpace(TariffTier) ? null : TariffTier.Trim(),
            ManualCurrency = currency is { Length: 3 } && currency.All(char.IsAsciiLetterUpper) ? currency : null,
            ManualTimeOfUse = ManualTimeOfUse,
            ManualPrice = Price(ManualPrice),
            ManualDayPrice = Price(ManualDayPrice),
            ManualPeakPrice = Price(ManualPeakPrice),
            ManualNightPrice = Price(ManualNightPrice),
            OnlineTariffUpdate = OnlineTariffUpdate,
        };
    }

    /// <summary>Largest manual price per kWh accepted, in any currency.</summary>
    public const double MaxManualPrice = 10000;

    private static double Price(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, MaxManualPrice) : 0;

    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.Components = (Components ?? new ComponentEstimate()) with { };
        return copy;
    }

    /// <summary>
    /// Brings settings read from an older file up to date. Version 1 files only knew the
    /// fixed "other components" value: those users keep exactly that value (mode "fixed")
    /// until they switch to the component estimate in Settings themselves.
    /// </summary>
    public static AppSettings Migrate(AppSettings loaded)
    {
        if (loaded.SettingsVersion < 2)
        {
            loaded.OtherMode = OtherFixed;
        }
        return loaded.Normalized();
    }
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

    /// <summary>
    /// Reads the settings. A missing file gives the defaults of a new installation; a file
    /// from an older version is migrated (<see cref="AppSettings.Migrate"/>).
    /// </summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings().Normalized();
            }
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
            return settings is null ? new AppSettings().Normalized() : AppSettings.Migrate(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings().Normalized();
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
