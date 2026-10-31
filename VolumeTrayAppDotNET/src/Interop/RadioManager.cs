using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace VolumeTrayAppDotNET.Interop;

// Windows SDK RadioMgr.h desktop interfaces. The Bluetooth radio manager CLSID activates the
// Bluetooth-specific IMediaRadioManager, so its instance collection cannot include Wi-Fi or WWAN. The COM objects
// these hand out are marshalled uniquely, so Safe.Release frees them deterministically.
internal enum DeviceRadioState
{
    RadioOn = 0,
    SoftwareRadioOff = 1,
    HardwareRadioOff = 2,
    SoftwareAndHardwareRadioOff = 3,
    HardwareRadioOnUncontrollable = 4,
    Invalid = 5,
    HardwareRadioOffUncontrollable = 6
}

[Guid("6cfdcab5-fc47-42a5-9241-074b58830e73")]
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IMediaRadioManager
{
    [PreserveSig]
    int GetRadioInstances(
        [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IRadioInstanceCollection>))] out IRadioInstanceCollection? collection);

    void UnusedOnSystemRadioStateChange();
}

[Guid("e5791fae-5665-4e0c-95be-5fde31644185")]
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IRadioInstanceCollection
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetAt(uint index, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IRadioInstance>))] out IRadioInstance? instance);
}

[Guid("70aa1c9e-f2b4-4c61-86d3-6b9fb75fd1a2")]
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IRadioInstance
{
    // Unused methods retain their vtable slots before GetRadioState and SetRadioState. Their
    // parameter lists are intentionally omitted because VTADN never invokes those slots.
    void UnusedGetRadioManagerSignature();
    void UnusedGetInstanceSignature();
    void UnusedGetFriendlyName();

    [PreserveSig]
    int GetRadioState(out DeviceRadioState state);

    [PreserveSig]
    int SetRadioState(DeviceRadioState state, uint timeoutSeconds);
}

internal static class BluetoothRadioManagerFactory
{
    // Registered by Windows as the Bluetooth Radio Media Manager (BthRadioMedia.dll).
    private static readonly Guid ClsidBluetoothRadioManager = new("afd198ac-5f30-4e89-a789-5ddf60a69366");

    public static IMediaRadioManager Create() =>
        COMActivation.CreateInstance<IMediaRadioManager>(
            ClsidBluetoothRadioManager,
            typeof(IMediaRadioManager).GUID,
            Ole32.CLSCTX_INPROC_SERVER);
}
