using System.Runtime.InteropServices;

namespace VolumeTrayAppDotNET.Audio.Interop;

/// <summary>
/// Minimal cfgmgr32 P/Invoke surface for reading Bluetooth-related devnode properties.
/// </summary>
internal static class CfgMgr32
{
    // CR_* return codes used by the readers above. SUCCESS = value was read; BUFFER_SMALL = size-
    // probe call (expected on the first CM_Get_DevNode_Property pass).
    public const int CR_SUCCESS = 0x00000000;
    public const int CR_BUFFER_SMALL = 0x0000001A;

    public const int CM_LOCATE_DEVNODE_NORMAL = 0;
    public const uint CM_GETIDLIST_FILTER_PRESENT = 0x00000100;
    public const uint DEVPROP_TYPE_BYTE = 0x00000003;
    public const uint DEVPROP_TYPE_GUID = 0x0000000D;

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    // DEVPKEY_Bluetooth_Battery: {104ea319-6ee2-4701-bd47-8ddbf425bbe5} pid 2. Byte 0-100.
    public static readonly DEVPROPKEY DEVPKEY_Bluetooth_Battery = new()
    {
        fmtid = new Guid(0x104EA319, 0x6EE2, 0x4701, 0xBD, 0x47, 0x8D, 0xDB, 0xF4, 0x25, 0xBB, 0xE5), pid = 2,
    };

    // DEVPKEY_Device_ContainerId: {8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c} pid 2. 16-byte GUID.
    public static readonly DEVPROPKEY DEVPKEY_Device_ContainerId = new()
    {
        fmtid = new Guid(0x8C7ED206, 0x3F8A, 0x4827, 0xB3, 0xAB, 0xAE, 0x9E, 0x1F, 0xAE, 0xFC, 0x6C), pid = 2,
    };

    // DEVPKEY_Device_ClassGuid: {a45c254e-df1c-4efd-8020-67d146a850e0} pid 10. 16-byte GUID.
    public static readonly DEVPROPKEY DEVPKEY_Device_ClassGuid = new()
    {
        fmtid = new Guid(0xA45C254E, 0xDF1C, 0x4EFD, 0x80, 0x20, 0x67, 0xD1, 0x46, 0xA8, 0x50, 0xE0), pid = 10,
    };

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int CM_Locate_DevNodeW(
        out uint pdnDevInst,
        [In] string pDeviceID,
        uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int CM_Get_DevNode_PropertyW(
        uint dnDevInst,
        ref DEVPROPKEY propertyKey,
        out uint propertyType,
        [Out] byte[]? propertyBuffer,
        ref uint propertyBufferSize,
        uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int CM_Get_Device_ID_List_SizeW(
        out uint pulLen,
        [In] string? pszFilter,
        uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int CM_Get_Device_ID_ListW(
        [In] string? pszFilter,
        [Out] char[] buffer,
        uint bufferLen,
        uint ulFlags);
}
