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

    internal MainWindow(AppSettings settings, SensorOptions options)
    {
        InitializeComponent();
        _settings = settings;

        BuildTableHeader();
        _gpuRow = AddRow(1);
        _cpuRow = AddRow(2);
        _otherRow = AddRow(3);
        _dcRow = AddRow(4, separatorAbove: true);
        _wallRow = AddRow(5, bold: true);
        BuildLegend();

        _tray = new TrayIcon(ShowFromTray, ResetSession, ExitApp);

        _sampler = new SamplerService(settings, options);
        _sampler.SensorsReady += info => Dispatcher.BeginInvoke(() => { _sensors = info; Render(); });
        _sampler.SampleReady += (sample, session) => Dispatcher.BeginInvoke(() => OnSample(sample, session));
        _sampler.CsvStatusChanged += status => Dispatcher.BeginInvoke(() => { _csv = status; Render(); });

        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        ThemeManager.ThemeChanged += () => Chart.InvalidateVisual();

        Render();
        _sampler.Start();
    }

    private void OnSample(PowerSample sample, SessionSnapshot session)
    {
        if (session.SessionId < _sessionId)
        {
            return; // measured before a reset, delivered after it
        }
        _sample = sample;
        _session = session;
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
        _otherRow.Set(Loc.T("RowOther"), null, Kind.Estimated, Loc.T("SrcFixed"), s?.OtherWatts, st?.Other);
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

    private void ResetSession()
    {
        _sessionId++;
        _sampler.ResetSession();
        _history.Clear();
        _sample = null;
        _session = null;
        Chart.Update(_history.Points, ChartWindowSeconds);
        Render();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings.Clone(), SamplerService.DefaultCsvFolder) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        AppSettings updated = dialog.Result.Normalized();
        bool languageChanged = updated.Language != _settings.Language;
        _settings = updated;
        if (!SettingsStore.Save(updated, SettingsStore.DefaultPath))
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
        Render();
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
