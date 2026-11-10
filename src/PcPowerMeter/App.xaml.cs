using System.Windows;
using PcPowerMeter.Core;
using PcPowerMeter.Localization;
using PcPowerMeter.Services;
using PcPowerMeter.Theming;

namespace PcPowerMeter;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // "--data-dir <folder>" keeps settings, history and caches in that folder (testing, portable use).
        int dataIndex = Array.FindIndex(e.Args, a => a.Equals("--data-dir", StringComparison.OrdinalIgnoreCase));
        if (dataIndex >= 0 && dataIndex + 1 < e.Args.Length)
        {
            try
            {
                AppPaths.UseDataFolder(e.Args[dataIndex + 1]);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                MessageBox.Show(Loc.F("InvalidDataDir", e.Args[dataIndex + 1]), Loc.T("AppTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(2);
                return;
            }
        }
        CrashHandler.Install(this);

        AppSettings settings = SettingsStore.Load(AppPaths.SettingsPath);
        // "--lang en|tr" overrides the language for this run only (handy for screenshots).
        int langIndex = Array.FindIndex(e.Args, a => a.Equals("--lang", StringComparison.OrdinalIgnoreCase));
        string language = langIndex >= 0 && langIndex + 1 < e.Args.Length ? e.Args[langIndex + 1] : settings.Language;
        Loc.Apply(language, Resources);
        ThemeManager.Initialize(Resources);

        // One instance per usage history file: two instances must never write the same file.
        // The lock name comes from the normalized path, so every spelling of the folder matches.
        _mutex = new Mutex(true, AppPaths.InstanceLockName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(Loc.T("AlreadyRunning"), Loc.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        // Test switches for the "hardware missing" paths; documented in the README.
        var options = new SensorOptions(
            DisableGpu: e.Args.Contains("--no-gpu", StringComparer.OrdinalIgnoreCase),
            DisableCpuMeter: e.Args.Contains("--no-cpu-meter", StringComparer.OrdinalIgnoreCase));

        var usage = new UsageHistory(AppPaths.UsagePath);
        var tariffs = new TariffRepository(AppPaths.DataFolder);
        var window = new MainWindow(settings, options, usage, tariffs);
        CrashHandler.Cleanup = window.RemoveTrayIcon;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
