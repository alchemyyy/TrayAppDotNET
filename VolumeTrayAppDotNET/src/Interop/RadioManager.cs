using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace VolumeTrayAppDotNET.Interop;

// Windows SDK RadioMgr.h desktop interfaces. The Bluetooth radio manager CLSID activates the
// Bluetooth-specific IMediaRadioManager, so its instance collection cannot include Wi-Fi or WWAN.
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
    int GetRadioInstances(out IRadioInstanceCollection? collection);

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
    int GetAt(uint index, out IRadioInstance? instance);
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

internal static partial class BluetoothRadioManagerFactory
{
    // Registered by Windows as the Bluetooth Radio Media Manager (BthRadioMedia.dll).
    private static readonly Guid ClsidBluetoothRadioManager = new("afd198ac-5f30-4e89-a789-5ddf60a69366");

    public static IMediaRadioManager Create()
    {
        Guid clsid = ClsidBluetoothRadioManager;
        Guid iid = typeof(IMediaRadioManager).GUID;
        int result = CoCreateInstance(
            in clsid,
            IntPtr.Zero,
            (uint)ClsCtx.INPROC_SERVER,
            in iid,
            out IMediaRadioManager manager);
        if (result < 0) Marshal.ThrowExceptionForHR(result);
        return manager;
    }

    [LibraryImport("ole32.dll", EntryPoint = "CoCreateInstance")]
    private static partial int CoCreateInstance(
        in Guid classId,
        IntPtr outerUnknown,
        uint classContext,
        in Guid interfaceId,
        [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IMediaRadioManager>))]
        out IMediaRadioManager instance);
}
