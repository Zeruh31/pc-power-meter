using System.Globalization;
using System.Text;

namespace PcPowerMeter.Core;

/// <summary>Totals of a period. <see cref="Cost"/> is null when no price is set.</summary>
public sealed record UsageTotals(double WallWh, double DcWh, double GpuWh, double CpuWh, double OtherWh,
    double MeasuredSeconds, double NoGpuSeconds, double? Cost)
{
    public double WallKwh => WallWh / 1000.0;

    public static readonly UsageTotals Zero = new(0, 0, 0, 0, 0, 0, 0, null);
}

/// <summary>Totals of one day or month, starting at <paramref name="Start"/>.</summary>
public sealed record UsageBucket(DateTime Start, UsageTotals Totals);

/// <summary>Monthly estimate from the recent daily average.</summary>
/// <param name="Days">Calendar days the average is taken over, today included.</param>
public sealed record MonthlyProjection(double WallKwh, double? Cost, int Days)
{
    /// <summary>The divisor actually used: full days plus the part of today that has passed (at least 1).</summary>
    public double EffectiveDays { get; init; } = Days;
}

/// <summary>Sums the hourly history into periods and prices it. Costs always use wall energy.</summary>
public static class UsageReport
{
    /// <summary>Average days per month (365.25 / 12).</summary>
    public const double DaysPerMonth = 30.4375;

    public static UsageTotals Sum(IEnumerable<HourlyUsage> hours, DateTime from, DateTime to, PriceSchedule? prices) =>
        Total(hours.Where(h => h.Hour >= from && h.Hour < to), prices);

    public static UsageTotals Total(IEnumerable<HourlyUsage> hours, PriceSchedule? prices)
    {
        double wall = 0, dc = 0, gpu = 0, cpu = 0, other = 0, sec = 0, noGpu = 0, cost = 0;
        foreach (HourlyUsage h in hours)
        {
            wall += h.WallWh;
            dc += h.DcWh;
            gpu += h.GpuWh;
            cpu += h.CpuWh;
            other += h.OtherWh;
            sec += h.MeasuredSeconds;
            noGpu += h.NoGpuSeconds;
            if (prices is not null)
            {
                cost += prices.HourCost(h.Hour, h.WallWh);
            }
        }
        return new UsageTotals(wall, dc, gpu, cpu, other, sec, noGpu, prices is null ? null : cost);
    }

    /// <summary>One bucket per calendar day from <paramref name="firstDay"/> to <paramref name="lastDay"/>, empty days included.</summary>
    public static List<UsageBucket> Daily(IReadOnlyList<HourlyUsage> hours, DateTime firstDay, DateTime lastDay, PriceSchedule? prices)
    {
        var byDay = hours.GroupBy(h => h.Hour.Date).ToDictionary(g => g.Key, g => Total(g, prices));
        var result = new List<UsageBucket>();
        for (DateTime d = firstDay.Date; d <= lastDay.Date; d = d.AddDays(1))
        {
            result.Add(new UsageBucket(d, byDay.TryGetValue(d, out UsageTotals? t) ? t : Empty(prices)));
        }
        return result;
    }

    /// <summary>The last <paramref name="count"/> calendar months up to and including <paramref name="now"/>'s month, newest first.</summary>
    public static List<UsageBucket> Monthly(IReadOnlyList<HourlyUsage> hours, DateTime now, int count, PriceSchedule? prices)
    {
        var byMonth = hours.GroupBy(h => new DateTime(h.Hour.Year, h.Hour.Month, 1)).ToDictionary(g => g.Key, g => Total(g, prices));
        var first = new DateTime(now.Year, now.Month, 1);
        var result = new List<UsageBucket>();
        for (int i = 0; i < count; i++)
        {
            DateTime m = first.AddMonths(-i);
            result.Add(new UsageBucket(m, byMonth.TryGetValue(m, out UsageTotals? t) ? t : Empty(prices)));
        }
        return result;
    }

    /// <summary>
    /// "At this rate" monthly estimate: the average per day over the last seven calendar
    /// days (fewer if the history is younger), times the average month length. Days on
    /// which the app was not running count as days with no use. Today only counts with the
    /// part of it that has passed (14:00 = 0.58 of a day), so a half-finished day does not
    /// pull the average down; the whole span counts as at least one day, so the first
    /// minutes of a new history are not extrapolated.
    /// </summary>
    public static MonthlyProjection? Projection(IReadOnlyList<HourlyUsage> hours, DateTime now, PriceSchedule? prices)
    {
        if (hours.Count == 0)
        {
            return null;
        }
        DateTime today = now.Date;
        DateTime from = today.AddDays(-6);
        DateTime firstDay = hours.Min(h => h.Hour).Date;
        if (firstDay > from)
        {
            from = firstDay;
        }
        int days = (int)(today - from).TotalDays + 1;
        double effectiveDays = Math.Max(1.0, (days - 1) + (now - today).TotalDays);
        UsageTotals t = Sum(hours, from, today.AddDays(1), prices);
        return new MonthlyProjection(t.WallKwh / effectiveDays * DaysPerMonth, t.Cost / effectiveDays * DaysPerMonth, days)
        {
            EffectiveDays = effectiveDays,
        };
    }

    /// <summary>First day of the week containing <paramref name="now"/>.</summary>
    public static DateTime WeekStart(DateTime now, DayOfWeek firstDayOfWeek)
    {
        int diff = ((int)now.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        return now.Date.AddDays(-diff);
    }

    private static UsageTotals Empty(PriceSchedule? prices) => UsageTotals.Zero with { Cost = prices is null ? null : 0 };

    // ------------------------------------------------------------------ CSV export

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public const string HourlyHeader =
        "hour,wall_kwh_est,dc_kwh,gpu_kwh,cpu_kwh,other_kwh_est,measured_h,no_gpu_h,price_per_kwh,cost_est,currency";

    public const string DailyHeader =
        "date,wall_kwh_est,dc_kwh,gpu_kwh,cpu_kwh,other_kwh_est,measured_h,no_gpu_h,cost_est,currency";

    /// <summary>Hourly CSV, same conventions as the live log (comma, '.', local time).</summary>
    public static string ExportHourly(IEnumerable<HourlyUsage> hours, PriceSchedule? prices)
    {
        var sb = new StringBuilder().Append(HourlyHeader).Append("\r\n");
        foreach (HourlyUsage h in hours.OrderBy(h => h.Hour))
        {
            double? price = prices?.HourPrice(h.Hour);
            sb.Append(string.Join(',',
                h.Hour.ToString("yyyy-MM-dd HH:mm", Inv),
                Kwh(h.WallWh), Kwh(h.DcWh), Kwh(h.GpuWh), Kwh(h.CpuWh), Kwh(h.OtherWh),
                Hours(h.MeasuredSeconds), Hours(h.NoGpuSeconds),
                price.HasValue ? price.Value.ToString("0.####", Inv) : "",
                price.HasValue ? (h.WallWh / 1000.0 * price.Value).ToString("0.####", Inv) : "",
                prices?.Currency ?? "")).Append("\r\n");
        }
        return sb.ToString();
    }

    public static string ExportDaily(IReadOnlyList<HourlyUsage> hours, PriceSchedule? prices)
    {
        var sb = new StringBuilder().Append(DailyHeader).Append("\r\n");
        foreach (var day in hours.GroupBy(h => h.Hour.Date).OrderBy(g => g.Key))
        {
            UsageTotals t = Total(day, prices);
            sb.Append(string.Join(',',
                day.Key.ToString("yyyy-MM-dd", Inv),
                Kwh(t.WallWh), Kwh(t.DcWh), Kwh(t.GpuWh), Kwh(t.CpuWh), Kwh(t.OtherWh),
                Hours(t.MeasuredSeconds), Hours(t.NoGpuSeconds),
                t.Cost.HasValue ? t.Cost.Value.ToString("0.####", Inv) : "",
                prices?.Currency ?? "")).Append("\r\n");
        }
        return sb.ToString();
    }

    private static string Kwh(double wh) => (wh / 1000.0).ToString("0.######", Inv);

    private static string Hours(double seconds) => (seconds / 3600.0).ToString("0.####", Inv);
}
