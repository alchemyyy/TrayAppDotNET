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

    // Unplugged is the only disconnected state whose profile driver can accept a reconnect request
    [Theory]
    [InlineData(true, (uint)DeviceState.Unplugged, true)]
    [InlineData(true, (uint)DeviceState.NotPresent, false)]
    [InlineData(true, (uint)DeviceState.Active, false)]
    [InlineData(true, (uint)DeviceState.Disabled, false)]
    [InlineData(false, (uint)DeviceState.Unplugged, false)]
    [InlineData(false, (uint)DeviceState.NotPresent, false)]
    public void OnlyUnpluggedBluetoothEndpointsAreReconnectable(
        bool isBluetooth,
        uint state,
        bool expected)
    {
        Assert.Equal(
            expected,
            AudioDevice.IsBluetoothUnpluggedState(isBluetooth, (DeviceState)state));
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

    [Fact]
    public void CollapseDisconnectedBluetoothDevicesDefaultsToEnabled() =>
        Assert.True(new AppSettings().CollapseDisconnectedBluetoothDevicesInFlyout);

    [Theory]
    [InlineData(true, true, false, true, false, true)]
    [InlineData(true, true, false, true, true, false)]
    [InlineData(false, true, false, true, false, false)]
    [InlineData(true, false, false, true, false, false)]
    [InlineData(true, true, true, true, false, false)]
    [InlineData(true, true, false, false, false, false)]
    public void OnlyDisconnectedBluetoothEndpointsOfInactiveDevicesCollapse(
        bool isBluetooth,
        bool isDisconnected,
        bool isBluetoothOrphaned,
        bool hasContainer,
        bool isContainerConnected,
        bool expectedCollapsible)
    {
        HashSet<Guid> connectedContainers = [];
        if (isContainerConnected) connectedContainers.Add(FirstHeadset);

        Guid? container = FlyoutDeviceOrdering.ResolveCollapsibleContainer(
            isBluetooth,
            isDisconnected,
            isBluetoothOrphaned,
            hasContainer ? FirstHeadset : null,
            connectedContainers);

        Assert.Equal(expectedCollapsible, container.HasValue);
    }

    // StateGrouped is visually bottom-up, so the playback endpoint closest to the bottom is the most prominent
    [Fact]
    public void CollapsedHeadsetTakesItsMostProminentPlaybackSlot()
    {
        CollapseTestDevice[] ordered =
        [
            new(Name: "Headset microphone", EDataFlow.eCapture, FirstHeadset),
            new(Name: "Desk microphone", EDataFlow.eCapture, Container: null),
            new(Name: "Headphones", EDataFlow.eRender, FirstHeadset),
            new(Name: "Headset", EDataFlow.eRender, FirstHeadset),
            new(Name: "Speakers", EDataFlow.eRender, Container: null)
        ];

        List<CollapseTestDevice> collapsed = Collapse(ordered, FlyoutDeviceSortOrder.StateGrouped);

        Assert.Equal(["Desk microphone", "Headset", "Speakers"], collapsed.Select(static device => device.Name));
    }

    [Fact]
    public void CollapsedHeadsetTakesTopmostPlaybackSlotInWindowsOrder()
    {
        CollapseTestDevice[] ordered =
        [
            new(Name: "Headphones", EDataFlow.eRender, FirstHeadset),
            new(Name: "Speakers", EDataFlow.eRender, Container: null),
            new(Name: "Headset", EDataFlow.eRender, FirstHeadset),
            new(Name: "Headset microphone", EDataFlow.eCapture, FirstHeadset)
        ];

        List<CollapseTestDevice> collapsed = Collapse(ordered, FlyoutDeviceSortOrder.WindowsEnumeration);

        Assert.Equal(["Headphones", "Speakers"], collapsed.Select(static device => device.Name));
    }

    [Fact]
    public void CollapsedPlaybackOutranksRecordingThatSortedSooner()
    {
        CollapseTestDevice[] ordered =
        [
            new(Name: "Headphones", EDataFlow.eRender, FirstHeadset),
            new(Name: "Headset microphone", EDataFlow.eCapture, FirstHeadset)
        ];

        List<CollapseTestDevice> collapsed = Collapse(ordered, FlyoutDeviceSortOrder.StateGrouped);

        Assert.Equal(["Headphones"], collapsed.Select(static device => device.Name));
    }

    [Fact]
    public void CollapsedRowFallsBackToRecordingWithoutListedPlayback()
    {
        CollapseTestDevice[] ordered =
        [
            new(Name: "Headset microphone", EDataFlow.eCapture, FirstHeadset),
            new(Name: "Speakers", EDataFlow.eRender, Container: null)
        ];

        List<CollapseTestDevice> collapsed = Collapse(ordered, FlyoutDeviceSortOrder.StateGrouped);

        Assert.Equal(["Headset microphone", "Speakers"], collapsed.Select(static device => device.Name));
    }

    // Identically named headsets stay apart because the container, not the name, groups endpoints
    [Fact]
    public void CollapseKeepsOneRowPerDevice()
    {
        CollapseTestDevice[] ordered =
        [
            new(Name: "Headphones (first)", EDataFlow.eRender, FirstHeadset),
            new(Name: "Headphones (second)", EDataFlow.eRender, SecondHeadset),
            new(Name: "Headset (first)", EDataFlow.eRender, FirstHeadset),
            new(Name: "Headset (second)", EDataFlow.eRender, SecondHeadset)
        ];

        List<CollapseTestDevice> collapsed = Collapse(ordered, FlyoutDeviceSortOrder.StateGrouped);

        Assert.Equal(["Headset (first)", "Headset (second)"], collapsed.Select(static device => device.Name));
    }

    private static readonly Guid FirstHeadset = new("79ca349e-3234-5f32-9369-f304d62a4404");
    private static readonly Guid SecondHeadset = new("3ba371b9-a719-5d78-b530-7d17f546fbdf");

    private static List<CollapseTestDevice> Collapse(
        IReadOnlyList<CollapseTestDevice> ordered,
        FlyoutDeviceSortOrder sortOrder) =>
        FlyoutDeviceOrdering.CollapseDisconnectedBluetooth(
            ordered,
            sortOrder,
            static device => device.Container,
            static device => device.DataFlow);

    private readonly record struct TestDevice(string Name, EDataFlow DataFlow, int StateBucket);

    private readonly record struct CollapseTestDevice(string Name, EDataFlow DataFlow, Guid? Container);
}
