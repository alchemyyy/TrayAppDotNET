using VolumeTrayAppDotNET.UI.Flyout;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class BluetoothBatteryDisplayTests
{
    [Theory]
    [InlineData(false, 64, 23, 64)]
    [InlineData(false, null, 23, null)]
    [InlineData(true, null, 23, 23)]
    [InlineData(true, null, null, null)]
    [InlineData(true, 64, 23, 23)]
    public void ResolveBluetoothBatteryLevelUsesCurrentOnlyWhileConnected(
        bool isDisconnected,
        int? currentBatteryLevel,
        int? lastKnownBatteryLevel,
        int? expected)
    {
        Assert.Equal(
            expected,
            VolumeFlyoutWindow.ResolveBluetoothBatteryLevel(
                isDisconnected,
                currentBatteryLevel,
                lastKnownBatteryLevel));
    }
}
