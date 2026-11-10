using System.ComponentModel;
using System.Diagnostics;
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
    private readonly AppSettings _original;
    private readonly TariffRepository _tariffs;
    private readonly string _regionCode;
    private readonly List<ComponentRow> _rows = new();
    private readonly List<RadioButton> _tierRadios = new();
    private bool _ready;

    internal SettingsWindow(AppSettings settings, string defaultCsvFolder, TariffRepository tariffs, string regionCode)
    {
        InitializeComponent();
        Result = settings;
        _original = settings;
        _defaultFolder = defaultCsvFolder;
        _tariffs = tariffs;
        _regionCode = regionCode;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        CultureInfo c = Loc.Culture;
        IntervalBox.Text = settings.IntervalSeconds.ToString("0.##", c);
        PsuBox.Text = (settings.PsuEfficiency * 100).ToString("0.#", c);
        TdpBox.Text = settings.CpuTdpWatts.ToString("0.#", c);
        IdleBox.Text = settings.CpuIdleWatts.ToString("0.#", c);
        LangAuto.IsChecked = settings.Language == AppSettings.LanguageAuto;
        LangEn.IsChecked = settings.Language == AppSettings.LanguageEnglish;
        LangTr.IsChecked = settings.Language == AppSettings.LanguageTurkish;
        CloseToTrayBox.IsChecked = settings.CloseToTray;
        CsvBox.IsChecked = settings.CsvEnabled;
        FolderBox.Text = string.IsNullOrWhiteSpace(settings.CsvFolder) ? defaultCsvFolder : settings.CsvFolder;

        // Other components
        OtherBox.Text = settings.OtherWatts.ToString("0.#", c);
        BuildComponentGrid(settings.Components.Normalized());
        OtherFixedRadio.IsChecked = settings.OtherMode == AppSettings.OtherFixed;
        OtherComponentsRadio.IsChecked = settings.OtherMode != AppSettings.OtherFixed;

        // Electricity cost
        CurrencyBox.Text = settings.ManualCurrency ?? TariffResolver.RegionCurrency(regionCode) ?? "EUR";
        ManualPriceBox.Text = PriceText(settings.ManualPrice);
        ManualDayBox.Text = PriceText(settings.ManualDayPrice);
        ManualPeakBox.Text = PriceText(settings.ManualPeakPrice);
        ManualNightBox.Text = PriceText(settings.ManualNightPrice);
        ManualTouRadio.IsChecked = settings.ManualTimeOfUse;
        ManualFlatRadio.IsChecked = !settings.ManualTimeOfUse;
        OnlineBox.IsChecked = settings.OnlineTariffUpdate;
        FillTariffList(settings.TariffId);
        ShowTableInfo();

        _ready = true;
        UpdateOtherMode();
        UpdateComponentTotal();
        UpdateManualMode();
        UpdateTariffDetails();
    }

    /// <summary>The edited settings; valid after the dialog returns true.</summary>
    internal AppSettings Result { get; private set; }

    private static string PriceText(double price) => price > 0 ? price.ToString("0.####", Loc.Culture) : "";

    // ------------------------------------------------------------------ other components

    /// <summary>The input controls of one component line.</summary>
    private sealed class ComponentRow
    {
        public required string Key { get; init; }
        public TextBox? Count { get; init; }
        public required TextBox Watts { get; init; }
        public List<RadioButton> Options { get; } = new();
        public required TextBlock Subtotal { get; init; }

        public string? Selected => Options.FirstOrDefault(o => o.IsChecked == true)?.Tag as string;
    }

    private void BuildComponentGrid(ComponentEstimate c)
    {
        ComponentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ComponentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        ComponentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ComponentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        ComponentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        AddHeader("ColCount", 1);
        AddHeader("ColWattsEach", 3);

        AddRow("Board", null, c.BoardWatts, c.Board, ComponentEstimate.DefaultBoardWatts,
            (ComponentEstimate.BoardEntry, "OptEntry"), (ComponentEstimate.BoardMainstream, "OptMainstream"), (ComponentEstimate.BoardHighEnd, "OptHighEnd"));
        AddRow("Ram", c.RamModules, c.RamModuleWatts, c.RamType, ComponentEstimate.DefaultRamWatts,
            (ComponentEstimate.RamDdr4, null), (ComponentEstimate.RamDdr5, null));
        AddRow("CaseFans", c.CaseFans, c.CaseFanWatts, null, null);
        AddRow("Cooler", null, c.CoolerWatts, c.Cooler, ComponentEstimate.DefaultCoolerWatts,
            (ComponentEstimate.CoolerNone, "OptNone"), (ComponentEstimate.CoolerAir, "OptAir"), (ComponentEstimate.CoolerAio, "OptAio"));
        AddRow("Nvme", c.NvmeDrives, c.NvmeWatts, null, null);
        AddRow("SataSsd", c.SataSsds, c.SataSsdWatts, null, null);
        AddRow("Hdd", c.Hdds, c.HddWatts, null, null);
        AddRow("Rgb", null, c.RgbWatts, c.Rgb, ComponentEstimate.DefaultRgbWatts,
            (ComponentEstimate.RgbNone, "OptNone"), (ComponentEstimate.RgbFew, "OptFew"), (ComponentEstimate.RgbMany, "OptMany"));
        AddRow("Usb", null, c.UsbWatts, null, null);
        AddRow("Extra", null, c.ExtraWatts, null, null);
    }

    private void AddHeader(string key, int column)
    {
        if (ComponentGrid.RowDefinitions.Count == 0)
        {
            ComponentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        var text = new TextBlock { Text = Loc.T(key), Style = (Style)FindResource("SubText"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 2) };
        Grid.SetColumn(text, column);
        ComponentGrid.Children.Add(text);
    }

    private void AddRow(string key, int? count, double watts, string? selected, Func<string, double>? defaultWatts,
        params (string Value, string? LabelKey)[] options)
    {
        int row = ComponentGrid.RowDefinitions.Count;
        ComponentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        CultureInfo c = Loc.Culture;

        var label = new TextBlock { Text = Loc.T("Comp" + key), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Place(label, row, 0);

        TextBox? countBox = null;
        if (count.HasValue)
        {
            countBox = new TextBox { Text = count.Value.ToString(c), Margin = new Thickness(6, 3, 0, 3), HorizontalContentAlignment = HorizontalAlignment.Right };
            countBox.TextChanged += (_, _) => UpdateComponentTotal();
            Place(countBox, row, 1);
        }

        var wattsBox = new TextBox { Text = watts.ToString("0.##", c), Margin = new Thickness(6, 3, 0, 3), HorizontalContentAlignment = HorizontalAlignment.Right };
        wattsBox.TextChanged += (_, _) => UpdateComponentTotal();
        Place(wattsBox, row, 3);

        var subtotal = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        subtotal.SetResourceReference(TextBlock.ForegroundProperty, "B.SubText");
        Place(subtotal, row, 4);

        var r = new ComponentRow { Key = key, Count = countBox, Watts = wattsBox, Subtotal = subtotal };
        if (options.Length > 0)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            foreach ((string value, string? labelKey) in options)
            {
                var radio = new RadioButton
                {
                    GroupName = "Comp" + key,
                    Content = labelKey is null ? value.ToUpperInvariant() : Loc.T(labelKey),
                    Tag = value,
                    IsChecked = value == selected,
                    Margin = new Thickness(0, 0, 10, 0),
                };
                // A new type fills in its typical value; the user can still change it afterwards.
                radio.Checked += (_, _) =>
                {
                    if (_ready && defaultWatts is not null)
                    {
                        wattsBox.Text = defaultWatts(value).ToString("0.##", Loc.Culture);
                    }
                };
                r.Options.Add(radio);
                panel.Children.Add(radio);
            }
            Place(panel, row, 2);
        }
        _rows.Add(r);
    }

    private void Place(UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        ComponentGrid.Children.Add(element);
    }

    /// <summary>Reads the component inputs. Invalid inputs are reported in <paramref name="errors"/> if given.</summary>
    private ComponentEstimate ReadComponents(List<string>? errors)
    {
        var c = new ComponentEstimate();
        foreach (ComponentRow r in _rows)
        {
            string label = Loc.T("Comp" + r.Key);
            int count = r.Count is null ? 1 : ParseInt(r.Count, label, errors);
            double watts = ParseOptional(r.Watts, label, 0, ComponentEstimate.MaxUnitWatts, errors);
            string? type = r.Selected;
            switch (r.Key)
            {
                case "Board": c.Board = type ?? c.Board; c.BoardWatts = watts; break;
                case "Ram": c.RamModules = count; c.RamType = type ?? c.RamType; c.RamModuleWatts = watts; break;
                case "CaseFans": c.CaseFans = count; c.CaseFanWatts = watts; break;
                case "Cooler": c.Cooler = type ?? c.Cooler; c.CoolerWatts = watts; break;
                case "Nvme": c.NvmeDrives = count; c.NvmeWatts = watts; break;
                case "SataSsd": c.SataSsds = count; c.SataSsdWatts = watts; break;
                case "Hdd": c.Hdds = count; c.HddWatts = watts; break;
                case "Rgb": c.Rgb = type ?? c.Rgb; c.RgbWatts = watts; break;
                case "Usb": c.UsbWatts = watts; break;
                case "Extra": c.ExtraWatts = watts; break;
            }
        }
        return c;
    }

    private void UpdateComponentTotal()
    {
        if (!_ready)
        {
            return;
        }
        ComponentEstimate c = ReadComponents(null);
        IReadOnlyList<ComponentItem> items = c.Breakdown();
        foreach (ComponentRow r in _rows)
        {
            ComponentItem item = items.First(i => i.Key == r.Key);
            r.Subtotal.Text = item.Watts.ToString("0.#", Loc.Culture) + " W";
        }
        ComponentTotal.Text = Loc.F("SetOtherTotal", c.TotalWatts.ToString("N1", Loc.Culture));
    }

    private void OtherMode_Changed(object sender, RoutedEventArgs e) => UpdateOtherMode();

    private void UpdateOtherMode()
    {
        if (!_ready)
        {
            return;
        }
        bool components = OtherComponentsRadio.IsChecked == true;
        OtherBox.IsEnabled = !components;
        ComponentGrid.IsEnabled = components;
        ComponentGrid.Opacity = components ? 1 : 0.5;
        ComponentTotal.Opacity = components ? 1 : 0.5;
    }

    // ------------------------------------------------------------------ electricity cost

    private void FillTariffList(string? selectedId)
    {
        TariffTable table = _tariffs.Table;
        bool tr = Loc.IsTurkish;
        TariffChoice auto = TariffResolver.Resolve(table, new AppSettings(), _regionCode);
        string autoName = auto.Tariff is null
            ? Loc.T("TariffAutoNone")
            : $"{table.CountryOf(auto.Tariff)?.Name.Get(tr)} – {auto.Tariff.Name.Get(tr)}";

        AddTariffItem(null, Loc.F("TariffAuto", string.IsNullOrEmpty(_regionCode) ? "?" : _regionCode, autoName), selectedId);
        AddTariffItem(AppSettings.TariffOff, Loc.T("TariffOff"), selectedId);
        AddTariffItem(AppSettings.TariffManual, Loc.T("TariffManual"), selectedId);
        foreach (TariffCountry country in table.Countries)
        {
            foreach (Tariff t in country.Tariffs)
            {
                string price = t.Type == Tariff.TypeFlat ? " · " + Loc.Money(t.Price ?? 0, t.Currency, 4) + "/kWh" : "";
                AddTariffItem(t.Id, $"{country.Name.Get(tr)} – {t.Name.Get(tr)}{price}", selectedId);
            }
        }
        if (TariffList.SelectedItem is null)
        {
            TariffList.SelectedIndex = 0; // e.g. a tariff that is no longer in the table
        }
    }

    private void AddTariffItem(string? id, string text, string? selectedId)
    {
        var item = new ListBoxItem { Content = text, Tag = id };
        TariffList.Items.Add(item);
        if (id == selectedId)
        {
            TariffList.SelectedItem = item;
        }
    }

    private string? SelectedTariffId => (TariffList.SelectedItem as ListBoxItem)?.Tag as string;

    /// <summary>The table tariff the selection stands for: the chosen one, or the region's for "automatic".</summary>
    private Tariff? EffectiveTariff()
    {
        string? id = SelectedTariffId;
        if (id is AppSettings.TariffOff or AppSettings.TariffManual)
        {
            return null;
        }
        return TariffResolver.Resolve(_tariffs.Table, new AppSettings { TariffId = id }, _regionCode).Tariff;
    }

    private void TariffList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateTariffDetails();

    private void UpdateTariffDetails()
    {
        if (!_ready)
        {
            return;
        }
        bool tr = Loc.IsTurkish;
        bool manual = SelectedTariffId == AppSettings.TariffManual;
        ManualPanel.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;

        Tariff? t = EffectiveTariff();
        if (t is null)
        {
            TariffDetails.Text = "";
            TariffDetails.Visibility = Visibility.Collapsed;
            SourceLine.Visibility = Visibility.Collapsed;
            TierPanel.Visibility = Visibility.Collapsed;
            return;
        }

        string details = Loc.F("TariffValidFrom", t.ValidFrom) +
                         (t.DataPeriod is null ? "" : ", " + Loc.F("TariffDataPeriod", t.DataPeriod)) + ". " +
                         (t.Note?.Get(tr) ?? "");
        if (t.Type == Tariff.TypeTimeOfUse)
        {
            details = UsageWindow.PriceSummary(PriceSchedule.FromTariff(t, null), t) + ". " + details;
        }
        TariffDetails.Text = details;
        TariffDetails.Visibility = Visibility.Visible;
        SourceText.Text = t.Source;
        SourceLink.NavigateUri = Uri.TryCreate(t.Source, UriKind.Absolute, out Uri? uri) ? uri : null;
        SourceLine.Visibility = Visibility.Visible;

        TierOptions.Children.Clear();
        _tierRadios.Clear();
        if (t.Type != Tariff.TypeTiered)
        {
            TierPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TariffTier current = PriceSchedule.SelectTier(t, Result.TariffTier);
        foreach (TariffTier tier in t.Tiers!)
        {
            var radio = new RadioButton
            {
                GroupName = "Tier",
                Tag = tier.Id,
                Content = $"{tier.Name.Get(tr)} · {Loc.Money(tier.Price, t.Currency, 4)}/kWh",
                IsChecked = tier.Id == current.Id,
                Margin = new Thickness(0, 0, 16, 4),
            };
            radio.Checked += (_, _) => Result.TariffTier = tier.Id;
            _tierRadios.Add(radio);
            TierOptions.Children.Add(radio);
        }
        double? limit = t.Tiers![0].LimitKwhPerDay;
        TierHint.Text = limit is double l
            ? Loc.F("SetTierHint", l.ToString("0.#", Loc.Culture), (l * 30).ToString("N0", Loc.Culture))
            : "";
        TierPanel.Visibility = Visibility.Visible;
    }

    private void ManualMode_Changed(object sender, RoutedEventArgs e) => UpdateManualMode();

    private void UpdateManualMode()
    {
        if (!_ready)
        {
            return;
        }
        bool tou = ManualTouRadio.IsChecked == true;
        Visibility flat = tou ? Visibility.Collapsed : Visibility.Visible;
        Visibility three = tou ? Visibility.Visible : Visibility.Collapsed;
        ManualPriceLabel.Visibility = ManualPriceBox.Visibility = flat;
        ManualDayLabel.Visibility = ManualDayBox.Visibility = three;
        ManualPeakLabel.Visibility = ManualPeakBox.Visibility = three;
        ManualNightLabel.Visibility = ManualNightBox.Visibility = three;
    }

    private void ShowTableInfo()
    {
        TariffTable table = _tariffs.Table;
        TableInfo.Text = Loc.F("TableInfo", table.Version, table.Updated,
            Loc.T(_tariffs.TableFromCache ? "TableDownloaded" : "TableBuiltIn"));
        TariffUpdateState state = _tariffs.ReadState();
        OnlineLast.Text = state.LastCheckUtc is DateTime last
            ? Loc.F("SetOnlineLast", last.ToLocalTime().ToString("g", Loc.Culture), state.LastResult ?? "")
            : Loc.T("SetOnlineNever");
    }

    private void SourceLink_Click(object sender, RoutedEventArgs e)
    {
        // Opens the source page in the user's browser; the app itself does not fetch it.
        if (SourceLink.NavigateUri is Uri uri && uri.Scheme == Uri.UriSchemeHttps)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                MessageBox.Show(this, ex.Message, Loc.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    // ------------------------------------------------------------------ general

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
        double tdp = Parse(TdpBox, "SetTdp", 1, MeterSettings.MaxWatts, errors);
        double idle = Parse(IdleBox, "SetIdle", 0, MeterSettings.MaxWatts, errors);

        bool componentsMode = OtherComponentsRadio.IsChecked == true;
        double other = componentsMode
            ? ParseOptional(OtherBox, Loc.T("SetOtherFixedMode"), 0, MeterSettings.MaxWatts, null, _original.OtherWatts)
            : Parse(OtherBox, "SetOtherFixedMode", 0, MeterSettings.MaxWatts, errors);
        ComponentEstimate components = ReadComponents(componentsMode ? errors : null);

        string? tariffId = SelectedTariffId;
        bool manual = tariffId == AppSettings.TariffManual;
        bool tou = ManualTouRadio.IsChecked == true;
        string currency = CurrencyBox.Text.Trim().ToUpperInvariant();
        if (manual && !(currency.Length == 3 && currency.All(char.IsAsciiLetterUpper)))
        {
            errors.Add(Loc.T("InvalidCurrency"));
        }
        const double minPrice = 0.0001;
        double flatPrice = manual && !tou
            ? Parse(ManualPriceBox, "SetManualPrice", minPrice, AppSettings.MaxManualPrice, errors)
            : ParseOptional(ManualPriceBox, "", 0, AppSettings.MaxManualPrice, null, _original.ManualPrice);
        double day = manual && tou
            ? Parse(ManualDayBox, "SetManualDay", minPrice, AppSettings.MaxManualPrice, errors)
            : ParseOptional(ManualDayBox, "", 0, AppSettings.MaxManualPrice, null, _original.ManualDayPrice);
        double peak = manual && tou
            ? Parse(ManualPeakBox, "SetManualPeak", minPrice, AppSettings.MaxManualPrice, errors)
            : ParseOptional(ManualPeakBox, "", 0, AppSettings.MaxManualPrice, null, _original.ManualPeakPrice);
        double night = manual && tou
            ? Parse(ManualNightBox, "SetManualNight", minPrice, AppSettings.MaxManualPrice, errors)
            : ParseOptional(ManualNightBox, "", 0, AppSettings.MaxManualPrice, null, _original.ManualNightPrice);

        if (errors.Count > 0)
        {
            ErrorText.Text = string.Join("\n", errors);
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        string folder = FolderBox.Text.Trim();
        string? tier = _tierRadios.FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? Result.TariffTier;
        Result = new AppSettings
        {
            SettingsVersion = AppSettings.CurrentVersion,
            IntervalSeconds = interval,
            PsuEfficiency = psu / 100.0,
            OtherMode = componentsMode ? AppSettings.OtherComponents : AppSettings.OtherFixed,
            OtherWatts = other,
            Components = components,
            CpuTdpWatts = tdp,
            CpuIdleWatts = Math.Min(idle, tdp),
            Language = LangTr.IsChecked == true ? AppSettings.LanguageTurkish
                : LangEn.IsChecked == true ? AppSettings.LanguageEnglish
                : AppSettings.LanguageAuto,
            CloseToTray = CloseToTrayBox.IsChecked == true,
            CsvEnabled = CsvBox.IsChecked == true,
            // The default folder is stored as "not set", so no user path ends up in the JSON.
            CsvFolder = folder.Length == 0 || string.Equals(folder, _defaultFolder, StringComparison.OrdinalIgnoreCase) ? null : folder,
            TariffId = tariffId,
            TariffTier = tier,
            // The region's currency is stored as "not set" as well.
            ManualCurrency = !(currency.Length == 3 && currency.All(char.IsAsciiLetterUpper)) ? _original.ManualCurrency
                : currency == TariffResolver.RegionCurrency(_regionCode) ? null
                : currency,
            ManualTimeOfUse = tou,
            ManualPrice = flatPrice,
            ManualDayPrice = day,
            ManualPeakPrice = peak,
            ManualNightPrice = night,
            OnlineTariffUpdate = OnlineBox.IsChecked == true,
        };
        DialogResult = true;
    }

    /// <summary>Accepts both the UI culture's decimal separator and '.'.</summary>
    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, Loc.Culture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static double Parse(TextBox box, string labelKey, double min, double max, List<string> errors)
    {
        string text = box.Text.Trim();
        if (TryParseNumber(text, out double value) && value >= min && value <= max)
        {
            return value;
        }
        errors.Add(Loc.F("InvalidValue", Loc.T(labelKey), min.ToString("0.####", Loc.Culture), max.ToString("0.##", Loc.Culture)));
        return double.NaN;
    }

    /// <summary>Like <see cref="Parse"/>, but with a fallback; reports an error only when <paramref name="errors"/> is given.</summary>
    private static double ParseOptional(TextBox box, string label, double min, double max, List<string>? errors, double fallback = 0)
    {
        string text = box.Text.Trim();
        if (TryParseNumber(text, out double value) && value >= min && value <= max)
        {
            return value;
        }
        if (text.Length == 0 && errors is null)
        {
            return fallback;
        }
        errors?.Add(Loc.F("InvalidValue", label, min.ToString("0.##", Loc.Culture), max.ToString("0.##", Loc.Culture)));
        return fallback;
    }

    private static int ParseInt(TextBox box, string label, List<string>? errors)
    {
        if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, Loc.Culture, out int value) &&
            value >= 0 && value <= ComponentEstimate.MaxCount)
        {
            return value;
        }
        errors?.Add(Loc.F("InvalidValue", label, 0, ComponentEstimate.MaxCount));
        return 0;
    }
}
