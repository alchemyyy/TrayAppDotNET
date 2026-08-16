using System.Runtime.InteropServices;
using Avalonia.Input;
using VolumeTrayAppDotNET.Audio;
using VolumeTrayAppDotNET.Interop;
using VolumeTrayAppDotNET.UI.Flyout;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class BluetoothConnectionDisplayTests
{
    [Fact]
    public void ConfigurationManagerNotificationFilterMatchesNativeLayout()
    {
        Assert.Equal(416, Marshal.SizeOf<CfgMgr32.CMNotifyFilter>());
    }

    [Fact]
    public void ClassicBluetoothConnectionStructuresMatchNativeX64Layouts()
    {
        Assert.Equal(40, Marshal.SizeOf<BluetoothApis.BLUETOOTH_DEVICE_SEARCH_PARAMS>());
        Assert.Equal(560, Marshal.SizeOf<BluetoothApis.BLUETOOTH_DEVICE_INFO>());
    }

    [Fact]
    public void RememberedBluetoothDevnodeIsNotTreatedAsConnected()
    {
        Guid containerID = Guid.NewGuid();
        Dictionary<string, Guid> idToContainer = new(StringComparer.Ordinal) { { @"BTHENUM\DEV_8099E75CA52C\7&123456&0&BLUETOOTHDEVICE_8099E75CA52C", containerID } };
        HashSet<ulong> connectedAddresses = [];

        HashSet<Guid> connectedContainers = BluetoothBatteryMonitor.ResolveConnectedContainers(
            idToContainer,
            connectedAddresses);

        Assert.Empty(connectedContainers);
    }

    [Fact]
    public void ConnectedBluetoothAddressMapsToItsPnPContainer()
    {
        const ulong connectedAddress = 0x8099E75CA52Cul;
        Guid connectedContainerID = Guid.NewGuid();
        Guid disconnectedContainerID = Guid.NewGuid();
        Dictionary<string, Guid> idToContainer = new(StringComparer.Ordinal)
        {
            { @"BTHENUM\DEV_8099E75CA52C", connectedContainerID },
            { @"BTHENUM\DEV_8099E7B6BC69", disconnectedContainerID }
        };
        HashSet<ulong> connectedAddresses =
        [
            connectedAddress
        ];

        HashSet<Guid> connectedContainers = BluetoothBatteryMonitor.ResolveConnectedContainers(
            idToContainer,
            connectedAddresses);

        Assert.Contains(connectedContainerID, connectedContainers);
        Assert.DoesNotContain(disconnectedContainerID, connectedContainers);
    }

    [Theory]
    [InlineData(30_000, 0, 30)]
    [InlineData(30_000, 1, 30)]
    [InlineData(30_000, 29_000, 1)]
    [InlineData(30_000, 29_999, 1)]
    [InlineData(30_000, 30_000, 0)]
    [InlineData(30_000, 31_000, 0)]
    public void ResolveBluetoothConnectionSecondsRemainingRoundsUpUntilDeadline(
        long deadlineMilliseconds,
        long nowMilliseconds,
        int expected)
    {
        int actual = AudioDevice.ResolveBluetoothConnectionSecondsRemaining(
            deadlineMilliseconds,
            nowMilliseconds);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(1_000, 0, 31_000)]
    [InlineData(1_000, 31_000, 31_001)]
    [InlineData(2_000, 31_000, 32_000)]
    public void RetryDeadlineIsFreshAndUnique(
        long nowMilliseconds,
        long previousDeadlineMilliseconds,
        long expected)
    {
        long actual = AudioDeviceManager.ResolveBluetoothConnectionAttemptDeadline(
            nowMilliseconds,
            previousDeadlineMilliseconds);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(30_000, 0, 1.0)]
    [InlineData(30_000, 15_000, 0.5)]
    [InlineData(30_000, 30_000, 0.0)]
    [InlineData(30_000, 40_000, 0.0)]
    [InlineData(40_000, 0, 1.0)]
    public void CountdownOverlayFractionIsClamped(
        long deadlineMilliseconds,
        long nowMilliseconds,
        double expected)
    {
        double actual = BluetoothConnectionCountdownOverlay.ResolveRemainingFraction(
            deadlineMilliseconds,
            nowMilliseconds,
            timeoutMilliseconds: 30_000);

        Assert.Equal(expected, actual, precision: 6);
    }

    [Theory]
    [InlineData(true, true, KeyModifiers.None, (int)BluetoothButtonAction.Retry)]
    [InlineData(true, false, KeyModifiers.None, (int)BluetoothButtonAction.Connect)]
    [InlineData(false, false, KeyModifiers.Control, (int)BluetoothButtonAction.Disconnect)]
    [InlineData(false, false, KeyModifiers.None, (int)BluetoothButtonAction.None)]
    public void BluetoothButtonRoutesPendingClicksToRetry(
        bool isDisconnected,
        bool isConnectionPending,
        KeyModifiers keyModifiers,
        int expected)
    {
        BluetoothButtonAction actual = VolumeFlyoutWindow.ResolveBluetoothButtonAction(
            isDisconnected,
            isConnectionPending,
            keyModifiers);

        Assert.Equal(expected, (int)actual);
    }

    [Theory]
    [InlineData("2 channel, 24 bit, 96000 Hz", "LDAC", "Connection Pending: 18s",
        "2 channel, 24 bit, 96000 Hz, LDAC, Connection Pending: 18s")]
    [InlineData("2 channel, 24 bit, 96000 Hz", "", "Connected - Audio Waiting",
        "2 channel, 24 bit, 96000 Hz, Connected - Audio Waiting")]
    [InlineData("", "", "Connected - Audio Waiting", "Connected - Audio Waiting")]
    [InlineData("", "AAC", "", "AAC")]
    public void BuildDeviceFormatLineAppendsConnectionState(
        string format,
        string codec,
        string connectionStatus,
        string expected)
    {
        string actual = VolumeFlyoutWindow.BuildDeviceFormatLine(format, codec, connectionStatus);

        Assert.Equal(expected, actual);
    }
}
