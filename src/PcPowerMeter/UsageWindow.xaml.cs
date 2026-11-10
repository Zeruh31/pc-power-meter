using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using PcPowerMeter.Controls;
using PcPowerMeter.Core;
using PcPowerMeter.Localization;
using PcPowerMeter.Theming;

namespace PcPowerMeter;

/// <summary>Usage history: period totals, daily chart, monthly summary, export and reset.</summary>
public partial class UsageWindow : Window
{
    private static readonly string[] SummaryHeaders = { "ColPeriod", "ColKwh", "ColCost", "ColMeasured", "ColNoGpu" };
    private static readonly string[] MonthlyHeaders = { "ColMonth", "ColKwh", "ColCost", "ColMeasured", "ColNoGpu" };

    private readonly UsageHistory _usage;
    private readonly Func<TariffChoice> _choice;
    private readonly Func<TariffTable> _table;
    private readonly DispatcherTimer _timer;
    private int _revision = -1;

    internal UsageWindow(UsageHistory usage, Func<TariffChoice> choice, Func<TariffTable> table)
    {
        InitializeComponent();
        _usage = usage;
        _choice = choice;
        _table = table;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        ThemeManager.ThemeChanged += OnThemeChanged;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += (_, _) => Refresh(force: false);
        _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            ThemeManager.ThemeChanged -= OnThemeChanged;
        };
        Refresh(force: true);
    }

    private void OnThemeChanged() => Chart.InvalidateVisual();

    /// <summary>Recomputes everything; without <paramref name="force"/> only when the history changed.</summary>
    internal void Refresh(bool force)
    {
        int revision = _usage.Revision;
        if (!force && revision == _revision)
        {
            return;
        }
        _revision = revision;

        List<HourlyUsage> hours = _usage.Snapshot();
        TariffChoice choice = _choice();
        PriceSchedule? prices = choice.Schedule;
        DateTime now = DateTime.Now;
        DateTime today = now.Date;
        var month = new DateTime(now.Year, now.Month, 1);
        DateTime week = UsageReport.WeekStart(now, Loc.Culture.DateTimeFormat.FirstDayOfWeek);

        var summary = new List<(string, UsageTotals)>
        {
            (Loc.T("PeriodToday"), UsageReport.Sum(hours, today, today.AddDays(1), prices)),
            (Loc.T("PeriodWeek"), UsageReport.Sum(hours, week, week.AddDays(7), prices)),
            (Loc.T("PeriodMonth"), UsageReport.Sum(hours, month, month.AddMonths(1), prices)),
            (Loc.T("Period12Months"), UsageReport.Sum(hours, month.AddMonths(-11), month.AddMonths(1), prices)),
            (Loc.T("PeriodAll"), UsageReport.Total(hours, prices)),
        };
        FillTable(SummaryTable, SummaryHeaders, summary.Select(r => Cells(r.Item1, r.Item2, prices)).ToList(), boldFirst: false);

        MonthlyProjection? projection = UsageReport.Projection(hours, now, prices);
        ProjectionText.Text = projection is null
            ? Loc.T("UsageEmpty")
            : projection.Cost is double cost
                ? Loc.F("UsageProjection", projection.Days, projection.WallKwh.ToString("N1", Loc.Culture), Loc.Money(cost, prices!.Currency))
                : Loc.F("UsageProjectionNoCost", projection.Days, projection.WallKwh.ToString("N1", Loc.Culture));

        List<UsageBucket> days = UsageReport.Daily(hours, today.AddDays(-29), today, prices);
        Chart.Update(days, prices?.Currency);

        List<UsageBucket> months = UsageReport.Monthly(hours, now, 12, prices);
        FillTable(MonthlyTable, MonthlyHeaders,
            months.Select(m => Cells(m.Start.ToString("MMMM yyyy", Loc.DateCulture), m.Totals, prices)).ToList(), boldFirst: false);

        TariffText.Text = Loc.F("TariffLine", Describe(choice, _table(), Loc.IsTurkish));
    }

    private static string[] Cells(string label, UsageTotals t, PriceSchedule? prices) => new[]
    {
        label,
        Loc.Kwh(t.WallKwh),
        t.Cost is double c && prices is not null ? MainWindow.Money(c, prices.Currency) : "–",
        Loc.Duration(t.MeasuredSeconds),
        t.NoGpuSeconds > 0 ? Loc.Duration(t.NoGpuSeconds) : "–",
    };

    /// <summary>A one-line description of the tariff in use, e.g. for the usage window footer.</summary>
    internal static string Describe(TariffChoice choice, TariffTable table, bool turkish)
    {
        if (choice.Schedule is not PriceSchedule prices)
        {
            return Loc.T("NoPrice");
        }
        if (choice.Manual || choice.Tariff is null)
        {
            return $"{Loc.T("TariffManual")}: {PriceSummary(prices, null)}";
        }
        Tariff t = choice.Tariff;
        string country = table.CountryOf(t)?.Name.Get(turkish) ?? "";
        string name = $"{country} – {t.Name.Get(turkish)}";
        if (choice.Tier is not null)
        {
            name += $" ({choice.Tier.Name.Get(turkish)})";
        }
        string valid = Loc.F("TariffValidFrom", t.ValidFrom) + (t.DataPeriod is null ? "" : ", " + Loc.F("TariffDataPeriod", t.DataPeriod));
        return $"{name}: {PriceSummary(prices, t)} · {valid}";
    }

    /// <summary>"3,2380 TL/kWh" or "night 3,2607 TL, day 4,9204 TL, peak 6,9958 TL /kWh" style summary.</summary>
    /// <param name="tariff">The table tariff, for its own period names; null for the manual price.</param>
    internal static string PriceSummary(PriceSchedule prices, Tariff? tariff)
    {
        if (!prices.IsTimeOfUse)
        {
            return Loc.Money(prices.PriceAt(DateTime.Today), prices.Currency, 4) + "/kWh";
        }
        return string.Join(", ", prices.PeriodPrices.Select(p =>
            $"{PeriodName(p.Period, tariff)} {Loc.Money(p.Price, prices.Currency, 4)}")) + " /kWh";
    }

    /// <summary>The tariff's own name for a period, the built-in name for day / peak / night, or a neutral label.</summary>
    private static string PeriodName(string id, Tariff? tariff) =>
        tariff?.Periods?.FirstOrDefault(p => p.Id == id)?.Name.Get(Loc.IsTurkish)
        ?? Loc.TryT("Tou_" + id)
        ?? Loc.T("TouOther");

    private static void FillTable(Grid grid, string[] headerKeys, List<string[]> rows, bool boldFirst)
    {
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
        for (int c = 1; c < headerKeys.Length; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        }
        for (int r = 0; r <= rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (int c = 0; c < headerKeys.Length; c++)
        {
            var header = new TextBlock
            {
                Text = Loc.T(headerKeys[c]),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = c == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, "B.SubText");
            Grid.SetColumn(header, c);
            grid.Children.Add(header);
        }
        for (int r = 0; r < rows.Count; r++)
        {
            for (int c = 0; c < rows[r].Length; c++)
            {
                var cell = new TextBlock
                {
                    Text = rows[r][c],
                    Margin = new Thickness(0, 2, 0, 2),
                    HorizontalAlignment = c == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                    FontWeight = boldFirst && c == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                };
                System.Windows.Documents.Typography.SetNumeralAlignment(cell, FontNumeralAlignment.Tabular);
                Grid.SetRow(cell, r + 1);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
    }

    // ------------------------------------------------------------------ actions

    private void ExportHourly_Click(object sender, RoutedEventArgs e) =>
        Export("pc-power-usage-hourly.csv", prices => UsageReport.ExportHourly(_usage.Snapshot(), prices));

    private void ExportDaily_Click(object sender, RoutedEventArgs e) =>
        Export("pc-power-usage-daily.csv", prices => UsageReport.ExportDaily(_usage.Snapshot(), prices));

    private void Export(string fileName, Func<PriceSchedule?, string> build)
    {
        var dialog = new SaveFileDialog
        {
            FileName = fileName,
            DefaultExt = ".csv",
            Filter = "CSV (*.csv)|*.csv",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        try
        {
            File.WriteAllText(dialog.FileName, build(_choice().Schedule), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, Loc.F("ExportFailed", ex.Message), Loc.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ResetHistory_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult answer = MessageBox.Show(this, Loc.T("ResetHistoryConfirm"), Loc.T("AppTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }
        var button = (Button)sender;
        button.IsEnabled = false;
        try
        {
            // Clearing writes the file; keep the disk off the UI thread.
            if (!await Task.Run(_usage.Clear))
            {
                MessageBox.Show(this, Loc.F("HistoryError", _usage.LastError), Loc.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            button.IsEnabled = true;
        }
        Refresh(force: true);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
