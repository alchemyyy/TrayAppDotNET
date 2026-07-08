using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.UI.Flyout;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class TrayWheelBrightnessAdjustmentTests
{
    [Fact]
    public void ResolveCurveModeTrayWheelManualTargetsUsesCurveValuePlusDirection()
    {
        MonitorInfo masterMonitor = CreateCurveActiveMonitor(curveTarget: 60, brightness: 10);
        MonitorInfo activeMonitor = CreateCurveActiveMonitor(curveTarget: 42, brightness: 80);
        MonitorInfo releasedMonitor = new() { Brightness = 30, SliderState = SliderState.CurveReleased };
        MonitorInfo[] monitors = [activeMonitor, releasedMonitor];

        Dictionary<MonitorInfo, int>? targets = BrightnessAvaloniaApp.ResolveCurveModeTrayWheelManualTargets(
            isBrightnessCurveEnabled: true,
            isCurveAbsoluteMode: true,
            masterMonitor: masterMonitor,
            monitors: monitors,
            delta: 10);

        Assert.NotNull(targets);
        Assert.Equal(43, targets[activeMonitor]);
        Assert.False(targets.ContainsKey(releasedMonitor));
    }

    [Fact]
    public void ResolveCurveModeTrayWheelManualTargetsClampsDirectionTarget()
    {
        MonitorInfo masterMonitor = CreateCurveActiveMonitor(curveTarget: 60, brightness: 10);
        MonitorInfo activeMonitor = CreateCurveActiveMonitor(curveTarget: 0, brightness: 80);
        MonitorInfo[] monitors = [activeMonitor];

        Dictionary<MonitorInfo, int>? targets = BrightnessAvaloniaApp.ResolveCurveModeTrayWheelManualTargets(
            isBrightnessCurveEnabled: true,
            isCurveAbsoluteMode: true,
            masterMonitor: masterMonitor,
            monitors: monitors,
            delta: -10);

        Assert.NotNull(targets);
        Assert.Equal(0, targets[activeMonitor]);
    }

    [Fact]
    public void ResolveCurveModeTrayWheelManualTargetsReturnsNullOutsideCurveRelease()
    {
        MonitorInfo masterMonitor = CreateCurveActiveMonitor(curveTarget: 60, brightness: 10);
        MonitorInfo activeMonitor = CreateCurveActiveMonitor(curveTarget: 42, brightness: 80);
        MonitorInfo[] monitors = [activeMonitor];

        Dictionary<MonitorInfo, int>? disabledCurveTargets =
            BrightnessAvaloniaApp.ResolveCurveModeTrayWheelManualTargets(
                isBrightnessCurveEnabled: false,
                isCurveAbsoluteMode: true,
                masterMonitor: masterMonitor,
                monitors: monitors,
                delta: 10);
        Dictionary<MonitorInfo, int>? offsetModeTargets =
            BrightnessAvaloniaApp.ResolveCurveModeTrayWheelManualTargets(
                isBrightnessCurveEnabled: true,
                isCurveAbsoluteMode: false,
                masterMonitor: masterMonitor,
                monitors: monitors,
                delta: 10);

        masterMonitor.SliderState = SliderState.CurveReleased;
        Dictionary<MonitorInfo, int>? releasedMasterTargets =
            BrightnessAvaloniaApp.ResolveCurveModeTrayWheelManualTargets(
                isBrightnessCurveEnabled: true,
                isCurveAbsoluteMode: true,
                masterMonitor: masterMonitor,
                monitors: monitors,
                delta: 10);

        Assert.Null(disabledCurveTargets);
        Assert.Null(offsetModeTargets);
        Assert.Null(releasedMasterTargets);
    }

    [Theory]
    [InlineData(false, false, false, true, true)]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, false, true, true, true)]
    [InlineData(true, false, false, true, false)]
    [InlineData(false, false, false, false, false)]
    public void ManualNightLightStrengthWritesOnlyWhenUserOwnsHardware(
        bool isCurveEnabled,
        bool isInDisabledPeriod,
        bool isCurveReleased,
        bool isNightLightActive,
        bool expected)
    {
        bool shouldApply = BrightnessFlyoutWindow.ShouldApplyManualNightLightStrength(
            isCurveEnabled,
            isInDisabledPeriod,
            isCurveReleased,
            isNightLightActive);

        Assert.Equal(expected, shouldApply);
    }

    /// <summary>
    /// Creates a monitor whose effective value is driven by a curve target.
    /// </summary>
    private static MonitorInfo CreateCurveActiveMonitor(double curveTarget, double brightness)
    {
        MonitorInfo monitor = new()
        {
            Brightness = brightness, SliderState = SliderState.CurveActive, CurveTargetBrightness = curveTarget
        };
        return monitor;
    }
}
