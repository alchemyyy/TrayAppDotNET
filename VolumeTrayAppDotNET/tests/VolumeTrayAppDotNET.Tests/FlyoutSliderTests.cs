using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class FlyoutSliderTests
{
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
