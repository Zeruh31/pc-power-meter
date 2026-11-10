using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace PcPowerMeter.Theming;

/// <summary>
/// Follows the Windows app theme (light or dark). Colors are published as application
/// resources "B.*" (brushes) so XAML can use DynamicResource and repaint on change.
/// </summary>
internal static partial class ThemeManager
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    public static bool IsDark { get; private set; }

    public static event Action? ThemeChanged;

    private static readonly Dictionary<string, (string Dark, string Light)> Palette = new()
    {
        ["Background"] = ("#1B1D21", "#F4F5F7"),
        ["Surface"] = ("#24272D", "#FFFFFF"),
        ["SurfaceAlt"] = ("#2D3139", "#ECEEF1"),
        ["Border"] = ("#3A3F48", "#D0D5DD"),
        ["Text"] = ("#E8EAED", "#1F2328"),
        ["SubText"] = ("#A0A6B0", "#59636E"),
        ["Accent"] = ("#F2B33D", "#A8650A"),
        ["AccentText"] = ("#1B1D21", "#FFFFFF"),
        ["Measured"] = ("#3FB950", "#1A7F37"),
        ["Estimated"] = ("#D29922", "#9A6700"),
        ["Unavailable"] = ("#F85149", "#CF222E"),
        ["WarningBg"] = ("#3B2A10", "#FFF4D6"),
        ["WarningFg"] = ("#F2CC60", "#6B4E00"),
        ["Grid"] = ("#33373F", "#E1E4E8"),
        ["SeriesWall"] = ("#F2B33D", "#C27C0E"),
        ["SeriesDc"] = ("#9AA3AE", "#6E7781"),
        ["SeriesGpu"] = ("#3FB950", "#1A7F37"),
        ["SeriesCpu"] = ("#58A6FF", "#0969DA"),
        ["SeriesOther"] = ("#BC8CFF", "#8250DF"),
    };

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    /// <summary>Applies the current Windows theme and starts listening for changes.</summary>
    public static void Initialize(ResourceDictionary resources)
    {
        Apply(resources);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    bool wasDark = IsDark;
                    Apply(resources);
                    if (wasDark != IsDark)
                    {
                        foreach (Window w in Application.Current.Windows)
                        {
                            ApplyTitleBar(w);
                        }
                        ThemeChanged?.Invoke();
                    }
                });
            }
        };
    }

    public static Brush Brush(string name) =>
        Application.Current.Resources["B." + name] as Brush ?? Brushes.Gray;

    /// <summary>Dark or light title bar to match the content (Windows 11; ignored elsewhere).</summary>
    public static void ApplyTitleBar(Window window)
    {
        nint hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0)
        {
            return;
        }
        int value = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    private static void Apply(ResourceDictionary resources)
    {
        IsDark = ReadAppsUseDarkTheme();
        foreach (var (name, pair) in Palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(IsDark ? pair.Dark : pair.Light);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources["B." + name] = brush;
        }
    }

    private static bool ReadAppsUseDarkTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
