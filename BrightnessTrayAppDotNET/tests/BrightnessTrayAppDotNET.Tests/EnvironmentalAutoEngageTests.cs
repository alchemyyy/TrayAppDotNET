using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.Services;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class EnvironmentalAutoEngageTests
{
    [Fact]
    public void AutoEngageComparisonTreatsNearEqualAsBoundaryHit()
    {
        int comparison = EnvironmentalCurveService.CompareAutoEngageTargets(curveTarget: 50.0004, manualTarget: 50.0);

        Assert.Equal(expected: 0, comparison);
    }

    [Fact]
    public void AutoEngageCrossingRequiresPriorComparison()
    {
        bool shouldReengage =
            EnvironmentalCurveService.DidAutoEngageTargetReachOrCross(previousComparison: null, currentComparison: 1);

        Assert.False(shouldReengage);
    }

    [Fact]
    public void AutoEngageCrossingDetectsHitAndCross()
    {
        bool hit = EnvironmentalCurveService.DidAutoEngageTargetReachOrCross(previousComparison: -1,
            currentComparison: 0);
        bool cross =
            EnvironmentalCurveService.DidAutoEngageTargetReachOrCross(previousComparison: -1, currentComparison: 1);

        Assert.True(hit);
        Assert.True(cross);
    }

    [Fact]
    public void AutoEngageCrossingIgnoresLeavingSuppressedBoundary()
    {
        bool shouldReengage =
            EnvironmentalCurveService.DidAutoEngageTargetReachOrCross(previousComparison: 0, currentComparison: 1);

        Assert.False(shouldReengage);
    }

    [Fact]
    public void ManualBrightnessStampAdvancesForWritesAndCurveRelease()
    {
        MonitorInfo monitor = new();
        long initialRevision = monitor.ManualBrightnessRevision;

        monitor.Brightness = 50;
        long brightnessRevision = monitor.ManualBrightnessRevision;
        monitor.SliderState = SliderState.CurveActive;
        monitor.SliderState = SliderState.CurveReleased;

        Assert.True(brightnessRevision > initialRevision);
        Assert.True(monitor.ManualBrightnessRevision > brightnessRevision);
    }
}
