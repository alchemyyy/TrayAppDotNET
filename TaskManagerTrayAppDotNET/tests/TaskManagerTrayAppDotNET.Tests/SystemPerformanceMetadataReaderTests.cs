using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class SystemPerformanceMetadataReaderTests
{
    [Fact]
    public void ProcessorPerformanceAboveOneHundredPercentProducesTurboSpeed()
    {
        ulong currentSpeedHertz = SystemPerformanceMetadataReader.CalculateCurrentSpeedHertz(
            baseSpeedHertz: 4_200_000_000,
            processorPerformancePercent: 128.24);

        Assert.Equal(expected: 5_386_080_000UL, currentSpeedHertz);
    }
}
