using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.Services;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class BrightnessSliderMathTests
{
    [Theory]
    [InlineData(99.8, 100)]
    [InlineData(99.2, 99)]
    [InlineData(-2.0, 0)]
    [InlineData(102.0, 100)]
    public void NormalizeManualPercentSnapsNearValuesToIntegerPercent(double value, double expected)
    {
        double normalized = BrightnessSliderMath.NormalizeManualPercent(value);

        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void ComputeMasterPercentUsesHardwareFunctionalRows()
    {
        MonitorInfo first = new() { Brightness = 99 };
        MonitorInfo second = new() { Brightness = 99 };
        MonitorInfo failed = new() { Brightness = 100, SliderState = SliderState.Failed };
        MonitorInfo[] monitors = [first, second, failed];

        double master = BrightnessSliderMath.ComputeMasterPercent(monitors, MasterSliderMode.Average, 42);

        Assert.Equal(99, master);
    }
}
