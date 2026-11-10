using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PcPowerMeter.Core;
using PcPowerMeter.Theming;

namespace PcPowerMeter.Controls;

/// <summary>
/// A small dependency-free line chart of the recent power history: wall, DC total,
/// GPU, CPU and other components, newest on the right.
/// </summary>
internal sealed class PowerChart : FrameworkElement
{
    private const double LeftPad = 48, RightPad = 10, TopPad = 8, BottomPad = 20;

    private IReadOnlyList<HistoryPoint> _points = Array.Empty<HistoryPoint>();
    private double _windowSeconds = 300;

    public void Update(IReadOnlyList<HistoryPoint> points, double windowSeconds)
    {
        _points = points;
        _windowSeconds = windowSeconds;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        double plotW = w - LeftPad - RightPad, plotH = h - TopPad - BottomPad;
        if (plotW < 20 || plotH < 20)
        {
            return;
        }
        // Hit-testable background so the control has a size in the visual tree.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        // The newest point is always at the right edge; the chart scrolls to the left.
        double tEnd = _points.Count > 0 ? _points[^1].Time : 0;
        double tStart = tEnd - _windowSeconds;
        (double yMax, int steps) = NiceScale(_points.Count > 0 ? _points.Max(p => p.Wall) : 0);

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Brush sub = ThemeManager.Brush("SubText");
        var gridPen = new Pen(ThemeManager.Brush("Grid"), 1);
        gridPen.Freeze();

        // Horizontal grid lines with watt labels.
        for (int i = 0; i <= steps; i++)
        {
            double v = yMax * i / steps;
            double y = Snap(TopPad + plotH - plotH * i / steps);
            dc.DrawLine(gridPen, new Point(LeftPad, y), new Point(LeftPad + plotW, y));
            var label = new FormattedText(v.ToString("0", CultureInfo.CurrentCulture) + " W", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 11, sub, pixelsPerDip);
            dc.DrawText(label, new Point(LeftPad - 6 - label.Width, y - label.Height / 2));
        }

        // Vertical grid lines every minute, labelled relative to now.
        for (int m = 0; m * 60 <= _windowSeconds; m++)
        {
            double x = Snap(LeftPad + plotW - plotW * (m * 60) / _windowSeconds);
            dc.DrawLine(gridPen, new Point(x, TopPad), new Point(x, TopPad + plotH));
            string text = m == 0 ? "0" : "-" + m.ToString(CultureInfo.CurrentCulture) + ":00";
            var label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, sub, pixelsPerDip);
            double lx = Math.Clamp(x - label.Width / 2, 0, w - label.Width);
            dc.DrawText(label, new Point(lx, TopPad + plotH + 3));
        }

        if (_points.Count < 2)
        {
            return;
        }

        Point Map(double t, double v) => new(
            LeftPad + plotW * (t - tStart) / _windowSeconds,
            TopPad + plotH - plotH * Math.Clamp(v / yMax, 0, 1));

        dc.PushClip(new RectangleGeometry(new Rect(LeftPad, TopPad - 2, plotW, plotH + 4)));
        DrawSeries(dc, "SeriesOther", 1.2, p => p.Other, Map);
        DrawSeries(dc, "SeriesCpu", 1.6, p => p.Cpu, Map);
        DrawSeries(dc, "SeriesGpu", 1.6, p => p.Gpu, Map);
        DrawSeries(dc, "SeriesDc", 1.4, p => p.Dc, Map);
        DrawSeries(dc, "SeriesWall", 2.4, p => p.Wall, Map);
        dc.Pop();
    }

    private void DrawSeries(DrawingContext dc, string brushName, double thickness,
        Func<HistoryPoint, double?> value, Func<double, double, Point> map)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            bool open = false;
            foreach (HistoryPoint p in _points)
            {
                double? v = value(p);
                if (v is null)
                {
                    open = false; // a gap where the value was unavailable
                    continue;
                }
                Point pt = map(p.Time, v.Value);
                if (!open)
                {
                    ctx.BeginFigure(pt, false, false);
                    open = true;
                }
                else
                {
                    ctx.LineTo(pt, true, true);
                }
            }
        }
        geometry.Freeze();
        var pen = new Pen(ThemeManager.Brush(brushName), thickness) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    /// <summary>A round axis maximum just above the largest value, and a grid step count that keeps labels round.</summary>
    internal static (double Max, int Steps) NiceScale(double max)
    {
        double target = Math.Max(max * 1.1, 50);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(target)));
        foreach (double s in new[] { 1, 2, 2.5, 5 })
        {
            if (s * magnitude >= target)
            {
                return (s * magnitude, s == 2.5 ? 5 : 4);
            }
        }
        return (10 * magnitude, 4);
    }

    private static double Snap(double v) => Math.Round(v) + 0.5;
}
