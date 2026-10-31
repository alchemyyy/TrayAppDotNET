using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class FlyoutSliderTests
{
    [Theory]
    [InlineData(32.0, 4.0, 22.0)]
    [InlineData(31.0, 4.0, 18.0)]
    [InlineData(31.5, 3.5, 17.25)]
    public void CalculateCenteredTopAlignsTrackAndThumb(
        double containerHeight,
        double trackHeight,
        double thumbHeight)
    {
        double trackCenter =
            FlyoutSlider.CalculateCenteredTop(containerHeight, trackHeight) + trackHeight / 2.0;
        double thumbCenter =
            FlyoutSlider.CalculateCenteredTop(containerHeight, thumbHeight) + thumbHeight / 2.0;

        Assert.Equal(containerHeight / 2.0, trackCenter, precision: 10);
        Assert.Equal(trackCenter, thumbCenter, precision: 10);
    }

    [Theory]
    [InlineData(0.0, 1.0f, 2.0, 0.0)]
    [InlineData(100.0, 0.0f, 2.0, 0.0)]
    [InlineData(100.0, 0.5f, 2.0, 51.0)]
    [InlineData(100.0, 1.0f, 2.0, 100.0)]
    [InlineData(100.0, 1.1f, 2.0, 100.0)]
    public void CalculatePeakWidthStaysWithinVolumeExtent(
        double peakExtent,
        float peak,
        double radius,
        double expected)
    {
        double width = FlyoutSlider.CalculatePeakWidth(peakExtent, peak, radius);

        Assert.Equal(expected, width);
    }
}
