using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class SystemPerformanceSamplerGroupTests
{
    [Fact]
    public void BufferCoversEveryProcessorGroup()
    {
        // Two 64-processor groups (128 logical processors) need room for both groups, not just the caller's
        int bytes = SystemPerformanceSampler.CalculateProcessorBufferBytes(
            [64u, 64u],
            entrySize: 48,
            slackPerGroup: 4);

        Assert.Equal(expected: (64 + 4 + 64 + 4) * 48, bytes);
    }

    [Fact]
    public void EmptyOrZeroGroupsStillGetOneEntry()
    {
        Assert.Equal(expected: 5 * 48, SystemPerformanceSampler.CalculateProcessorBufferBytes([], entrySize: 48, slackPerGroup: 4));
        Assert.Equal(expected: 5 * 48, SystemPerformanceSampler.CalculateProcessorBufferBytes([0u], entrySize: 48, slackPerGroup: 4));
    }
}
