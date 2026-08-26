using TaskManagerTrayAppDotNET.UI.Tray;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class TaskManagerTrayGraphSamplerTests
{
    [Fact]
    public void DefaultPolynomialPlacementWidthsGrowTowardNewestSample()
    {
        double[] samplePositions = TaskManagerTrayGraphSampler.CreateSamplePositions(16);

        Assert.Equal(0, samplePositions[0]);
        Assert.Equal(1, samplePositions[^1]);
        double previousWidth = 0;
        for (int sampleIndex = 0; sampleIndex < samplePositions.Length - 1; sampleIndex++)
        {
            double width = samplePositions[sampleIndex + 1] - samplePositions[sampleIndex];
            Assert.True(width > previousWidth);
            previousWidth = width;
        }

        Assert.True(previousWidth > 0.12);
    }

    [Fact]
    public void SpecifiedPlacementFunctionReceivesSampleIndexAndDisplayCount()
    {
        List<(int CurrentSampleIndex, int NumSamplesToDisplay)> arguments = [];
        MarqueeSamplePlacementFunction placementFunction = (currentSampleIndex, numSamplesToDisplay) =>
        {
            arguments.Add((currentSampleIndex, numSamplesToDisplay));
            return currentSampleIndex / (double)(numSamplesToDisplay - 1);
        };

        double[] samplePositions = TaskManagerTrayGraphSampler.CreateSamplePositions(4, placementFunction);

        Assert.Equal([0, 1.0 / 3.0, 2.0 / 3.0, 1], samplePositions);
        Assert.Equal(
            [(0, 4), (1, 4), (2, 4), (3, 4)],
            arguments);
    }

    [Fact]
    public void MarqueeSplineDoesNotOvershootAndEndsAtNewestValue()
    {
        double[] values = [10, 90, 20, 80];

        double[] samples = TaskManagerTrayGraphSampler.SampleMarquee(values, 129);

        Assert.Equal(10, samples[0], precision: 8);
        Assert.Equal(80, samples[^1], precision: 8);
        Assert.All(samples, value => Assert.InRange(value, 10, 90));
    }

    [Fact]
    public void OneValueFillsTheMarqueeAtTheCurrentLevel()
    {
        double[] samples = TaskManagerTrayGraphSampler.SampleMarquee([42], 17);

        Assert.All(samples, value => Assert.Equal(42, value, precision: 8));
    }
}
