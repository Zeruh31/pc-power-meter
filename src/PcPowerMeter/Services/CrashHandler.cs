using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using PcPowerMeter.Localization;

namespace PcPowerMeter.Services;

/// <summary>
/// Last-resort error handling: logs the exception to %LOCALAPPDATA%\PcPowerMeter\error.log,
/// removes the tray icon so no dead icon is left behind, tells the user and closes.
/// Nothing is sent anywhere.
/// </summary>
internal static class CrashHandler
{
    private static int _handling;

    /// <summary>Called before shutting down, e.g. to remove the tray icon.</summary>
    public static Action? Cleanup { get; set; }

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcPowerMeter", "error.log");

    public static void Install(Application app)
    {
        app.DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTask;
    }

    private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Fail("UI thread", e.Exception, shutdown: true);
    }

    private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        // The runtime terminates the process after this handler; just log, clean up and inform.
        Fail("background thread", e.ExceptionObject as Exception, shutdown: false);
    }

    private static void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Not fatal: record it and keep running.
        e.SetObserved();
        Log("unobserved task", e.Exception);
    }

    private static void Fail(string where, Exception? ex, bool shutdown)
    {
        if (Interlocked.Exchange(ref _handling, 1) != 0)
        {
            return;
        }
        Log(where, ex);
        try
        {
            Cleanup?.Invoke();
        }
        catch (Exception cleanupError)
        {
            Log("cleanup", cleanupError);
        }
        try
        {
            MessageBox.Show(Loc.F("CrashMessage", LogPath), Loc.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // No UI available; the log is all we can do.
        }
        if (shutdown)
        {
            Application.Current?.Shutdown(1);
        }
    }

    private static void Log(string where, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var text = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                .Append(" [").Append(where).Append("] ")
                .AppendLine(ex?.ToString() ?? "unknown error")
                .AppendLine();
            File.AppendAllText(LogPath, text.ToString(), Encoding.UTF8);
        }
        catch (Exception)
        {
            // Logging must never throw from an error handler.
        }
    }
}
