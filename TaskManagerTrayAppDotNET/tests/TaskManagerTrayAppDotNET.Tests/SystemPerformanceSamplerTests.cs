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

        Assert.InRange(sample.CPUAveragePercent, 0, 100);
        Assert.InRange(sample.CPUHighestCorePercent, 0, 100);
        Assert.InRange(sample.MemoryPercent, 0.01, 100);
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
