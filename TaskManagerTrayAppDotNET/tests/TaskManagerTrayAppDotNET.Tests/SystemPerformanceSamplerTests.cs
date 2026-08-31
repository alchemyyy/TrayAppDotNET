using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class SystemPerformanceSamplerTests
{
    [Fact]
    public void NativeSampleReturnsBoundedSystemPercentages()
    {
        using SystemPerformanceSampler sampler = new();

        _ = sampler.Sample();
        Thread.Sleep(25);
        SystemPerformanceSample sample = sampler.Sample();
        int logicalProcessorCount = sampler.LastLogicalProcessorCount;
        double[] logicalProcessorPercents = new double[logicalProcessorCount];
        int copiedProcessorCount = sampler.CopyLastLogicalProcessorPercents(
            logicalProcessorPercents);
        SystemMemoryStatus memoryStatus = sampler.GetLastMemoryStatus();

        Assert.InRange(sample.CPUAveragePercent, low: 0, high: 100);
        Assert.InRange(sample.CPUHighestCorePercent, low: 0, high: 100);
        Assert.InRange(sample.MemoryPercent, low: 0.01, high: 100);
        Assert.True(sampler.LastProcessorSampleAvailable);
        Assert.True(sampler.LastMemorySampleAvailable);
        Assert.True(logicalProcessorCount > 0);
        Assert.Equal(logicalProcessorCount, copiedProcessorCount);
        Assert.All(logicalProcessorPercents, static percent => Assert.InRange(percent, low: 0, high: 100));
        Assert.True(memoryStatus.TotalPhysicalBytes > 0);
        Assert.True(memoryStatus.AvailablePhysicalBytes <= memoryStatus.TotalPhysicalBytes);
    }

    [Fact]
    public void ResetProcessorBaselineSuppressesTheNextDelta()
    {
        using SystemPerformanceSampler sampler = new();
        _ = sampler.Sample();
        Thread.Sleep(25);
        _ = sampler.Sample();
        Assert.True(sampler.LastProcessorSampleAvailable);

        sampler.ResetProcessorBaseline();
        SystemPerformanceSample baselineSample = sampler.Sample();

        Assert.False(sampler.LastProcessorSampleAvailable);
        Assert.Equal(expected: 0, baselineSample.CPUAveragePercent);
        Assert.Equal(expected: 0, baselineSample.CPUHighestCorePercent);
    }

    [Theory]
    [InlineData(50, 150, 66.6666666667)]
    [InlineData(0, 100, 100)]
    [InlineData(100, 100, 0)]
    [InlineData(150, 100, 0)]
    [InlineData(-10, 100, 100)]
    [InlineData(10, 0, 0)]
    public void CPUUsageCalculationClampsIdleAndResult(
        double idleDelta,
        double totalDelta,
        double expectedPercent)
    {
        double actualPercent = SystemPerformanceSampler.CalculateCPUUsagePercent(idleDelta, totalDelta);

        Assert.Equal(expectedPercent, actualPercent, precision: 8);
    }
}
