using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PcPowerMeter.Core;
using PcPowerMeter.Localization;
using PcPowerMeter.Theming;

namespace PcPowerMeter.Controls;

/// <summary>
/// A dependency-free bar chart of daily wall energy (kWh, estimate). Hovering a bar shows
/// its date, energy, cost and measured time.
/// </summary>
internal sealed class DailyChart : FrameworkElement
{
    private const double LeftPad = 48, RightPad = 6, TopPad = 8, BottomPad = 20;

    private IReadOnlyList<UsageBucket> _days = Array.Empty<UsageBucket>();
    private string? _currency;
    private readonly ToolTip _tip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
    private int _hover = -1;

    public DailyChart()
    {
        _tip.SetResourceReference(Control.BackgroundProperty, "B.Surface");
        _tip.SetResourceReference(Control.ForegroundProperty, "B.Text");
        _tip.SetResourceReference(Control.BorderBrushProperty, "B.Border");
        ToolTip = _tip;
        ToolTipService.SetInitialShowDelay(this, 0);
        ToolTipService.SetShowDuration(this, 60000);
    }

    public void Update(IReadOnlyList<UsageBucket> days, string? currency)
    {
        _days = days;
        _currency = currency;
        InvalidateVisual();
    }

    private Rect Plot => new(LeftPad, TopPad, Math.Max(0, ActualWidth - LeftPad - RightPad), Math.Max(0, ActualHeight - TopPad - BottomPad));

    protected override void OnRender(DrawingContext dc)
    {
        Rect plot = Plot;
        if (plot.Width < 20 || plot.Height < 20)
        {
            return;
        }
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        double max = _days.Count > 0 ? _days.Max(d => d.Totals.WallKwh) : 0;
        (double yMax, int steps) = NiceScale(max);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Brush sub = ThemeManager.Brush("SubText");
        var gridPen = new Pen(ThemeManager.Brush("Grid"), 1);
        gridPen.Freeze();

        for (int i = 0; i <= steps; i++)
        {
            double v = yMax * i / steps;
            double y = Math.Round(plot.Bottom - plot.Height * i / steps) + 0.5;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = new FormattedText(v.ToString(yMax < 1 ? "0.0#" : yMax < 10 ? "0.#" : "0", Loc.Culture),
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, sub, pixelsPerDip);
            dc.DrawText(label, new Point(plot.Left - 6 - label.Width, y - label.Height / 2));
        }

        if (_days.Count == 0)
        {
            return;
        }
        double slot = plot.Width / _days.Count;
        double barWidth = Math.Max(1, slot * 0.7);
        Brush bar = ThemeManager.Brush("SeriesWall");
        Brush accent = ThemeManager.Brush("Accent");
        for (int i = 0; i < _days.Count; i++)
        {
            double kwh = _days[i].Totals.WallKwh;
            double h = plot.Height * Math.Clamp(kwh / yMax, 0, 1);
            double x = plot.Left + slot * i + (slot - barWidth) / 2;
            if (h > 0)
            {
                dc.DrawRectangle(i == _hover ? accent : bar, null, new Rect(x, plot.Bottom - h, barWidth, h));
            }
            // Date labels every 5 days, counted back from today so today is always labelled.
            if ((_days.Count - 1 - i) % 5 == 0)
            {
                var label = new FormattedText(_days[i].Start.ToString("d MMM", Loc.DateCulture), CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, typeface, 11, sub, pixelsPerDip);
                double lx = Math.Clamp(x + barWidth / 2 - label.Width / 2, 0, ActualWidth - label.Width);
                dc.DrawText(label, new Point(lx, plot.Bottom + 3));
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Rect plot = Plot;
        double x = e.GetPosition(this).X;
        int index = _days.Count == 0 || plot.Width <= 0 ? -1 : (int)Math.Floor((x - plot.Left) / (plot.Width / _days.Count));
        if (index < 0 || index >= _days.Count)
        {
            index = -1;
        }
        if (index == _hover)
        {
            return;
        }
        _hover = index;
        if (index >= 0)
        {
            UsageBucket d = _days[index];
            string text = $"{d.Start.ToString("D", Loc.DateCulture)}\n{Loc.Kwh(d.Totals.WallKwh)} kWh";
            if (d.Totals.Cost is double cost && _currency is not null)
            {
                text += " · " + Loc.Money(cost, _currency);
            }
            text += $"\n{Loc.T("ColMeasured")}: {Loc.Duration(d.Totals.MeasuredSeconds)}";
            _tip.Content = text;
            _tip.IsOpen = true;
        }
        else
        {
            _tip.IsOpen = false;
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        _tip.IsOpen = false;
        InvalidateVisual();
    }

    /// <summary>A round axis maximum above the largest value (at least 0.5 kWh).</summary>
    internal static (double Max, int Steps) NiceScale(double max)
    {
        double target = Math.Max(max * 1.1, 0.5);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(target)));
        foreach (double s in new[] { 1, 2, 2.5, 5 })
        {
            if (s * magnitude >= target)
            {
                return (s * magnitude, s is 2.5 or 5 ? 5 : 4); // keeps the grid steps round
            }
        }
        return (10 * magnitude, 4);
    }
}
