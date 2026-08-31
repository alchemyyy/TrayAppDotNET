using VolumeTrayAppDotNET.Audio;
using VolumeTrayAppDotNET.Interop;
using VolumeTrayAppDotNET.Models;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class FlyoutDeviceOrderingTests
{
    [Fact]
    public void OrderStateGroupedSeparatesCaptureFromRenderAcrossStateBuckets()
    {
        TestDevice[] devices =
        [
            new(Name: "Default microphone", EDataFlow.eCapture, StateBucket: 0),
            new(Name: "Digital output playback", EDataFlow.eRender, StateBucket: 3),
            new(Name: "Bluetooth headset microphone", EDataFlow.eCapture, StateBucket: 2),
            new(Name: "Disconnected microphone", EDataFlow.eCapture, StateBucket: 4),
            new(Name: "Bluetooth headset playback", EDataFlow.eRender, StateBucket: 0)
        ];

        List<TestDevice> ordered = FlyoutDeviceOrdering.OrderStateGrouped(
            devices,
            intermix: false,
            static device => device.DataFlow,
            static device => device.StateBucket);

        Assert.Equal(
            [
                "Disconnected microphone",
                "Bluetooth headset microphone",
                "Default microphone",
                "Digital output playback",
                "Bluetooth headset playback"
            ],
            ordered.Select(static device => device.Name));
    }

    [Fact]
    public void OrderStateGroupedPreservesStateFirstOrderingWhenIntermixed()
    {
        TestDevice[] devices =
        [
            new(Name: "Default microphone", EDataFlow.eCapture, StateBucket: 0),
            new(Name: "Digital output playback", EDataFlow.eRender, StateBucket: 3),
            new(Name: "Bluetooth headset microphone", EDataFlow.eCapture, StateBucket: 2),
            new(Name: "Disconnected microphone", EDataFlow.eCapture, StateBucket: 4),
            new(Name: "Bluetooth headset playback", EDataFlow.eRender, StateBucket: 0)
        ];

        List<TestDevice> ordered = FlyoutDeviceOrdering.OrderStateGrouped(
            devices,
            intermix: true,
            static device => device.DataFlow,
            static device => device.StateBucket);

        Assert.Equal(
            [
                "Disconnected microphone",
                "Digital output playback",
                "Bluetooth headset microphone",
                "Bluetooth headset playback",
                "Default microphone"
            ],
            ordered.Select(static device => device.Name));
    }

    [Theory]
    [InlineData(FlyoutDisconnectedBluetoothDeviceVisibility.NeverShow, false,
        (int)FlyoutDeviceOrdering.DisconnectedBluetoothPlacement.Hidden)]
    [InlineData(FlyoutDisconnectedBluetoothDeviceVisibility.NeverShow, true,
        (int)FlyoutDeviceOrdering.DisconnectedBluetoothPlacement.Hidden)]
    [InlineData(FlyoutDisconnectedBluetoothDeviceVisibility.Show, false,
        (int)FlyoutDeviceOrdering.DisconnectedBluetoothPlacement.Hidden)]
    [InlineData(FlyoutDisconnectedBluetoothDeviceVisibility.Show, true,
        (int)FlyoutDeviceOrdering.DisconnectedBluetoothPlacement.Standard)]
    [InlineData(FlyoutDisconnectedBluetoothDeviceVisibility.AlwaysShow, false,
        (int)FlyoutDeviceOrdering.DisconnectedBluetoothPlacement.DedicatedSection)]
    [InlineData(FlyoutDisconnectedBluetoothDeviceVisibility.AlwaysShow, true,
        (int)FlyoutDeviceOrdering.DisconnectedBluetoothPlacement.DedicatedSection)]
    [InlineData(FlyoutDisconnectedBluetoothDeviceVisibility.AlwaysShowIntermixed, false,
        (int)FlyoutDeviceOrdering.DisconnectedBluetoothPlacement.Standard)]
    [InlineData(FlyoutDisconnectedBluetoothDeviceVisibility.AlwaysShowIntermixed, true,
        (int)FlyoutDeviceOrdering.DisconnectedBluetoothPlacement.Standard)]
    public void ClassifyDisconnectedBluetoothImplementsVisibilityMode(
        FlyoutDisconnectedBluetoothDeviceVisibility visibility,
        bool normallyVisible,
        int expected)
    {
        Assert.Equal(
            expected,
            (int)FlyoutDeviceOrdering.ClassifyDisconnectedBluetooth(visibility, normallyVisible));
    }

    [Theory]
    [InlineData(FlyoutDisconnectedBluetoothDeviceVisibility.NeverShow,
        (int)FlyoutDeviceOrdering.DisconnectedBluetoothPlacement.Hidden)]
    [InlineData(FlyoutDisconnectedBluetoothDeviceVisibility.Show,
        (int)FlyoutDeviceOrdering.DisconnectedBluetoothPlacement.Standard)]
    [InlineData(FlyoutDisconnectedBluetoothDeviceVisibility.AlwaysShow,
        (int)FlyoutDeviceOrdering.DisconnectedBluetoothPlacement.DedicatedSection)]
    [InlineData(FlyoutDisconnectedBluetoothDeviceVisibility.AlwaysShowIntermixed,
        (int)FlyoutDeviceOrdering.DisconnectedBluetoothPlacement.Standard)]
    public void ConnectionActivitySurfacesBluetoothRowsUnlessExplicitlyHidden(
        FlyoutDisconnectedBluetoothDeviceVisibility visibility,
        int expected)
    {
        Assert.Equal(
            expected,
            (int)FlyoutDeviceOrdering.ClassifyDisconnectedBluetooth(
                visibility,
                normallyVisible: false,
                hasConnectionActivity: true));
    }

    [Fact]
    public void DisconnectedBluetoothVisibilityDefaultsToAlwaysShowIntermixed()
    {
        Assert.Equal(
            FlyoutDisconnectedBluetoothDeviceVisibility.AlwaysShowIntermixed,
            new AppSettings().FlyoutDisconnectedBluetoothDeviceVisibility);
    }

    [Fact]
    public void BluetoothRadioVisibilityGateDefaultsToEnabled() =>
        Assert.True(new AppSettings().ShowBluetoothDevicesOnlyWhenBluetoothIsOn);

    [Theory]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    public void BluetoothRadioGateOnlyHidesBluetoothDevicesWhenConfiguredAndOff(
        bool isBluetoothDevice,
        bool showOnlyWhenBluetoothIsOn,
        bool isBluetoothRadioEnabled,
        bool expected)
    {
        Assert.Equal(
            expected,
            FlyoutDeviceOrdering.PassesBluetoothRadioGate(
                isBluetoothDevice,
                showOnlyWhenBluetoothIsOn,
                isBluetoothRadioEnabled));
    }

    [Theory]
    [InlineData(FlyoutDeviceSortOrder.StateGrouped,
        "Disconnected Bluetooth playback,Disconnected Bluetooth recording,Playback,Recording")]
    [InlineData(FlyoutDeviceSortOrder.WindowsEnumeration,
        "Playback,Recording,Disconnected Bluetooth playback,Disconnected Bluetooth recording")]
    public void AlwaysShowSectionFollowsSortDirectionAfterBothNormalFlows(
        FlyoutDeviceSortOrder sortOrder,
        string expectedOrder)
    {
        string[] normallyOrdered = ["Playback", "Recording"];
        string[] dedicatedSection = ["Disconnected Bluetooth playback", "Disconnected Bluetooth recording"];

        List<string> combined = FlyoutDeviceOrdering.PlaceDedicatedSection(
            normallyOrdered,
            dedicatedSection,
            sortOrder);

        Assert.Equal(expectedOrder.Split(','), combined);
    }

    private readonly record struct TestDevice(string Name, EDataFlow DataFlow, int StateBucket);
}
