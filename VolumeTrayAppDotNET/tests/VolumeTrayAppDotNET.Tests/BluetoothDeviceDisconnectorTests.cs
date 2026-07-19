using VolumeTrayAppDotNET.Audio;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class BluetoothDeviceDisconnectorTests
{
    [Theory]
    [InlineData(@"BTHENUM\DEV_001122AABBCC\7&123456&0&BLUETOOTHDEVICE_001122AABBCC", 0x001122AABBCCul)]
    [InlineData(@"BTHLE\dev_a1b2c3d4e5f6\8&ABCDEF&0&a1b2c3d4e5f6", 0xA1B2C3D4E5F6ul)]
    [InlineData("BTHENUM\\DEV_ABCDEF123456", 0xABCDEF123456ul)]
    public void TryParseAddress_ParsesBluetoothDevnodeIds(string deviceInstanceId, ulong expected)
    {
        Assert.True(BluetoothDeviceDisconnector.TryParseAddress(deviceInstanceId, out ulong actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}")]
    [InlineData(@"BTHENUM\DEV_001122AABBC")]
    [InlineData(@"BTHENUM\DEV_001122AABBCG")]
    [InlineData(@"BTHENUM\DEV_001122AABBCCD")]
    [InlineData(@"BTHENUM\DEV_000000000000")]
    public void TryParseAddress_RejectsMissingOrMalformedAddresses(string? deviceInstanceId)
    {
        Assert.False(BluetoothDeviceDisconnector.TryParseAddress(deviceInstanceId, out ulong actual));
        Assert.Equal(0ul, actual);
    }
}
