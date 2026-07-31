using VolumeTrayAppDotNET.Audio;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class DingSuppressionPeakTests
{
    [Fact]
    public void ObserveTakesNewPeaksImmediatelyAndDecaysHeldPeaks()
    {
        const long startMilliseconds = 10_000;
        DingSuppressionPeak peak = new();

        float initial = peak.Observe(0.8f, startMilliseconds);
        float decayed = peak.Observe(
            0.1f,
            startMilliseconds + TimeConstants.DingSuppressionPeakHalfLifeMs);
        float replaced = peak.Observe(
            0.6f,
            startMilliseconds + TimeConstants.DingSuppressionPeakHalfLifeMs * 2L);

        Assert.Equal(0.8f, initial);
        Assert.InRange(decayed, 0.3999f, 0.4001f);
        Assert.Equal(0.6f, replaced);
    }

    [Fact]
    public void ReadContinuesDecayingWithoutADiscreteExpiration()
    {
        const long startMilliseconds = 20_000;
        DingSuppressionPeak peak = new();
        peak.Observe(1f, startMilliseconds);

        float afterTwoSeconds = peak.Read(startMilliseconds + 2_000L);
        float afterThreeSeconds = peak.Read(startMilliseconds + 3_000L);

        Assert.InRange(afterTwoSeconds, 0.0039062f, 0.0039063f);
        Assert.InRange(afterThreeSeconds, 0.0002441f, 0.0002442f);
    }

    [Fact]
    public void ObserveUsesTheCurrentPeakWhenItExceedsDecayedHistory()
    {
        const long startMilliseconds = 30_000;
        DingSuppressionPeak peak = new();
        peak.Observe(1f, startMilliseconds);

        float current = peak.Observe(0.12f, startMilliseconds + 2_000L);

        Assert.Equal(0.12f, current);
    }

    [Theory]
    [InlineData(5, 1.0f, 0.05f)]
    [InlineData(5, 0.5f, 0.025f)]
    [InlineData(5, 0.1f, 0.005f)]
    [InlineData(100, 0.75f, 0.75f)]
    [InlineData(0, 1.0f, 0.0f)]
    public void ResolveThresholdScalesWithVolume(
        int configuredPercent,
        float scalarVolume,
        float expected)
    {
        float threshold = DingSuppressionPeak.ResolveThreshold(configuredPercent, scalarVolume);

        Assert.InRange(threshold, expected - 0.000001f, expected + 0.000001f);
    }

    [Theory]
    [InlineData(-10, 0.5f, 0.0f)]
    [InlineData(150, 0.5f, 0.5f)]
    [InlineData(5, -1.0f, 0.0f)]
    [InlineData(5, 2.0f, 0.05f)]
    public void ResolveThresholdClampsInvalidRanges(
        int configuredPercent,
        float scalarVolume,
        float expected)
    {
        float threshold = DingSuppressionPeak.ResolveThreshold(configuredPercent, scalarVolume);

        Assert.InRange(threshold, expected - 0.000001f, expected + 0.000001f);
    }

    [Fact]
    public void UnavailablePeakSuppressesFeedbackInsteadOfExtendingTheBypass()
    {
        bool shouldSuppress = DingSuppressionPeak.ShouldSuppressFeedback(
            recentPeak: 0f,
            configuredPercent: 5,
            scalarVolume: 1f,
            isPeakAvailable: false);

        Assert.True(shouldSuppress);
    }
}
