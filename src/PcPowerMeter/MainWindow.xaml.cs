using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PcPowerMeter.Core;
using PcPowerMeter.Localization;
using PcPowerMeter.Services;
using PcPowerMeter.Theming;

namespace PcPowerMeter;

public partial class MainWindow : Window
{
    private const double ChartWindowSeconds = 300;

    private readonly SamplerService _sampler;
    private readonly TrayIcon _tray;
    private readonly PowerHistory _history = new(ChartWindowSeconds);
    private readonly TableRow _gpuRow, _cpuRow, _otherRow, _dcRow, _wallRow;

    private AppSettings _settings;
    private SensorInfo? _sensors;
    private PowerSample? _sample;
    private SessionSnapshot? _session;
    private CsvStatus _csv = new(false, null, null);
    private string _warningKey = "";
    private bool _exiting;
    private bool _wallTagTurkish;
    private int _sessionId;

    private readonly UsageHistory _usage;
    private readonly TariffRepository _tariffs;
    private readonly string _regionCode = CurrentRegionCode();
    private TariffChoice _choice = TariffChoice.None;
    private HistoryStatus _historyStatus = new(null);
    private UsageWindow? _usageWindow;
    private double _sessionCost;
    private MonthlyProjection? _projection;
    private DateTime _projectionTime;
    private int _projectionRevision = -1;
    private bool _tariffCheckRunning;

    internal MainWindow(AppSettings settings, SensorOptions options, UsageHistory usage, TariffRepository tariffs)
    {
        InitializeComponent();
        _settings = settings;
        _usage = usage;
        _tariffs = tariffs;
        _choice = TariffResolver.Resolve(_tariffs.Table, _settings, _regionCode);

        BuildTableHeader();
        _gpuRow = AddRow(1);
        _cpuRow = AddRow(2);
        _otherRow = AddRow(3);
        _dcRow = AddRow(4, separatorAbove: true);
        _wallRow = AddRow(5, bold: true);
        BuildLegend();

        _tray = new TrayIcon(ShowFromTray, ResetSession, ExitApp);

        _sampler = new SamplerService(settings, options, usage);
        _sampler.SensorsReady += info => Dispatcher.BeginInvoke(() => { _sensors = info; Render(); });
        _sampler.SampleReady += (sample, session) => Dispatcher.BeginInvoke(() => OnSample(sample, session));
        _sampler.CsvStatusChanged += status => Dispatcher.BeginInvoke(() => { _csv = status; Render(); });
        _sampler.HistoryStatusChanged += status => Dispatcher.BeginInvoke(() => { _historyStatus = status; Render(); });

        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        ThemeManager.ThemeChanged += () => Chart.InvalidateVisual();

        Render();
        _sampler.Start();
        CheckTariffsOnline();
    }

    private static string CurrentRegionCode()
    {
        try
        {
            return System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName;
        }
        catch (ArgumentException)
        {
            return "";
        }
    }

    /// <summary>
    /// The app's only network access, and only when the user turned it on: fetch the tariff
    /// table at most once a day. Failures leave the built-in or cached table in place.
    /// </summary>
    private async void CheckTariffsOnline()
    {
        if (!_settings.OnlineTariffUpdate || _tariffCheckRunning)
        {
            return;
        }
        _tariffCheckRunning = true;
        try
        {
            TariffCheckResult result = await Task.Run(() => _tariffs.CheckOnlineAsync(AppPaths.Version, DateTime.UtcNow));
            if (result == TariffCheckResult.Updated)
            {
                _choice = TariffResolver.Resolve(_tariffs.Table, _settings, _regionCode);
                _projectionTime = default;
                Render();
            }
        }
        finally
        {
            _tariffCheckRunning = false;
        }
    }

    private void OnSample(PowerSample sample, SessionSnapshot session)
    {
        if (session.SessionId < _sessionId)
        {
            return; // measured before a reset, delivered after it
        }
        _sample = sample;
        _session = session;
        if (_choice.Schedule is PriceSchedule prices)
        {
            // Priced at the middle of the interval, so a three-time limit splits it fairly.
            DateTime middle = sample.Timestamp - TimeSpan.FromSeconds(sample.Energy.Seconds / 2);
            _sessionCost += sample.Energy.WallWh / 1000.0 * prices.PriceAt(middle);
        }
        _history.Add(new HistoryPoint(sample.ElapsedSeconds, sample.WallWatts, sample.DcWatts,
            sample.GpuWatts, sample.CpuWatts, sample.OtherWatts));

        string wall = sample.WallWatts.ToString("N0", Loc.Culture);
        _tray.SetTooltip(Loc.F(sample.GpuIncluded ? "TrayTip" : "TrayTipNoGpu", wall));

        if (IsVisible && WindowState != WindowState.Minimized)
        {
            Render();
            Chart.Update(_history.Points, ChartWindowSeconds);
        }
    }

    // ------------------------------------------------------------------ rendering

    private void Render()
    {
        PowerSample? s = _sample;
        SessionSnapshot? st = _session;
        SensorInfo? sensors = _sensors;

        WallValue.Text = s is null ? "–" : s.WallWatts.ToString("N0", Loc.Culture);
        if (WallTag.Content is null || Loc.IsTurkish != _wallTagTurkish)
        {
            _wallTagTurkish = Loc.IsTurkish;
            WallTag.Content = MakeTag(Kind.Estimated);
        }
        WallDetail.Text = s is null
            ? Loc.T("Detecting")
            : $"DC {W(s.DcWatts)} ÷ {Pct(s.PsuEfficiency)}" +
              (s.GpuIncluded ? "" : "  ·  " + Loc.T("RowDcNoGpu"));
        RenderCost(s);

        // GPU
        bool gpuFound = sensors is not null && sensors.GpuSource != PowerSource.None;
        _gpuRow.Set(Loc.T("RowGpu"), sensors?.GpuName,
            s?.GpuWatts is not null ? Kind.Measured : sensors is null ? Kind.Pending : Kind.Unavailable,
            gpuFound ? SourceText(sensors!.GpuSource) : sensors is null ? "" : Loc.T("SrcNone"),
            s?.GpuWatts, st?.Gpu);

        // CPU
        PowerSource cpuSource = sensors?.CpuSource ?? PowerSource.None;
        Kind cpuKind = sensors is null ? Kind.Pending
            : s is not null && s.CpuWatts is null ? Kind.Unavailable
            : cpuSource == PowerSource.EnergyMeter ? Kind.Measured : Kind.Estimated;
        _cpuRow.Set(Loc.T("RowCpu"), sensors?.CpuName, cpuKind, SourceText(cpuSource), s?.CpuWatts, st?.Cpu);

        // Other, DC, wall
        bool components = _settings.OtherMode == AppSettings.OtherComponents;
        _otherRow.Set(Loc.T("RowOther"),
            components ? Loc.F("OtherHover", _settings.Components.Breakdown().Count(i => i.Watts > 0)) : Loc.T("OtherFixedHover"),
            Kind.Estimated, Loc.T(components ? "SrcComponents" : "SrcFixed"), s?.OtherWatts, st?.Other);
        _otherRow.SetToolTip(OtherBreakdown);
        bool anyMeasured = s is not null && (s.GpuIncluded || s.CpuSource == PowerSource.EnergyMeter);
        // With a GPU sensor, intervals in which it failed are in the DC/wall totals without
        // GPU energy; say how many, so the averages are not mistaken for full measurements.
        string? gpuGapNote = gpuFound && st is { GpuMissingSamples: > 0 }
            ? Loc.F("RowNoGpuIntervals", st.GpuMissingSamples, st.Samples)
            : null;
        _dcRow.Set(Loc.T(s is null || s.GpuIncluded ? "RowDc" : "RowDcNoGpu"), gpuGapNote,
            anyMeasured ? Kind.Mixed : Kind.Estimated, Loc.T("SrcSum"), s?.DcWatts, st?.Dc,
            warn: s is not null && !s.GpuIncluded);
        _wallRow.Set(Loc.F("RowWall", Pct(s?.PsuEfficiency ?? _settings.PsuEfficiency)), gpuGapNote,
            Kind.Estimated, Loc.T("SrcPsu"), s?.WallWatts, st?.Wall);

        RenderWarnings(s, st, sensors);

        // Footer
        if (st is not null)
        {
            string text = $"{Loc.T("Session")}: {TimeSpan.FromSeconds(Math.Floor(st.ElapsedSeconds)):hh\\:mm\\:ss}" +
                          $"  ·  {st.Samples.ToString("N0", Loc.Culture)} {Loc.T("Samples")}";
            if (s?.GpuLoadPercent is double gl)
            {
                text += $"  ·  {Loc.T("GpuLoad")} {Loc.Percent(gl)}";
            }
            if (s?.CpuLoadPercent is double cl)
            {
                text += $"  ·  {Loc.T("CpuLoad")} {Loc.Percent(cl)}";
            }
            if (st.SkippedGaps > 0)
            {
                text += "  ·  " + Loc.F("GapsSkipped", st.SkippedGaps);
            }
            SessionText.Text = text;
        }
        else
        {
            SessionText.Text = Loc.T("Detecting");
        }

        CsvText.Text = _csv.Active ? Loc.F("CsvOn", _csv.FilePath)
            : _csv.Error is not null ? Loc.F("CsvError", _csv.Error)
            : Loc.T("CsvOff");
        CsvText.ToolTip = _csv.FilePath;
        OpenFolderLink.Visibility = _settings.CsvEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderWarnings(PowerSample? s, SessionSnapshot? st, SensorInfo? sensors)
    {
        var warnings = new List<string>();
        if (sensors is not null)
        {
            if (sensors.GpuSource == PowerSource.None)
            {
                warnings.Add(Loc.T("WarnGpuMissing"));
            }
            else if (st is { GpuMissingSamples: > 0 })
            {
                warnings.Add(Loc.F("WarnGpuSamples", st.GpuMissingSamples));
            }
            if (sensors.CpuSource == PowerSource.LoadEstimate)
            {
                MeterSettings m = _settings.ToMeterSettings();
                warnings.Add(Loc.F("WarnCpuEstimate", m.CpuIdleWatts.ToString("N0", Loc.Culture),
                    m.CpuTdpWatts.ToString("N0", Loc.Culture)));
            }
            else if (s is not null && s.CpuWatts is null)
            {
                warnings.Add(Loc.T("WarnCpuWaiting"));
            }
        }
        if (!_csv.Active && _csv.Error is not null)
        {
            warnings.Add(Loc.F("CsvError", _csv.Error));
        }
        if (_historyStatus.Error is not null)
        {
            warnings.Add(Loc.F("HistoryError", _historyStatus.Error));
        }

        string key = string.Join("\n", warnings);
        if (key == _warningKey)
        {
            return;
        }
        _warningKey = key;
        WarningPanel.Children.Clear();
        foreach (string text in warnings)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 7, 12, 7),
                Margin = new Thickness(0, 0, 0, 8),
                BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = "⚠  " + text, TextWrapping = TextWrapping.Wrap },
            };
            border.SetResourceReference(Border.BackgroundProperty, "B.WarningBg");
            border.SetResourceReference(Border.BorderBrushProperty, "B.Estimated");
            ((TextBlock)border.Child).SetResourceReference(TextBlock.ForegroundProperty, "B.WarningFg");
            WarningPanel.Children.Add(border);
        }
    }

    /// <summary>Live cost line: cost per hour at the current wall power, session cost, monthly estimate.</summary>
    private void RenderCost(PowerSample? s)
    {
        if (_choice.Schedule is not PriceSchedule prices)
        {
            CostText.Text = Loc.T("CostNone");
            return;
        }
        DateTime now = DateTime.Now;
        // Once a minute, and as soon as the history has been loaded at start-up.
        bool historyArrived = _projection is null && _usage.Revision != _projectionRevision;
        if (historyArrived || now - _projectionTime > TimeSpan.FromMinutes(1) || now < _projectionTime)
        {
            _projectionTime = now;
            _projectionRevision = _usage.Revision;
            _projection = UsageReport.Projection(_usage.Snapshot(), now, prices);
        }
        string perHour = s is null ? "–" : Money(s.WallWatts / 1000.0 * prices.PriceAt(now), prices.Currency);
        string text = Loc.F("CostLine", perHour, Money(_sessionCost, prices.Currency));
        if (_projection is { Cost: double monthly })
        {
            text += "  ·  " + Loc.F("CostMonthly", Money(monthly, prices.Currency), _projection.Days);
        }
        CostText.Text = text;
    }

    /// <summary>Small amounts get a third decimal, so a few cents per hour do not show as 0.</summary>
    internal static string Money(double amount, string currency) =>
        Loc.Money(amount, currency, amount != 0 && Math.Abs(amount) < 1 ? 3 : 2);

    /// <summary>Tooltip content for the "other components" row.</summary>
    private UIElement OtherBreakdown()
    {
        var panel = new StackPanel { MaxWidth = 420 };
        panel.Children.Add(new TextBlock { Text = Loc.T("BreakdownTitle"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        if (_settings.OtherMode != AppSettings.OtherComponents)
        {
            panel.Children.Add(new TextBlock
            {
                Text = Loc.F("BreakdownFixed", _settings.OtherWatts.ToString("N1", Loc.Culture)),
                TextWrapping = TextWrapping.Wrap,
            });
            return panel;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        IReadOnlyList<ComponentItem> items = _settings.Components.Breakdown();
        int row = 0;
        void AddLine(string label, string detail, string watts, bool bold)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var cells = new[]
            {
                new TextBlock { Text = label, Margin = new Thickness(0, 1, 16, 1) },
                new TextBlock { Text = detail, Margin = new Thickness(0, 1, 16, 1), HorizontalAlignment = HorizontalAlignment.Right },
                new TextBlock { Text = watts, Margin = new Thickness(0, 1, 0, 1), HorizontalAlignment = HorizontalAlignment.Right },
            };
            for (int c = 0; c < cells.Length; c++)
            {
                if (bold)
                {
                    cells[c].FontWeight = FontWeights.SemiBold;
                }
                if (c == 1)
                {
                    cells[c].SetResourceReference(TextBlock.ForegroundProperty, "B.SubText");
                }
                Grid.SetRow(cells[c], row);
                Grid.SetColumn(cells[c], c);
                grid.Children.Add(cells[c]);
            }
            row++;
        }
        foreach (ComponentItem item in items)
        {
            string detail = item.Key is "Board" or "Cooler" or "Rgb" or "Usb" or "Extra"
                ? ""
                : $"{item.Count} × {item.UnitWatts.ToString("0.##", Loc.Culture)} W";
            AddLine(Loc.T("Comp" + item.Key), detail, item.Watts.ToString("N1", Loc.Culture) + " W", false);
        }
        AddLine(Loc.T("BreakdownTotal"), "", items.Sum(i => i.Watts).ToString("N1", Loc.Culture) + " W", true);
        panel.Children.Add(grid);
        var note = new TextBlock { Text = Loc.T("BreakdownNote"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), FontSize = 11 };
        note.SetResourceReference(TextBlock.ForegroundProperty, "B.SubText");
        panel.Children.Add(note);
        return panel;
    }

    private static string SourceText(PowerSource source) => source switch
    {
        PowerSource.Nvml => Loc.T("SrcNvml"),
        PowerSource.NvidiaSmi => Loc.T("SrcNvidiaSmi"),
        PowerSource.EnergyMeter => Loc.T("SrcEnergyMeter"),
        PowerSource.LoadEstimate => Loc.T("SrcLoadEstimate"),
        PowerSource.Fixed => Loc.T("SrcFixed"),
        _ => Loc.T("SrcNone"),
    };

    private static string W(double? watts) => watts is null ? "–" : watts.Value.ToString("N1", Loc.Culture) + " W";

    private static string Wh(double wh) => wh.ToString("N2", Loc.Culture) + " Wh";

    private static string Pct(double fraction) => Loc.Percent(fraction * 100);

    // ------------------------------------------------------------------ table

    private enum Kind { Pending, Measured, Estimated, Mixed, Unavailable }

    private static FrameworkElement MakeTag(Kind kind)
    {
        (string text, string brush) = kind switch
        {
            Kind.Measured => (Loc.T("TagMeasured"), "B.Measured"),
            Kind.Estimated => (Loc.T("TagEstimated"), "B.Estimated"),
            Kind.Mixed => (Loc.T("TagMixed"), "B.SubText"),
            Kind.Unavailable => (Loc.T("TagUnavailable"), "B.Unavailable"),
            _ => ("…", "B.SubText"),
        };
        var label = new TextBlock { Text = text, FontSize = 11 };
        label.SetResourceReference(TextBlock.ForegroundProperty, brush);
        var border = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 0, 6, 1),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = label,
        };
        border.SetResourceReference(Border.BorderBrushProperty, brush);
        return border;
    }

    private static readonly string[] HeaderKeys = { "ColComponent", "ColSource", "", "ColNow", "ColAvg", "ColMax", "ColEnergy" };

    private void BuildTableHeader()
    {
        Table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 150 });
        Table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        for (int i = 0; i < 4; i++)
        {
            Table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        }
        for (int i = 0; i < 6; i++)
        {
            Table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (int c = 0; c < HeaderKeys.Length; c++)
        {
            if (HeaderKeys[c].Length == 0)
            {
                continue;
            }
            var header = new TextBlock
            {
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6),
                HorizontalAlignment = c >= 3 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            };
            header.SetResourceReference(TextBlock.TextProperty, "L." + HeaderKeys[c]);
            header.SetResourceReference(TextBlock.ForegroundProperty, "B.SubText");
            Grid.SetColumn(header, c);
            Table.Children.Add(header);
        }
    }

    private TableRow AddRow(int row, bool separatorAbove = false, bool bold = false)
    {
        var r = new TableRow(Table, row, separatorAbove, bold);
        return r;
    }

    /// <summary>The text cells of one table row.</summary>
    private sealed class TableRow
    {
        private readonly TextBlock _name = new() { FontWeight = FontWeights.SemiBold };
        private readonly TextBlock _detail = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly ContentControl _tag = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _source = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        private readonly TextBlock[] _values = new TextBlock[4];
        private Kind _kind = (Kind)(-1);
        private bool _turkish;
        private readonly StackPanel _nameStack;
        private ToolTip? _tip;

        public TableRow(Grid grid, int row, bool separatorAbove, bool bold)
        {
            var margin = new Thickness(0, separatorAbove ? 8 : 3, 0, 3);
            if (separatorAbove)
            {
                var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
                line.SetResourceReference(Border.BorderBrushProperty, "B.Border");
                Grid.SetRow(line, row);
                Grid.SetColumnSpan(line, 7);
                grid.Children.Add(line);
            }
            if (!bold)
            {
                _name.FontWeight = FontWeights.Normal;
            }
            _detail.SetResourceReference(TextBlock.ForegroundProperty, "B.SubText");
            _source.SetResourceReference(TextBlock.ForegroundProperty, "B.SubText");

            var nameStack = new StackPanel { Margin = margin, VerticalAlignment = VerticalAlignment.Center };
            nameStack.Children.Add(_name);
            nameStack.Children.Add(_detail);
            Place(grid, nameStack, row, 0);
            _nameStack = nameStack;

            var tagStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = margin, VerticalAlignment = VerticalAlignment.Center };
            tagStack.Children.Add(_tag);
            tagStack.Children.Add(_source);
            Place(grid, tagStack, row, 1);

            for (int i = 0; i < 4; i++)
            {
                _values[i] = new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = margin,
                    FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                    FontFamily = new FontFamily("Segoe UI"),
                };
                Typography.SetNumeralAlignment(_values[i], FontNumeralAlignment.Tabular);
                Place(grid, _values[i], row, 3 + i);
            }
        }

        public void Set(string name, string? detail, Kind kind, string source, double? now, StatSnapshot? stat, bool warn = false)
        {
            _name.Text = name;
            if (warn)
            {
                _name.SetResourceReference(TextBlock.ForegroundProperty, "B.Unavailable");
            }
            else
            {
                _name.SetResourceReference(TextBlock.ForegroundProperty, "B.Text");
            }
            _detail.Text = detail ?? "";
            _detail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
            if (kind != _kind || Loc.IsTurkish != _turkish)
            {
                _kind = kind;
                _turkish = Loc.IsTurkish;
                _tag.Content = MakeTag(kind);
            }
            _source.Text = source;
            _values[0].Text = W(now);
            _values[1].Text = W(stat?.Average);
            _values[2].Text = W(stat?.Max);
            _values[3].Text = stat is null ? "–" : Wh(stat.Value.EnergyWh);
        }

        /// <summary>
        /// Shows <paramref name="content"/> when the pointer rests on the row name, and also on
        /// a click. The content is built when the tooltip opens, so it is always current.
        /// </summary>
        public void SetToolTip(Func<UIElement> content)
        {
            if (_tip is not null)
            {
                return;
            }
            var tip = new ToolTip { Padding = new Thickness(10, 8, 10, 8), PlacementTarget = _nameStack };
            tip.SetResourceReference(Control.BackgroundProperty, "B.Surface");
            tip.SetResourceReference(Control.ForegroundProperty, "B.Text");
            tip.SetResourceReference(Control.BorderBrushProperty, "B.Border");
            tip.Opened += (_, _) => tip.Content = content();
            _tip = tip;
            _nameStack.Background = Brushes.Transparent; // hit-testable across the whole cell
            _nameStack.Cursor = System.Windows.Input.Cursors.Hand;
            _nameStack.ToolTip = tip;
            ToolTipService.SetShowDuration(_nameStack, 60000);
            ToolTipService.SetInitialShowDelay(_nameStack, 250);
            _nameStack.MouseLeftButtonUp += (_, _) => tip.IsOpen = !tip.IsOpen;
        }

        private static void Place(Grid grid, UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }
    }

    private void BuildLegend()
    {
        (string key, string brush)[] items =
        {
            ("L.LegendWall", "B.SeriesWall"), ("L.LegendDc", "B.SeriesDc"), ("L.LegendGpu", "B.SeriesGpu"),
            ("L.LegendCpu", "B.SeriesCpu"), ("L.LegendOther", "B.SeriesOther"),
        };
        foreach (var (key, brush) in items)
        {
            var swatch = new Border { Width = 14, Height = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(12, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            swatch.SetResourceReference(Border.BackgroundProperty, brush);
            var label = new TextBlock { FontSize = 12 };
            label.SetResourceReference(TextBlock.TextProperty, key);
            label.SetResourceReference(TextBlock.ForegroundProperty, "B.SubText");
            var item = new StackPanel { Orientation = Orientation.Horizontal };
            item.Children.Add(swatch);
            item.Children.Add(label);
            Legend.Children.Add(item);
        }
    }

    // ------------------------------------------------------------------ actions

    private void Reset_Click(object sender, RoutedEventArgs e) => ResetSession();

    private void Usage_Click(object sender, RoutedEventArgs e)
    {
        if (_usageWindow is null)
        {
            _usageWindow = new UsageWindow(_usage, () => _choice, () => _tariffs.Table) { Owner = this };
            _usageWindow.Closed += (_, _) => _usageWindow = null;
            _usageWindow.Show();
        }
        else
        {
            if (_usageWindow.WindowState == WindowState.Minimized)
            {
                _usageWindow.WindowState = WindowState.Normal;
            }
            _usageWindow.Activate();
        }
    }

    private void ResetSession()
    {
        _sessionId++;
        _sessionCost = 0;
        _sampler.ResetSession();
        _history.Clear();
        _sample = null;
        _session = null;
        Chart.Update(_history.Points, ChartWindowSeconds);
        Render();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings.Clone(), SamplerService.DefaultCsvFolder, _tariffs, _regionCode) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        AppSettings updated = dialog.Result.Normalized();
        bool languageChanged = updated.Language != _settings.Language;
        _settings = updated;
        _choice = TariffResolver.Resolve(_tariffs.Table, _settings, _regionCode);
        _projectionTime = default;
        if (!SettingsStore.Save(updated, AppPaths.SettingsPath))
        {
            MessageBox.Show(this, Loc.T("SaveFailed"), Loc.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        if (languageChanged)
        {
            Loc.Apply(updated.Language, Application.Current.Resources);
            _tray.UpdateTexts();
            _warningKey = "";
        }
        _sampler.UpdateSettings(updated);
        _usageWindow?.Refresh(force: true);
        Render();
        CheckTariffsOnline();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        string folder = SamplerService.ResolveCsvFolder(_settings);
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            MessageBox.Show(this, ex.Message, Loc.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        Render();
        Chart.Update(_history.Points, ChartWindowSeconds);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState != WindowState.Minimized)
        {
            Render();
            Chart.Update(_history.Points, ChartWindowSeconds);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && _settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            _tray.ShowStillRunningOnce();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Shutdown();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    /// <summary>Removes the tray icon; used by the global error handler. Safe to call twice.</summary>
    internal void RemoveTrayIcon() => _tray.Dispose();

    private void Shutdown()
    {
        _exiting = true;
        _sampler.Dispose();
        _tray.Dispose();
        Application.Current.Shutdown();
    }
}
