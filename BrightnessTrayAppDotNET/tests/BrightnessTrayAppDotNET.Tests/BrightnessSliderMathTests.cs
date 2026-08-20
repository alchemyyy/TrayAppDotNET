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

    [Fact]
    public void InitialEnrollmentRebasesFirstMonitorAgainstRestoredProfileMaster()
    {
        MonitorInfo monitor = new();
        monitor.InitializeBrightnessFromHardware(20);
        monitor.Brightness = 84;
        MonitorInfo[] monitors = [monitor];

        double master = BrightnessSliderMath.RebaseInitialEnrollmentOffsets(
            monitors,
            MasterSliderMode.Average,
            fallback: 17,
            preserveMasterSliderOffsets: false);

        Assert.Equal(84, master);
        Assert.Equal(0, monitor.Offset);
    }

    [Fact]
    public void InitialEnrollmentRebasesPreviouslyPublishedOffsetsAsRowsArrive()
    {
        MonitorInfo first = new() { Brightness = 60 };
        MonitorInfo second = new() { Brightness = 84 };
        List<MonitorInfo> monitors = [];
        monitors.Add(first);

        _ = BrightnessSliderMath.RebaseInitialEnrollmentOffsets(
            monitors,
            MasterSliderMode.Average,
            fallback: 17,
            preserveMasterSliderOffsets: false);
        monitors.Add(second);
        double master = BrightnessSliderMath.RebaseInitialEnrollmentOffsets(
            monitors,
            MasterSliderMode.Average,
            fallback: 60,
            preserveMasterSliderOffsets: false);

        Assert.Equal(72, master);
        Assert.Equal(-12, first.Offset);
        Assert.Equal(12, second.Offset);
    }
}
