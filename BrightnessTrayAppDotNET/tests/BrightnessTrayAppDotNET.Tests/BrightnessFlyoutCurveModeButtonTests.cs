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
}
