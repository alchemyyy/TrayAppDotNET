using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class DevicePerformanceDetailsTests
{
    [Theory]
    [InlineData(0, 1_048_576)]
    [InlineData(1_048_576, 2_097_152)]
    [InlineData(300_000_000, 536_870_912)]
    public void DiskTransferScaleProvidesBinaryHeadroom(
        double maximumTransferBytesPerSecond,
        double expectedScale)
    {
        double scale = DiskPerformanceDetailsView.CalculateTransferScale(
            maximumTransferBytesPerSecond);

        Assert.Equal(expectedScale, scale);
    }

    [Fact]
    public void GPUCategoryUsesBusiestMatchingPhysicalEngine()
    {
        GPUPerformanceEngineSnapshot[] engines =
        [
            new(EngineIndex: 0, Name: "3D", UtilizationPercent: 12),
            new(EngineIndex: 1, Name: "Copy", UtilizationPercent: 70),
            new(EngineIndex: 2, Name: "3D", UtilizationPercent: 44)
        ];

        bool found = GPUPerformanceDetailsView.TryGetEngineUtilization(
            engines,
            engineName: "3D",
            out double utilizationPercent);

        Assert.True(found);
        Assert.Equal(expected: 44, utilizationPercent);
    }

    [Fact]
    public void GPUCategoryDoesNotInventMissingEngineData()
    {
        GPUPerformanceEngineSnapshot[] engines = [new(EngineIndex: 0, Name: "3D", UtilizationPercent: 12)];

        bool found = GPUPerformanceDetailsView.TryGetEngineUtilization(
            engines,
            engineName: "Video Decode",
            out double utilizationPercent);

        Assert.False(found);
        Assert.Equal(expected: 0, utilizationPercent);
    }

    [Fact]
    public void GPUFallbackLaneDoesNotDuplicateAnEarlierDetailEngine()
    {
        string[] displayedEngineNames = ["3D", "Copy", "Video Decode"];

        string fallbackName = GPUPerformanceDetailsView.SelectFallbackEngineName(
            displayedEngineNames);

        Assert.Equal(expected: "Video Encode", fallbackName);
    }

    [Fact]
    public void GPUFallbackLaneDoesNotReuseADetailEngineIndex()
    {
        string[] displayedEngineNames = ["GPU Engine"];
        GPUPerformanceDetailEngineSnapshot[] detailEngines =
        [
            new(EngineIndex: 0, Name: "GPU Engine", HasUtilizationSample: true, UtilizationPercent: 10)
        ];
        GPUPerformanceEngineSnapshot[] liveEngines =
        [
            new(EngineIndex: 0, Name: "3D", UtilizationPercent: 80),
            new(EngineIndex: 1, Name: "Copy", UtilizationPercent: 20)
        ];

        bool found = GPUPerformanceDetailsView.TrySelectFallbackEngine(
            displayedEngineNames,
            detailEngines,
            liveEngines,
            out string engineName,
            out double utilizationPercent);

        Assert.True(found);
        Assert.Equal(expected: "Copy", engineName);
        Assert.Equal(expected: 20, utilizationPercent);
    }
}
