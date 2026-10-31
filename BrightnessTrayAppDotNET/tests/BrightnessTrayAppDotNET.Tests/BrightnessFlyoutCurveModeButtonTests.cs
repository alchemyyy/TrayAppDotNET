using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.UI.Flyout;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class BrightnessFlyoutCurveModeButtonTests
{
    [Theory]
    [InlineData(false, SliderState.Enabled, false)]
    [InlineData(false, SliderState.CurveActive, false)]
    [InlineData(true, SliderState.Enabled, false)]
    [InlineData(true, SliderState.Disabled, false)]
    [InlineData(true, SliderState.Failed, false)]
    [InlineData(true, SliderState.CurveActive, true)]
    [InlineData(true, SliderState.CurveSleeping, true)]
    [InlineData(true, SliderState.CurveReleased, true)]
    public void CurveModeButtonOnlyOccupiesSliderSpaceForCurveControlledRows(
        bool isCurveEnabled,
        SliderState sliderState,
        bool expected)
    {
        bool isVisible = BrightnessFlyoutWindow.ShouldShowCurveModeButton(isCurveEnabled, sliderState);

        Assert.Equal(expected, isVisible);
    }

    [Theory]
    [InlineData(SliderState.CurveActive, 51.0)]
    [InlineData(SliderState.CurveActive, 100.0)]
    [InlineData(SliderState.CurveSleeping, 51.0)]
    [InlineData(SliderState.CurveSleeping, 100.0)]
    public void MasterManualOverrideReleasesCurveDrivenRowEvenWhenSliderMatchesCurve(
        SliderState curveState,
        double curveTarget)
    {
        MonitorInfo row = new() { Brightness = 51, SliderState = curveState, CurveTargetBrightness = curveTarget };
        long manualRevision = row.ManualBrightnessRevision;

        List<MonitorInfo> released = BrightnessFlyoutWindow.ReleaseCurveDrivenRows([row]);

        Assert.Same(row, Assert.Single(released));
        Assert.Equal(SliderState.CurveReleased, row.SliderState);
        Assert.False(row.IsCurveDriven);
        Assert.Equal(expected: 51, row.EffectiveRoundedBrightness);
        Assert.True(row.ManualBrightnessRevision > manualRevision);
    }

    [Theory]
    [InlineData(SliderState.Enabled)]
    [InlineData(SliderState.Disabled)]
    [InlineData(SliderState.Failed)]
    [InlineData(SliderState.CurveReleased)]
    public void MasterManualOverrideLeavesRowsOutsideCurveControlUntouched(SliderState state)
    {
        MonitorInfo row = new() { Brightness = 51, SliderState = state };
        long manualRevision = row.ManualBrightnessRevision;

        List<MonitorInfo> released = BrightnessFlyoutWindow.ReleaseCurveDrivenRows([row]);

        Assert.Empty(released);
        Assert.Equal(state, row.SliderState);
        Assert.Equal(manualRevision, row.ManualBrightnessRevision);
    }
}
