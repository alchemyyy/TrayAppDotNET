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
            new(0, "3D", 12),
            new(1, "Copy", 70),
            new(2, "3D", 44)
        ];

        bool found = GPUPerformanceDetailsView.TryGetEngineUtilization(
            engines,
            "3D",
            out double utilizationPercent);

        Assert.True(found);
        Assert.Equal(44, utilizationPercent);
    }

    [Fact]
    public void GPUCategoryDoesNotInventMissingEngineData()
    {
        GPUPerformanceEngineSnapshot[] engines = [new(0, "3D", 12)];

        bool found = GPUPerformanceDetailsView.TryGetEngineUtilization(
            engines,
            "Video Decode",
            out double utilizationPercent);

        Assert.False(found);
        Assert.Equal(0, utilizationPercent);
    }

    [Fact]
    public void GPUFallbackLaneDoesNotDuplicateAnEarlierDetailEngine()
    {
        string[] displayedEngineNames = ["3D", "Copy", "Video Decode"];

        string fallbackName = GPUPerformanceDetailsView.SelectFallbackEngineName(
            displayedEngineNames);

        Assert.Equal("Video Encode", fallbackName);
    }

    [Fact]
    public void GPUFallbackLaneDoesNotReuseADetailEngineIndex()
    {
        string[] displayedEngineNames = ["GPU Engine"];
        GPUPerformanceDetailEngineSnapshot[] detailEngines =
        [
            new(0, "GPU Engine", true, 10)
        ];
        GPUPerformanceEngineSnapshot[] liveEngines =
        [
            new(0, "3D", 80),
            new(1, "Copy", 20)
        ];

        bool found = GPUPerformanceDetailsView.TrySelectFallbackEngine(
            displayedEngineNames,
            detailEngines,
            liveEngines,
            out string engineName,
            out double utilizationPercent);

        Assert.True(found);
        Assert.Equal("Copy", engineName);
        Assert.Equal(20, utilizationPercent);
    }
}
