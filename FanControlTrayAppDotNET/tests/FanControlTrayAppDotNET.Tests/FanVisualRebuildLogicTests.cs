using FanControlTrayAppDotNET.UI.Flyout;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class FanVisualRebuildLogicTests
{
    [Fact]
    public void SuccessfulCommitConsumesOnlyTheRequestPresentAtStart()
    {
        bool pending = FanVisualRebuildLogic.ResolvePendingAfterAttempt(
            pendingAtStart: true,
            requestedDuringAttempt: false,
            FanVisualRebuildResult.Committed);

        Assert.False(pending);
    }

    [Fact]
    public void SuccessfulCommitRetainsReentrantRequest()
    {
        bool pending = FanVisualRebuildLogic.ResolvePendingAfterAttempt(
            pendingAtStart: true,
            requestedDuringAttempt: true,
            FanVisualRebuildResult.Committed);

        Assert.True(pending);
    }

    [Theory]
    [InlineData((int)FanVisualRebuildResult.Deferred)]
    [InlineData((int)FanVisualRebuildResult.Unavailable)]
    [InlineData((int)FanVisualRebuildResult.Failed)]
    public void NonCommitRetainsOriginalRequest(int resultValue)
    {
        FanVisualRebuildResult result = (FanVisualRebuildResult)resultValue;
        bool pending = FanVisualRebuildLogic.ResolvePendingAfterAttempt(
            pendingAtStart: true,
            requestedDuringAttempt: false,
            result);

        Assert.True(pending);
    }
}
