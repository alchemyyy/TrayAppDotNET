using BrightnessTrayAppDotNET.DDCCI;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class DisplayServiceTests
{
    [Fact]
    public void ParentProcessAlwaysUsesPositiveKillableOperationTimeout()
    {
        using DisplayService displayService = new();

        displayService.OperationTimeoutMs = 0;
        Assert.Equal(TimeConstants.DisplayServiceOperationTimeoutMs, displayService.OperationTimeoutMs);

        displayService.OperationTimeoutMs = 1;
        Assert.Equal(TimeConstants.DDCOperationTimeoutSafetyFloorMs, displayService.OperationTimeoutMs);
    }

    [Fact]
    public void HelperProcessMayRunInlineWithoutParentTimeout()
    {
        using DisplayService displayService = new(false);

        displayService.OperationTimeoutMs = 0;

        Assert.Equal(expected: 0, displayService.OperationTimeoutMs);
    }

    [Theory]
    [InlineData("GetVCPFeatureAndVCPFeatureReply failed (Win32: -1071241845)")]
    [InlineData("GetVCPFeatureAndVCPFeatureReply failed (Win32: -1071241845, 0xC026258B)")]
    public void InvalidMessageChecksumIsRecognizedAcrossOldAndNewLogFormats(string error) =>
        Assert.True(DDCNativeError.IsInvalidMessageChecksum(error));

    // An invalid message command is garbled too, but only a checksum failure lets an acknowledged recovery SET
    // establish read-degraded state while blind writes are off
    [Theory]
    [InlineData("GetVCPFeature failed with a generic transport error")]
    [InlineData("GetVCPFeatureAndVCPFeatureReply failed (Win32: -1071241847, 0xC0262589)")]
    public void OtherFailuresAreNotClassifiedAsInvalidMessageChecksum(string error) =>
        Assert.False(DDCNativeError.IsInvalidMessageChecksum(error));

    // Checksum (0x8B) as the hdmi-relink recorder saw it; invalid message command (0x89) and invalid data (0x85) as
    // DISPLAY2's wedged reply pipeline answered on 2026-10-29
    [Theory]
    [InlineData("GetVCPFeatureAndVCPFeatureReply failed (Win32: -1071241845)")]
    [InlineData("GetVCPFeatureAndVCPFeatureReply failed (Win32: -1071241845, 0xC026258B)")]
    [InlineData("GetVCPFeatureAndVCPFeatureReply failed (Win32: -1071241847)")]
    [InlineData("GetVCPFeatureAndVCPFeatureReply failed (Win32: -1071241847, 0xC0262589)")]
    [InlineData("GetVCPFeatureAndVCPFeatureReply failed (Win32: -1071241851, 0xC0262585)")]
    public void GarbledReplyIsRecognizedAcrossOldAndNewLogFormats(string error) =>
        Assert.True(DDCNativeError.IsGarbledReply(error));

    // A NAK means nothing answered, as from a dark panel on a direct link
    [Theory]
    [InlineData("GetVCPFeatureAndVCPFeatureReply failed (Win32: -1071241854, 0xC0262582)")]
    [InlineData("GetVCPFeature failed with a generic transport error")]
    public void MissingReplyIsNotClassifiedAsGarbled(string error) =>
        Assert.False(DDCNativeError.IsGarbledReply(error));
}
