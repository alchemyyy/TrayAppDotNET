using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class SystemPerformanceMetadataReaderTests
{
    [Fact]
    public void ProcessorPerformanceAboveOneHundredPercentProducesTurboSpeed()
    {
        ulong currentSpeedHertz = SystemPerformanceMetadataReader.CalculateCurrentSpeedHertz(
            4_200_000_000,
            128.24);

        Assert.Equal(5_386_080_000UL, currentSpeedHertz);
    }
}
