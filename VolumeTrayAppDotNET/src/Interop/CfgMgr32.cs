using System.Runtime.InteropServices;
using System.Text;

namespace VolumeTrayAppDotNET.Interop;

/// <summary>
/// Minimal cfgmgr32 P/Invoke surface for reading Bluetooth-related devnode properties and for
/// querying the Windows pairing store through the Device Query API that cfgmgr32 also exports.
/// </summary>
internal static class CfgMgr32
{
    // CR_* return codes used by the readers below. SUCCESS = value was read; BUFFER_SMALL = size-
    // probe call (expected on the first CM_Get_DevNode_Property pass).
    public const int CR_SUCCESS = 0x00000000;
    public const int CR_BUFFER_SMALL = 0x0000001A;

    public const int CM_LOCATE_DEVNODE_NORMAL = 0;
    public const uint CM_LOCATE_DEVNODE_PHANTOM = 0x00000001;
    public const uint CM_GETIDLIST_FILTER_PRESENT = 0x00000100;
    public const uint CM_NOTIFY_FILTER_FLAG_ALL_DEVICE_INSTANCES = 0x00000002;
    public const uint CM_NOTIFY_FILTER_TYPE_DEVICEINSTANCE = 2;
    public const uint DEVPROP_TYPE_BYTE = 0x00000003;
    public const uint DEVPROP_TYPE_GUID = 0x0000000D;
    public const uint DEVPROP_TYPE_BOOLEAN = 0x00000011;
    public const uint DEVPROP_TYPE_STRING = 0x00000012;
    public const byte DEVPROP_TRUE = 0xFF;
    public const uint DEVPROP_STORE_SYSTEM = 0;

    // Device Query API (devquery.h, devfiltertypes.h). A filter is a flat expression array; logical
    // groups open and close with operator-only entries whose property is left zeroed.
    public const int DevObjectTypeAEP = 5;
    public const uint DevQueryFlagNone = 0;
    public const uint DEVPROP_OPERATOR_EQUALS = 0x00000002;
    public const uint DEVPROP_OPERATOR_AND_OPEN = 0x00100000;
    public const uint DEVPROP_OPERATOR_AND_CLOSE = 0x00200000;
    public const uint DEVPROP_OPERATOR_OR_OPEN = 0x00300000;
    public const uint DEVPROP_OPERATOR_OR_CLOSE = 0x00400000;

    public const uint CM_NOTIFY_ACTION_DEVICEINSTANCEENUMERATED = 7;
    public const uint CM_NOTIFY_ACTION_DEVICEINSTANCESTARTED = 8;
    public const uint CM_NOTIFY_ACTION_DEVICEINSTANCEREMOVED = 9;

    private const int MaxDeviceIDLength = 200;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate uint CMNotifyCallback(
        IntPtr notification,
        IntPtr context,
        uint action,
        IntPtr eventData,
        uint eventDataSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DEVPROPCOMPKEY
    {
        public DEVPROPKEY Key;
        public uint Store;
        public char* LocaleName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DEVPROPERTY
    {
        public DEVPROPCOMPKEY CompKey;
        public uint Type;
        public uint BufferSize;
        public void* Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROP_FILTER_EXPRESSION
    {
        public uint Operator;
        public DEVPROPERTY Property;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DEV_OBJECT
    {
        public int ObjectType;
        public char* ObjectID;
        public uint PropertyCount;
        public DEVPROPERTY* Properties;
    }

    // The native union's largest member is WCHAR InstanceId[MAX_DEVICE_ID_LEN]. The all-device
    // registration uses an empty instance id, so an explicitly zeroed union is sufficient.
    [StructLayout(LayoutKind.Explicit, Size = 16 + MaxDeviceIDLength * sizeof(char))]
    public struct CMNotifyFilter
    {
        [FieldOffset(0)]
        public uint Size;

        [FieldOffset(4)]
        public uint Flags;

        [FieldOffset(8)]
        public uint FilterType;

        [FieldOffset(12)]
        public uint Reserved;
    }

    // DEVPKEY_Bluetooth_Battery: {104ea319-6ee2-4701-bd47-8ddbf425bbe5} pid 2. Byte 0-100.
    public static readonly DEVPROPKEY DEVPKEY_Bluetooth_Battery = new()
    {
        fmtid = new Guid(a: 0x104EA319, b: 0x6EE2, c: 0x4701, d: 0xBD, e: 0x47, f: 0x8D, g: 0xDB, h: 0xF4, i: 0x25,
            j: 0xBB, k: 0xE5),
        pid = 2
    };

    // DEVPKEY_Device_ContainerId: {8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c} pid 2. 16-byte GUID.
    public static readonly DEVPROPKEY DEVPKEY_Device_ContainerId = new()
    {
        fmtid = new Guid(a: 0x8C7ED206, b: 0x3F8A, c: 0x4827, d: 0xB3, e: 0xAB, f: 0xAE, g: 0x9E, h: 0x1F, i: 0xAE,
            j: 0xFC, k: 0x6C),
        pid = 2
    };

    // DEVPKEY_Device_ClassGuid: {a45c254e-df1c-4efd-8020-67d146a850e0} pid 10. 16-byte GUID.
    public static readonly DEVPROPKEY DEVPKEY_Device_ClassGuid = new()
    {
        fmtid = new Guid(a: 0xA45C254E, b: 0xDF1C, c: 0x4EFD, d: 0x80, e: 0x20, f: 0x67, g: 0xD1, h: 0x46, i: 0xA8,
            j: 0x50, k: 0xE0),
        pid = 10
    };

    // DEVPKEY_Device_Parent: {4340a6c5-93fa-4706-972c-7b648008a5a7} pid 8. Parent instance id string.
    // NOTE: Also readable on phantom devnodes, which is what lets a lineage walk run with the radio off
    public static readonly DEVPROPKEY DEVPKEY_Device_Parent = new()
    {
        fmtid = new Guid(a: 0x4340A6C5, b: 0x93FA, c: 0x4706, d: 0x97, e: 0x2C, f: 0x7B, g: 0x64, h: 0x80, i: 0x08,
            j: 0xA5, k: 0xA7),
        pid = 8
    };

    // PKEY_Devices_Aep_ProtocolId: {3b2ce006-5e61-4fde-bab8-9b8aac9b26df} pid 5 (propkey.h). GUID.
    public static readonly DEVPROPKEY PKEY_Devices_Aep_ProtocolId = new()
    {
        fmtid = new Guid(a: 0x3B2CE006, b: 0x5E61, c: 0x4FDE, d: 0xBA, e: 0xB8, f: 0x9B, g: 0x8A, h: 0xAC, i: 0x9B,
            j: 0x26, k: 0xDF),
        pid = 5
    };

    // PKEY_Devices_Aep_IsPaired: {a35996ab-11cf-4935-8b61-a6761081ecdf} pid 16 (propkey.h). Boolean.
    public static readonly DEVPROPKEY PKEY_Devices_Aep_IsPaired = new()
    {
        fmtid = new Guid(a: 0xA35996AB, b: 0x11CF, c: 0x4935, d: 0x8B, e: 0x61, f: 0xA6, g: 0x76, h: 0x10, i: 0x81,
            j: 0xEC, k: 0xDF),
        pid = 16
    };

    // PKEY_Devices_Aep_ContainerId: {e7c3fb29-caa7-4f47-8c8b-be59b330d4c5} pid 2 (propkey.h). GUID.
    public static readonly DEVPROPKEY PKEY_Devices_Aep_ContainerId = new()
    {
        fmtid = new Guid(a: 0xE7C3FB29, b: 0xCAA7, c: 0x4F47, d: 0x8C, e: 0x8B, f: 0xBE, g: 0x59, h: 0xB3, i: 0x30,
            j: 0xD4, k: 0xC5),
        pid = 2
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

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    public static extern int CM_Register_Notification(
        ref CMNotifyFilter filter,
        IntPtr context,
        CMNotifyCallback callback,
        out IntPtr notification);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    public static extern int CM_Unregister_Notification(IntPtr notification);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    public static extern unsafe int DevGetObjects(
        int objectType,
        uint queryFlags,
        uint requestedPropertyCount,
        DEVPROPCOMPKEY* requestedProperties,
        uint filterExpressionCount,
        DEVPROP_FILTER_EXPRESSION* filter,
        out uint objectCount,
        out DEV_OBJECT* objects);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    public static extern unsafe void DevFreeObjects(uint objectCount, DEV_OBJECT* objects);

    // CM_Get_DevNode_Property: read a single byte property (DEVPROP_TYPE_BYTE) off a located
    // devnode handle. Returns null on any CR_* failure / type mismatch / out-of-range value.
    public static int? TryReadByteProperty(uint devInst, DEVPROPKEY key)
    {
        uint size = 0;
        int cr = CM_Get_DevNode_PropertyW(devInst, ref key, out uint propType, propertyBuffer: null, ref size,
            ulFlags: 0);
        if (cr is not CR_BUFFER_SMALL and not CR_SUCCESS) return null;
        if (propType != DEVPROP_TYPE_BYTE || size < 1) return null;

        byte[] buf = new byte[size];
        cr = CM_Get_DevNode_PropertyW(devInst, ref key, out propType, buf, ref size, ulFlags: 0);
        if (cr != CR_SUCCESS) return null;

        int level = buf[0];
        return level is >= 0 and <= 100 ? level : null;
    }

    // CM_Get_DevNode_Property: read a 16-byte GUID property (DEVPROP_TYPE_GUID).
    public static Guid? TryReadGuidProperty(uint devInst, DEVPROPKEY key)
    {
        uint size = 0;
        int cr = CM_Get_DevNode_PropertyW(devInst, ref key, out uint propType, propertyBuffer: null, ref size,
            ulFlags: 0);
        if (cr is not CR_BUFFER_SMALL and not CR_SUCCESS) return null;
        if (propType != DEVPROP_TYPE_GUID || size != 16) return null;

        byte[] buf = new byte[16];
        cr = CM_Get_DevNode_PropertyW(devInst, ref key, out propType, buf, ref size, ulFlags: 0);
        if (cr != CR_SUCCESS) return null;

        return new Guid(buf);
    }

    // CM_Get_DevNode_Property: read a null-terminated UTF-16 string property (DEVPROP_TYPE_STRING).
    // Returns null for a missing, mistyped, or empty value.
    public static string? TryReadStringProperty(uint devInst, DEVPROPKEY key)
    {
        uint size = 0;
        int cr = CM_Get_DevNode_PropertyW(devInst, ref key, out uint propType, propertyBuffer: null, ref size,
            ulFlags: 0);
        if (cr is not CR_BUFFER_SMALL and not CR_SUCCESS) return null;
        if (propType != DEVPROP_TYPE_STRING || size < sizeof(char)) return null;

        byte[] buf = new byte[size];
        cr = CM_Get_DevNode_PropertyW(devInst, ref key, out propType, buf, ref size, ulFlags: 0);
        if (cr != CR_SUCCESS) return null;

        string value = Encoding.Unicode.GetString(buf, index: 0, (int)size).TrimEnd('\0');
        return value.Length == 0 ? null : value;
    }
}
