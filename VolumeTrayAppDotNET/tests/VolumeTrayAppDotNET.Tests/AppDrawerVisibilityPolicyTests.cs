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
            Candidate(deviceID: "old-device", isDefaultDevice: false, AudioSessionState.Inactive),
            Candidate(deviceID: "new-device", isDefaultDevice: true, AudioSessionState.Inactive)
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
            Candidate(deviceID: "routed-device", isDefaultDevice: false, AudioSessionState.Active),
            Candidate(deviceID: "default-device", isDefaultDevice: true, AudioSessionState.Inactive)
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
            Candidate(deviceID: "first-device", isDefaultDevice: false, AudioSessionState.Active),
            Candidate(deviceID: "second-device", isDefaultDevice: true, AudioSessionState.Active)
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
            Candidate(deviceID: "first-routed-device", isDefaultDevice: false, AudioSessionState.Inactive),
            Candidate(deviceID: "second-routed-device", isDefaultDevice: false, AudioSessionState.Inactive)
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
            Candidate(deviceID: "old-device", isDefaultDevice: false, AudioSessionState.Inactive),
            Candidate(deviceID: "new-device", isDefaultDevice: true, AudioSessionState.Inactive),
            Candidate(
                deviceID: "old-device",
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
            Candidate(deviceID: "render-device", isDefaultDevice: true, AudioSessionState.Inactive),
            Candidate(
                deviceID: "capture-device",
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
            Candidate(deviceID: "expired-default", isDefaultDevice: true, AudioSessionState.Expired),
            Candidate(deviceID: "live-device", isDefaultDevice: false, AudioSessionState.Inactive)
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
            Candidate(deviceID: "old-device", isDefaultDevice: false, AudioSessionState.Inactive)
        ];
        AppDrawerVisibilityCandidate[] sourceAndDestination =
        [
            Candidate(deviceID: "old-device", isDefaultDevice: false, AudioSessionState.Inactive),
            Candidate(deviceID: "new-device", isDefaultDevice: true, AudioSessionState.Inactive)
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
