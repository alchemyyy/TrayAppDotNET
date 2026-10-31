using TaskManagerTrayAppDotNET.Interop;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PdhQueryTests
{
    [Fact]
    public void SetupRetriesBackOffAndAreCapped()
    {
        Assert.Equal(TimeSpan.Zero, PdhQuery.RetryDelay(consecutiveFailures: 0));
        Assert.Equal(PdhQuery.RetryDelay(1) * 2, PdhQuery.RetryDelay(2));
        Assert.Equal(TimeSpan.FromMinutes(5), PdhQuery.RetryDelay(consecutiveFailures: 1_000));
    }

    [Fact]
    public void FailedSetupWaitsForTheBackoffBeforeRetrying()
    {
        int attempts = 0;
        const uint result = 0xC0000BB8;
        using PdhQuery query = new("test", _ =>
        {
            attempts++;
            return result;
        });

        Assert.False(query.TryCollect());
        Assert.Equal(expected: 1, attempts);
        // Within the backoff window no new attempt is made
        Assert.False(query.TryCollect());
        Assert.Equal(expected: 1, attempts);
        Assert.False(query.IsOpen);
    }
}
