using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

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
}
