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
        using DisplayService displayService = new(useHelperProcess: false);

        displayService.OperationTimeoutMs = 0;

        Assert.Equal(0, displayService.OperationTimeoutMs);
    }

    [Theory]
    [InlineData("GetVCPFeatureAndVCPFeatureReply failed (Win32: -1071241845)")]
    [InlineData("GetVCPFeatureAndVCPFeatureReply failed (Win32: -1071241845, 0xC026258B)")]
    public void InvalidMessageChecksumIsRecognizedAcrossOldAndNewLogFormats(string error)
    {
        Assert.True(DDCNativeError.IsInvalidMessageChecksum(error));
    }

    [Fact]
    public void GenericDDCFailureIsNotClassifiedAsInvalidMessageChecksum()
    {
        Assert.False(DDCNativeError.IsInvalidMessageChecksum("GetVCPFeature failed with a generic transport error"));
    }
}
