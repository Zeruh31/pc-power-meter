namespace PcPowerMeter.Core;

/// <summary>
/// Decides whether a measured interval is a gap (sleep, hibernation, a stalled read) that
/// must not be counted. When the sampling interval changes, the interval that was already
/// running was scheduled with the old setting, so the next check uses the larger of the old
/// and new interval; after that check the current interval applies again.
/// </summary>
public sealed class GapDetector
{
    private double _pendingReference;

    /// <summary>Call when the sampling interval setting changes.</summary>
    public void IntervalChanged(double oldIntervalSeconds, double newIntervalSeconds) =>
        _pendingReference = Math.Max(_pendingReference, Math.Max(oldIntervalSeconds, newIntervalSeconds));

    /// <summary>Checks one interval; consumes a pending interval change.</summary>
    public bool IsGap(double dtSeconds, double currentIntervalSeconds)
    {
        double reference = Math.Max(currentIntervalSeconds, _pendingReference);
        _pendingReference = 0;
        return PowerMeterEngine.IsGap(dtSeconds, reference);
    }
}
