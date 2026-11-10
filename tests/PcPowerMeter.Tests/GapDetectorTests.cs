using PcPowerMeter.Core;

namespace PcPowerMeter.Tests;

public class GapDetectorTests
{
    [Fact]
    public void ShorterInterval_FirstIntervalJudgedByOldSetting()
    {
        var gaps = new GapDetector();
        gaps.IntervalChanged(oldIntervalSeconds: 60, newIntervalSeconds: 1);

        Assert.False(gaps.IsGap(45, currentIntervalSeconds: 1)); // the old 60 s span, not a gap
        Assert.True(gaps.IsGap(45, currentIntervalSeconds: 1));  // from now on 1 s rules apply
    }

    [Fact]
    public void LongerInterval_IsNotAGap()
    {
        var gaps = new GapDetector();
        gaps.IntervalChanged(oldIntervalSeconds: 1, newIntervalSeconds: 60);

        Assert.False(gaps.IsGap(60, currentIntervalSeconds: 60));
        Assert.False(gaps.IsGap(61, currentIntervalSeconds: 60));
    }

    [Fact]
    public void RealSleep_IsStillAGap_EvenRightAfterAChange()
    {
        var gaps = new GapDetector();
        gaps.IntervalChanged(oldIntervalSeconds: 60, newIntervalSeconds: 1);

        Assert.True(gaps.IsGap(8 * 3600, currentIntervalSeconds: 1));
    }

    [Fact]
    public void WithoutChange_UsesCurrentInterval()
    {
        var gaps = new GapDetector();

        Assert.False(gaps.IsGap(4, currentIntervalSeconds: 1)); // below the 5 s floor
        Assert.True(gaps.IsGap(6, currentIntervalSeconds: 1));
    }

    [Fact]
    public void SeveralChangesBeforeATick_KeepTheLargest()
    {
        var gaps = new GapDetector();
        gaps.IntervalChanged(60, 10);
        gaps.IntervalChanged(10, 1);

        Assert.False(gaps.IsGap(59, currentIntervalSeconds: 1));
    }
}
