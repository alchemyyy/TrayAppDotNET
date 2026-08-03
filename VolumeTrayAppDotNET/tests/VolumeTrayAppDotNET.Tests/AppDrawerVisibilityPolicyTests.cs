using VolumeTrayAppDotNET.Audio;
using VolumeTrayAppDotNET.Interop;
using Xunit;


namespace VolumeTrayAppDotNET.Tests;

public sealed class AppDrawerVisibilityPolicyTests
{
    [Fact]
    public void ResolvePrefersDefaultDeviceForInactiveDuplicates()
    {
        AppDrawerVisibilityCandidate[] candidates =
        [
            Candidate("old-device", isDefaultDevice: false, AudioSessionState.Inactive),
            Candidate("new-device", isDefaultDevice: true, AudioSessionState.Inactive)
        ];

        bool[] isVisible = AppDrawerVisibilityPolicy.Resolve(candidates);

        bool[] expected = [false, true];
        Assert.Equal(expected, isVisible);
    }

    [Fact]
    public void ResolvePrefersActiveNonDefaultCopyOverInactiveDefaultCopy()
    {
        AppDrawerVisibilityCandidate[] candidates =
        [
            Candidate("routed-device", isDefaultDevice: false, AudioSessionState.Active),
            Candidate("default-device", isDefaultDevice: true, AudioSessionState.Inactive)
        ];

        bool[] isVisible = AppDrawerVisibilityPolicy.Resolve(candidates);

        bool[] expected = [true, false];
        Assert.Equal(expected, isVisible);
    }

    [Fact]
    public void ResolveKeepsEveryActiveCopyForSimultaneousOutput()
    {
        AppDrawerVisibilityCandidate[] candidates =
        [
            Candidate("first-device", isDefaultDevice: false, AudioSessionState.Active),
            Candidate("second-device", isDefaultDevice: true, AudioSessionState.Active)
        ];

        bool[] isVisible = AppDrawerVisibilityPolicy.Resolve(candidates);

        bool[] expected = [true, true];
        Assert.Equal(expected, isVisible);
    }

    [Fact]
    public void ResolveKeepsInactiveCopiesWhenDefaultDeviceHasNoCopy()
    {
        AppDrawerVisibilityCandidate[] candidates =
        [
            Candidate("first-routed-device", isDefaultDevice: false, AudioSessionState.Inactive),
            Candidate("second-routed-device", isDefaultDevice: false, AudioSessionState.Inactive)
        ];

        bool[] isVisible = AppDrawerVisibilityPolicy.Resolve(candidates);

        bool[] expected = [true, true];
        Assert.Equal(expected, isVisible);
    }

    [Fact]
    public void ResolveKeepsUniqueAppOnPreviousDefaultDevice()
    {
        AppDrawerVisibilityCandidate[] candidates =
        [
            Candidate("old-device", isDefaultDevice: false, AudioSessionState.Inactive),
            Candidate("new-device", isDefaultDevice: true, AudioSessionState.Inactive),
            Candidate(
                "old-device",
                isDefaultDevice: false,
                AudioSessionState.Inactive,
                appID: "unique-app")
        ];

        bool[] isVisible = AppDrawerVisibilityPolicy.Resolve(candidates);

        bool[] expected = [false, true, true];
        Assert.Equal(expected, isVisible);
    }

    [Fact]
    public void ResolveDoesNotDeduplicateAcrossDataFlows()
    {
        AppDrawerVisibilityCandidate[] candidates =
        [
            Candidate("render-device", isDefaultDevice: true, AudioSessionState.Inactive),
            Candidate(
                "capture-device",
                isDefaultDevice: false,
                AudioSessionState.Inactive,
                EDataFlow.eCapture)
        ];

        bool[] isVisible = AppDrawerVisibilityPolicy.Resolve(candidates);

        bool[] expected = [true, true];
        Assert.Equal(expected, isVisible);
    }

    [Fact]
    public void ResolveHidesExpiredCopiesWithoutSuppressingLiveCopy()
    {
        AppDrawerVisibilityCandidate[] candidates =
        [
            Candidate("expired-default", isDefaultDevice: true, AudioSessionState.Expired),
            Candidate("live-device", isDefaultDevice: false, AudioSessionState.Inactive)
        ];

        bool[] isVisible = AppDrawerVisibilityPolicy.Resolve(candidates);

        bool[] expected = [false, true];
        Assert.Equal(expected, isVisible);
    }

    [Fact]
    public void ResolveTransfersInactiveOwnershipWhenDefaultCopyArrives()
    {
        AppDrawerVisibilityCandidate[] sourceOnly =
        [
            Candidate("old-device", isDefaultDevice: false, AudioSessionState.Inactive)
        ];
        AppDrawerVisibilityCandidate[] sourceAndDestination =
        [
            Candidate("old-device", isDefaultDevice: false, AudioSessionState.Inactive),
            Candidate("new-device", isDefaultDevice: true, AudioSessionState.Inactive)
        ];

        bool[] sourceOnlyVisibility = AppDrawerVisibilityPolicy.Resolve(sourceOnly);
        bool[] sourceAndDestinationVisibility = AppDrawerVisibilityPolicy.Resolve(sourceAndDestination);

        bool[] expectedSourceOnly = [true];
        bool[] expectedSourceAndDestination = [false, true];
        Assert.Equal(expectedSourceOnly, sourceOnlyVisibility);
        Assert.Equal(expectedSourceAndDestination, sourceAndDestinationVisibility);
    }

    private static AppDrawerVisibilityCandidate Candidate(
        string deviceID,
        bool isDefaultDevice,
        AudioSessionState state,
        EDataFlow dataFlow = EDataFlow.eRender,
        string appID = "test-app") =>
        new(dataFlow, deviceID, isDefaultDevice, appID, state);
}
