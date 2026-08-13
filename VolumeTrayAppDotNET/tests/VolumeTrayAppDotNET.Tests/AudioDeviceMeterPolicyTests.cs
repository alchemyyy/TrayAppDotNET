using VolumeTrayAppDotNET.Audio;
using VolumeTrayAppDotNET.Interop;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class AudioDeviceMeterPolicyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CaptureEndpointIsPolledRegardlessOfSessionVisibility(
        bool hasActiveSession,
        bool isExclusiveControlHeld)
    {
        bool shouldPin = AudioDevice.ShouldPinEndpointMeterToSilence(
            EDataFlow.eCapture,
            hasActiveSession,
            isExclusiveControlHeld);

        Assert.False(shouldPin);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void RenderEndpointRetainsSessionAndExclusiveModeGate(
        bool hasActiveSession,
        bool isExclusiveControlHeld,
        bool expectedShouldPin)
    {
        bool shouldPin = AudioDevice.ShouldPinEndpointMeterToSilence(
            EDataFlow.eRender,
            hasActiveSession,
            isExclusiveControlHeld);

        Assert.Equal(expectedShouldPin, shouldPin);
    }
}
