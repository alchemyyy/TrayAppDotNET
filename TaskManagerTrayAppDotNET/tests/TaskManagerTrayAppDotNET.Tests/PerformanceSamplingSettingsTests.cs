using TaskManagerTrayAppDotNET.Models;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceSamplingSettingsTests
{
    [Fact]
    public void DefaultsRetainOneMinuteAtOneSecondIntervals()
    {
        Assert.Equal(1, PerformanceSamplingSettings.DefaultHistoryLengthMinutes);
        Assert.Equal(1_000, PerformanceSamplingSettings.DefaultSampleIntervalMilliseconds);
        Assert.Equal(
            60,
            PerformanceSamplingSettings.CalculateMaximumHistoryCount(
                PerformanceSamplingSettings.DefaultHistoryLengthMinutes,
                PerformanceSamplingSettings.DefaultSampleIntervalMilliseconds));
    }

    [Theory]
    [InlineData(int.MinValue, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(61, 60)]
    [InlineData(int.MaxValue, 60)]
    public void HistoryLengthNormalizationClampsToSupportedMinutes(int value, int expected)
    {
        Assert.Equal(expected, PerformanceSamplingSettings.NormalizeHistoryLengthMinutes(value));
    }

    [Theory]
    [InlineData(int.MinValue, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(1_000, 1_000)]
    [InlineData(60_000, 60_000)]
    [InlineData(60_001, 60_000)]
    [InlineData(int.MaxValue, 60_000)]
    public void SampleIntervalNormalizationClampsToSupportedMilliseconds(
        int value,
        int expected)
    {
        Assert.Equal(expected, PerformanceSamplingSettings.NormalizeSampleIntervalMilliseconds(value));
    }

    [Theory]
    [InlineData(1, 1, 60_000)]
    [InlineData(1, 700, 85)]
    [InlineData(1, 60_000, 1)]
    [InlineData(5, 1_000, 300)]
    [InlineData(60, 1, 3_600_000)]
    [InlineData(0, int.MaxValue, 1)]
    public void MaximumHistoryCountUsesNormalizedIntegerDivision(
        int historyLengthMinutes,
        int sampleIntervalMilliseconds,
        int expected)
    {
        Assert.Equal(
            expected,
            PerformanceSamplingSettings.CalculateMaximumHistoryCount(
                historyLengthMinutes,
                sampleIntervalMilliseconds));
    }
}
