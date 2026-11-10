using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PcPowerMeter.Core;
using PcPowerMeter.Localization;
using PcPowerMeter.Theming;

namespace PcPowerMeter;

public partial class SettingsWindow : Window
{
    private readonly string _defaultFolder;

    internal SettingsWindow(AppSettings settings, string defaultCsvFolder)
    {
        InitializeComponent();
        Result = settings;
        _defaultFolder = defaultCsvFolder;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        CultureInfo c = Loc.Culture;
        IntervalBox.Text = settings.IntervalSeconds.ToString("0.##", c);
        PsuBox.Text = (settings.PsuEfficiency * 100).ToString("0.#", c);
        OtherBox.Text = settings.OtherWatts.ToString("0.#", c);
        TdpBox.Text = settings.CpuTdpWatts.ToString("0.#", c);
        IdleBox.Text = settings.CpuIdleWatts.ToString("0.#", c);
        LangAuto.IsChecked = settings.Language == AppSettings.LanguageAuto;
        LangEn.IsChecked = settings.Language == AppSettings.LanguageEnglish;
        LangTr.IsChecked = settings.Language == AppSettings.LanguageTurkish;
        CloseToTrayBox.IsChecked = settings.CloseToTray;
        CsvBox.IsChecked = settings.CsvEnabled;
        FolderBox.Text = string.IsNullOrWhiteSpace(settings.CsvFolder) ? defaultCsvFolder : settings.CsvFolder;
    }

    /// <summary>The edited settings; valid after the dialog returns true.</summary>
    internal AppSettings Result { get; private set; }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Multiselect = false };
        if (Directory.Exists(FolderBox.Text))
        {
            dialog.InitialDirectory = FolderBox.Text;
        }
        if (dialog.ShowDialog(this) == true)
        {
            FolderBox.Text = dialog.FolderName;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();
        double interval = Parse(IntervalBox, "SetInterval", MeterSettings.MinInterval, MeterSettings.MaxInterval, errors);
        double psu = Parse(PsuBox, "SetPsu", MeterSettings.MinPsuEfficiency * 100, MeterSettings.MaxPsuEfficiency * 100, errors);
        double other = Parse(OtherBox, "SetOther", 0, MeterSettings.MaxWatts, errors);
        double tdp = Parse(TdpBox, "SetTdp", 1, MeterSettings.MaxWatts, errors);
        double idle = Parse(IdleBox, "SetIdle", 0, MeterSettings.MaxWatts, errors);
        if (errors.Count > 0)
        {
            ErrorText.Text = string.Join("\n", errors);
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        string folder = FolderBox.Text.Trim();
        Result = new AppSettings
        {
            IntervalSeconds = interval,
            PsuEfficiency = psu / 100.0,
            OtherWatts = other,
            CpuTdpWatts = tdp,
            CpuIdleWatts = Math.Min(idle, tdp),
            Language = LangTr.IsChecked == true ? AppSettings.LanguageTurkish
                : LangEn.IsChecked == true ? AppSettings.LanguageEnglish
                : AppSettings.LanguageAuto,
            CloseToTray = CloseToTrayBox.IsChecked == true,
            CsvEnabled = CsvBox.IsChecked == true,
            // The default folder is stored as "not set", so no user path ends up in the JSON.
            CsvFolder = folder.Length == 0 || string.Equals(folder, _defaultFolder, StringComparison.OrdinalIgnoreCase) ? null : folder,
        };
        DialogResult = true;
    }

    /// <summary>Accepts both the UI culture's decimal separator and '.'.</summary>
    private static double Parse(TextBox box, string labelKey, double min, double max, List<string> errors)
    {
        string text = box.Text.Trim();
        if ((double.TryParse(text, NumberStyles.Float, Loc.Culture, out double value) ||
             double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
            value >= min && value <= max)
        {
            return value;
        }
        errors.Add(Loc.F("InvalidValue", Loc.T(labelKey), min.ToString("0.##", Loc.Culture), max.ToString("0.##", Loc.Culture)));
        return double.NaN;
    }
}
