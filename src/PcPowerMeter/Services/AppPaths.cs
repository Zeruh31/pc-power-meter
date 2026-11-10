using System.Reflection;
using PcPowerMeter.Core;

namespace PcPowerMeter.Services;

/// <summary>
/// Where the app keeps its files. By default settings go to %APPDATA%\PcPowerMeter and
/// everything else (usage history, tariff cache, error log) to %LOCALAPPDATA%\PcPowerMeter.
/// "--data-dir &lt;folder&gt;" puts all of them in one folder instead (for testing or a
/// portable setup).
/// </summary>
internal static class AppPaths
{
    private static string? _override;

    /// <summary>Uses <paramref name="folder"/> for all files. Throws for an invalid path.</summary>
    public static void UseDataFolder(string folder) => _override = DataPaths.NormalizeFolder(folder);

    public static string DataFolder => _override ?? DataPaths.NormalizeFolder(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcPowerMeter"));

    public static string SettingsPath => _override is null
        ? SettingsStore.DefaultPath
        : Path.Combine(_override, "settings.json");

    public static string UsagePath => Path.Combine(DataFolder, UsageHistory.FileName);

    public static string ErrorLogPath => Path.Combine(DataFolder, "error.log");

    /// <summary>One running instance per usage history file, however the folder was spelled.</summary>
    public static string InstanceLockName => DataPaths.InstanceLockName(UsagePath);

    /// <summary>"0.2.0": the app version without the revision number or build metadata.</summary>
    public static string Version
    {
        get
        {
            Version? v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
}
