namespace PcPowerMeter.Core;

/// <summary>One point of the live chart. Time is the session time in seconds.</summary>
public readonly record struct HistoryPoint(double Time, double Wall, double Dc, double? Gpu, double? Cpu, double Other);

/// <summary>Keeps the chart points that fall inside a sliding time window.</summary>
public sealed class PowerHistory
{
    private readonly List<HistoryPoint> _points = new();

    public PowerHistory(double windowSeconds)
    {
        if (windowSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSeconds));
        }
        WindowSeconds = windowSeconds;
    }

    public double WindowSeconds { get; }

    public IReadOnlyList<HistoryPoint> Points => _points;

    public void Add(HistoryPoint point)
    {
        if (_points.Count > 0 && point.Time < _points[^1].Time)
        {
            // Time went backwards (session reset): start a fresh chart.
            _points.Clear();
        }
        _points.Add(point);

        // Keep one point older than the window so the line reaches the left edge.
        double cutoff = point.Time - WindowSeconds;
        int remove = 0;
        while (remove + 1 < _points.Count && _points[remove + 1].Time <= cutoff)
        {
            remove++;
        }
        if (remove > 0)
        {
            _points.RemoveRange(0, remove);
        }
    }

    public void Clear() => _points.Clear();
}
