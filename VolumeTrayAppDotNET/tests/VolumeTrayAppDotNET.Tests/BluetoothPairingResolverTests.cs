using System.Runtime.CompilerServices;
using VolumeTrayAppDotNET.Audio;
using VolumeTrayAppDotNET.Interop;
using Xunit;
using LineageNode = VolumeTrayAppDotNET.Audio.BluetoothPairingResolver.LineageNode;

namespace VolumeTrayAppDotNET.Tests;

// Lineages mirror a real machine: two WH-1000XM4 pairings made through an Intel radio that has since
// been removed, and the current pairings made through a MediaTek radio. DEVPKEY_Device_Parent reports
// lowercase instance hashes while the present-devnode list reports uppercase ones.
public sealed class BluetoothPairingResolverTests
{
    private const string MediaTekRadio = @"USB\VID_13D3&PID_3602&MI_00\9&2434504c&0&0000";
    private const string MediaTekRadioAsListed = @"USB\VID_13D3&PID_3602&MI_00\9&2434504C&0&0000";
    private const string MediaTekComposite = @"USB\VID_13D3&PID_3602\000000000";
    private const string MediaTekRootHub = @"USB\ROOT_HUB30\7&137d32ad&0&0";
    private const string MediaTekStack = @"BTH\MS_BTHBRB\a&352eeb07&0&1";
    private const string MediaTekStereoProfile =
        @"BTHENUM\{0000110b-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&0d58\b&63b6b4e&0&8099E75CA52C_C00000000";
    private const string MediaTekHandsFreeService =
        @"BTHENUM\{0000111e-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&0d58\b&63b6b4e&0&8099E75CA52C_C00000000";
    private const string MediaTekHandsFreeAudio = @"BTHHFENUM\BthHFPAudio\c&8128029&0&97";

    private const string IntelRadio = @"USB\VID_8087&PID_0029\8&384b90af&0&6";
    private const string IntelRootHub = @"USB\ROOT_HUB30\7&30304c2c&0&0";
    private const string IntelStack = @"BTH\MS_BTHBRB\9&2c6d7227&0&1";
    private const string IntelStereoProfile =
        @"BTHENUM\{0000110b-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&0d58\a&18ca57&0&8099E75CA52C_C00000000";
    private const string PciRoot = @"ACPI\PNP0A08\0";

    private static readonly LineageNode[] CurrentStereoLineage =
    [
        new(MediaTekStereoProfile, IsBluetoothClass: false),
        new(MediaTekStack, IsBluetoothClass: true),
        new(MediaTekRadio, IsBluetoothClass: true),
        new(MediaTekComposite, IsBluetoothClass: false),
        new(MediaTekRootHub, IsBluetoothClass: false),
        new(PciRoot, IsBluetoothClass: false)
    ];

    private static readonly LineageNode[] CurrentHandsFreeLineage =
    [
        new(MediaTekHandsFreeAudio, IsBluetoothClass: false),
        new(MediaTekHandsFreeService, IsBluetoothClass: false),
        new(MediaTekStack, IsBluetoothClass: true),
        new(MediaTekRadio, IsBluetoothClass: true),
        new(MediaTekComposite, IsBluetoothClass: false),
        new(MediaTekRootHub, IsBluetoothClass: false),
        new(PciRoot, IsBluetoothClass: false)
    ];

    private static readonly LineageNode[] OlderStereoLineage =
    [
        new(IntelStereoProfile, IsBluetoothClass: false),
        new(IntelStack, IsBluetoothClass: true),
        new(IntelRadio, IsBluetoothClass: true),
        new(IntelRootHub, IsBluetoothClass: false),
        new(PciRoot, IsBluetoothClass: false)
    ];

    [Fact]
    public void CurrentPairingWithRadioOffIsRadioOffPairing()
    {
        HashSet<string> present = Present(MediaTekRadioAsListed, MediaTekComposite, MediaTekRootHub, IntelRootHub, PciRoot);

        Assert.True(BluetoothPairingResolver.IsRadioOffPairing(CurrentStereoLineage, present));
        Assert.True(BluetoothPairingResolver.IsRadioOffPairing(CurrentHandsFreeLineage, present));
    }

    [Fact]
    public void CurrentPairingWithRadioOnIsNotRadioOffPairing()
    {
        HashSet<string> present = Present(
            MediaTekStereoProfile, MediaTekHandsFreeService, MediaTekHandsFreeAudio, MediaTekStack,
            MediaTekRadioAsListed, MediaTekComposite, MediaTekRootHub, PciRoot);

        Assert.False(BluetoothPairingResolver.IsRadioOffPairing(CurrentStereoLineage, present));
        Assert.False(BluetoothPairingResolver.IsRadioOffPairing(CurrentHandsFreeLineage, present));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PairingThroughRemovedRadioIsNeverRadioOffPairing(bool isCurrentRadioOn)
    {
        HashSet<string> present = isCurrentRadioOn
            ? Present(MediaTekStack, MediaTekRadioAsListed, MediaTekComposite, MediaTekRootHub, IntelRootHub, PciRoot)
            : Present(MediaTekRadioAsListed, MediaTekComposite, MediaTekRootHub, IntelRootHub, PciRoot);

        Assert.False(BluetoothPairingResolver.IsRadioOffPairing(OlderStereoLineage, present));
    }

    // An unpaired device or a disabled hands-free service leaves the profile devnodes absent while the stack is up
    [Fact]
    public void MissingProfileWhileRadioOnIsNotRadioOffPairing()
    {
        HashSet<string> present = Present(MediaTekStack, MediaTekRadioAsListed, MediaTekComposite, MediaTekRootHub, PciRoot);

        Assert.False(BluetoothPairingResolver.IsRadioOffPairing(CurrentHandsFreeLineage, present));
    }

    [Fact]
    public void LineageWithoutRadioAdapterIsNeverRadioOffPairing()
    {
        LineageNode[] wiredLineage =
        [
            new(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0897\5&1b9c8b3&0&0001", IsBluetoothClass: false),
            new(PciRoot, IsBluetoothClass: false)
        ];
        HashSet<string> present = Present(PciRoot);

        Assert.False(BluetoothPairingResolver.IsRadioOffPairing(wiredLineage, present));
        Assert.False(BluetoothPairingResolver.IsRadioOffPairing([], present));
    }

    [Fact]
    public void RadioAdapterIsTopOfFirstBluetoothRun()
    {
        Assert.Equal(expected: 2, BluetoothPairingResolver.FindRadioAdapterIndex(CurrentStereoLineage));
        Assert.Equal(expected: 3, BluetoothPairingResolver.FindRadioAdapterIndex(CurrentHandsFreeLineage));
        Assert.Equal(expected: 2, BluetoothPairingResolver.FindRadioAdapterIndex(OlderStereoLineage));
        Assert.Equal(expected: -1, BluetoothPairingResolver.FindRadioAdapterIndex([]));
    }

    [Theory]
    [InlineData(@"{1}.BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0D58\B&63B6B4E&0&8099E75CA52C_C00000000",
        @"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0D58\B&63B6B4E&0&8099E75CA52C_C00000000")]
    [InlineData(@"BTHHFENUM\BthHFPAudio\c&8128029&0&97", @"BTHHFENUM\BthHFPAudio\c&8128029&0&97")]
    [InlineData(@"{1}BTHENUM\missing-separator", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void RecordedAdapterPathLosesItsPrefix(string? recordedPath, string expected) =>
        Assert.Equal(expected, BluetoothPairingResolver.ParseRecordedAdapterInstanceID(recordedPath));

    [Fact]
    public void EndpointWithoutRecordedAdapterStartsAtItsSoftwareDevnode()
    {
        const string endpointID = "{0.0.0.00000000}.{031f2cba-f397-4334-84c2-312cba509f6e}";

        Assert.Equal(
            @"SWD\MMDEVAPI\{0.0.0.00000000}.{031f2cba-f397-4334-84c2-312cba509f6e}",
            BluetoothPairingResolver.ResolveLineageStart(endpointID, recordedAdapterPath: null));
        Assert.Equal(
            MediaTekStereoProfile,
            BluetoothPairingResolver.ResolveLineageStart(endpointID, "{1}." + MediaTekStereoProfile));
    }

    [Fact]
    public void DeviceQueryStructuresMatchNativeX64Layouts()
    {
        Assert.Equal(expected: 32, Unsafe.SizeOf<CfgMgr32.DEVPROPCOMPKEY>());
        Assert.Equal(expected: 48, Unsafe.SizeOf<CfgMgr32.DEVPROPERTY>());
        Assert.Equal(expected: 56, Unsafe.SizeOf<CfgMgr32.DEVPROP_FILTER_EXPRESSION>());
        Assert.Equal(expected: 32, Unsafe.SizeOf<CfgMgr32.DEV_OBJECT>());
    }

    // Radio-off classification only rescues NotPresent endpoints; live states are never orphaned
    [Theory]
    [InlineData(true, (uint)DeviceState.NotPresent, false, true)]
    [InlineData(true, (uint)DeviceState.NotPresent, true, false)]
    [InlineData(true, (uint)DeviceState.Unplugged, false, false)]
    [InlineData(true, (uint)DeviceState.Active, false, false)]
    [InlineData(true, (uint)DeviceState.Disabled, false, false)]
    [InlineData(false, (uint)DeviceState.NotPresent, false, false)]
    public void OnlyUnclassifiedNotPresentBluetoothEndpointsAreOrphaned(
        bool isBluetooth,
        uint state,
        bool isRadioOffPairing,
        bool expected) =>
        Assert.Equal(expected, AudioDevice.IsBluetoothOrphanedState(isBluetooth, (DeviceState)state, isRadioOffPairing));

    private static HashSet<string> Present(params string[] instanceIDs) =>
        new(instanceIDs, StringComparer.OrdinalIgnoreCase);
}
