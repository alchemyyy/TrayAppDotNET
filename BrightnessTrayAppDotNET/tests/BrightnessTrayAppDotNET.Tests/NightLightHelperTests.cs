using BrightnessTrayAppDotNET.Interop.NightLight;
using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.Services;
using BrightnessTrayAppDotNET.UI.Flyout;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class NightLightHelperTests
{
    [Fact]
    public void ProviderCapabilityProbeDoesNotStartNativeHelper()
    {
        Assert.False(NightLightHelperClient.HasStartedInitialization);

        AppSettings settings = new();
        NightLightProvider.Initialize(settings);
        _ = NightLightProvider.IsSupported();

        Assert.False(NightLightHelperClient.HasStartedInitialization);
    }

    [Fact]
    public void LatestQueueReplacesPendingIntermediateValue()
    {
        NightLightLatestStrengthQueue queue = new();

        bool replacedFirst = queue.Store(10);
        bool replacedSecond = queue.Store(80);
        bool tookValue = queue.TryTake(out int value);

        Assert.False(replacedFirst);
        Assert.True(replacedSecond);
        Assert.True(tookValue);
        Assert.Equal(expected: 80, value);
        Assert.False(queue.TryTake(out int ignoredValue));
        Assert.Equal(expected: 0, ignoredValue);
    }

    [Fact]
    public void FailedValueDoesNotReplaceNewerPendingValue()
    {
        NightLightLatestStrengthQueue queue = new();
        queue.Store(20);
        Assert.True(queue.TryTake(out int inFlightValue));

        queue.Store(90);
        bool restored = queue.RestoreIfEmpty(inFlightValue);

        Assert.False(restored);
        Assert.True(queue.TryTake(out int pendingValue));
        Assert.Equal(expected: 90, pendingValue);
    }

    [Fact]
    public void FailedValueIsRestoredWhenNoReplacementExists()
    {
        NightLightLatestStrengthQueue queue = new();
        queue.Store(35);
        Assert.True(queue.TryTake(out int inFlightValue));

        bool restored = queue.RestoreIfEmpty(inFlightValue);

        Assert.True(restored);
        Assert.True(queue.TryTake(out int pendingValue));
        Assert.Equal(expected: 35, pendingValue);
    }

    [Fact]
    public void RecyclePolicyWarmsBeforeHardOperationLimit()
    {
        const int warmupThreshold = Constants.NightLightHelperRecycleOperationCount -
                                    Constants.NightLightHelperWarmupLeadOperationCount;

        Assert.False(NightLightHelperClient.ShouldStartWarmup(warmupThreshold - 1));
        Assert.True(NightLightHelperClient.ShouldStartWarmup(warmupThreshold));
        Assert.False(NightLightHelperClient.ShouldRecycle(
            Constants.NightLightHelperRecycleOperationCount - 1));
        Assert.True(NightLightHelperClient.ShouldRecycle(
            Constants.NightLightHelperRecycleOperationCount));
    }

    [Fact]
    public void PingProtocolDoesNotEnterNativeBackend()
    {
        string response = NightLightHelperServer.HandleCommand(NightLightHelperServer.PingCommand);

        Assert.Equal(NightLightHelperServer.PongResponse, response);
    }

    [Theory]
    [InlineData("ACTIVE")]
    [InlineData("ACTIVE\t2")]
    [InlineData("ACTIVE\t0\t50")]
    [InlineData("ACTIVE\t1\t101")]
    public void InvalidActiveStateCommandsAreRejected(string command)
    {
        string response = NightLightHelperServer.HandleCommand(command);

        Assert.Equal(NightLightHelperServer.FailureResponse, response);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void ProfileAutosaveWaitsForSliderGestureCompletion(
        bool autosaveEnabled,
        bool isAnySliderDragging,
        bool expected)
    {
        bool actual = BrightnessFlyoutWindow.CanAutosaveProfile(
            autosaveEnabled,
            isAnySliderDragging);

        Assert.Equal(expected, actual);
    }
}
