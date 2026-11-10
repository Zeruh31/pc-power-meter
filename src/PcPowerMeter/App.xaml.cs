using System.Windows;
using PcPowerMeter.Core;
using PcPowerMeter.Localization;
using PcPowerMeter.Services;
using PcPowerMeter.Theming;

namespace PcPowerMeter;

public partial class App : Application
{
    private const string MutexName = @"Local\PcPowerMeter.SingleInstance";
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CrashHandler.Install(this);

        AppSettings settings = SettingsStore.Load(SettingsStore.DefaultPath);
        // "--lang en|tr" overrides the language for this run only (handy for screenshots).
        int langIndex = Array.FindIndex(e.Args, a => a.Equals("--lang", StringComparison.OrdinalIgnoreCase));
        string language = langIndex >= 0 && langIndex + 1 < e.Args.Length ? e.Args[langIndex + 1] : settings.Language;
        Loc.Apply(language, Resources);
        ThemeManager.Initialize(Resources);

        _mutex = new Mutex(true, MutexName, out bool createdNew);
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

        var window = new MainWindow(settings, options);
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
