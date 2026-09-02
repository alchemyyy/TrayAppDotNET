using TaskManagerTrayAppDotNET.UI.Tray;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class TaskManagerTrayGraphSamplerTests
{
    [Fact]
    public void DefaultPolynomialPlacementWidthsGrowTowardNewestSample()
    {
        double[] samplePositions = TaskManagerTrayGraphSampler.CreateSamplePositions(16);

        Assert.Equal(expected: 0, samplePositions[0]);
        Assert.Equal(expected: 1, samplePositions[^1]);
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
        MarqueeSamplePlacementFunction placementFunction = Placement;

        double[] samplePositions =
            TaskManagerTrayGraphSampler.CreateSamplePositions(numSamplesToDisplay: 4, placementFunction);

        Assert.Equal([0, 1.0 / 3.0, 2.0 / 3.0, 1], samplePositions);
        Assert.Equal(
            [(0, 4), (1, 4), (2, 4), (3, 4)],
            arguments);
        return;

        double Placement(int currentSampleIndex, int numSamplesToDisplay)
        {
            arguments.Add((currentSampleIndex, numSamplesToDisplay));
            return currentSampleIndex / (double)(numSamplesToDisplay - 1);
        }
    }
}
