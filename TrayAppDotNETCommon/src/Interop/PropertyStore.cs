using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace TrayAppDotNETCommon.Interop;

/// <summary>A shell/COM property key (PROPERTYKEY): format ID plus property ID.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct PROPERTYKEY(Guid fmtid, uint pid)
{
    public Guid fmtid = fmtid;
    public uint pid = pid;
}

/// <summary>
/// PROPVARIANT with the native x64 layout: an 8-byte header (type plus three reserved words) and a 16-byte union, 24
/// bytes in total. Two pointer-size fields after the header cover the union: VT_LPWSTR, VT_UI4, VT_BOOL and VT_CLSID
/// live in <see cref="p1"/>; VT_BLOB keeps cbSize in the low 32 bits of <see cref="p1"/> and the data pointer in
/// <see cref="p2"/>. Every PROPVARIANT a native call fills must be released with
/// <see cref="PropertyStore.PropVariantClear"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PROPVARIANT
{
    public ushort vt;
    public ushort wReserved1;
    public ushort wReserved2;
    public ushort wReserved3;
    public IntPtr p1;
    public IntPtr p2;

    public const ushort VT_EMPTY = 0;
    public const ushort VT_I4 = 3;
    public const ushort VT_BOOL = 11;
    public const ushort VT_UI4 = 19;
    public const ushort VT_LPWSTR = 31;
    public const ushort VT_BLOB = 65;
    public const ushort VT_CLSID = 72;

    public readonly string? GetString() => vt == VT_LPWSTR && p1 != IntPtr.Zero ? Marshal.PtrToStringUni(p1) : null;

    public readonly uint GetUInt32() => vt == VT_UI4 ? (uint)p1.ToInt64() : 0u;

    /// <summary>VT_CLSID: <see cref="p1"/> points at a 16-byte GUID. Null on a type mismatch or null pointer.</summary>
    public readonly Guid? GetGuid()
    {
        if (vt != VT_CLSID || p1 == IntPtr.Zero) return null;
        byte[] buffer = new byte[16];
        Marshal.Copy(p1, buffer, startIndex: 0, length: 16);
        return new Guid(buffer);
    }

    /// <summary>VT_BLOB payload bytes, or null on a type mismatch or an empty payload.</summary>
    public readonly byte[]? GetBlobBytes()
    {
        if (vt != VT_BLOB) return null;
        int size = (int)p1.ToInt64();
        if (size <= 0 || p2 == IntPtr.Zero) return null;
        byte[] buffer = new byte[size];
        Marshal.Copy(p2, buffer, startIndex: 0, size);
        return buffer;
    }
}

/// <summary>
/// IPropertyStore. The store is always addressed by a known <see cref="PROPERTYKEY"/>, so GetCount and GetAt are
/// vtable padding.
/// </summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid(PropertyStore.InterfaceIDString)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IPropertyStore
{
    void Unused_GetCount();
    void Unused_GetAt();
    void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
    void SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
    void Commit();
}

/// <summary>Shared IPropertyStore constants and helpers for callers that hold a raw interface pointer.</summary>
public static class PropertyStore
{
    public const string InterfaceIDString = "886d8eeb-8cf2-4446-8d02-cdba1dbdcf99";

    /// <summary>IID_IPropertyStore.</summary>
    public static readonly Guid InterfaceID = new(InterfaceIDString);

    /// <summary>PKEY_AppUserModel_ID.</summary>
    public static readonly PROPERTYKEY AppUserModelIDKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid: 5);

    private const int GetValueSlot = 5;
    private const int ReleaseSlot = 2;

    /// <summary>Frees whatever a PROPVARIANT owns (for example the LPWSTR returned by GetValue) and empties it.</summary>
    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PROPVARIANT pvar);

    /// <summary>Reads a VT_LPWSTR value through a raw IPropertyStore pointer; null when absent or of another type.</summary>
    public static unsafe string? TryGetString(IntPtr propertyStore, PROPERTYKEY key)
    {
        if (propertyStore == IntPtr.Zero) return null;

        void** virtualTable = *(void***)propertyStore;
        delegate* unmanaged[Stdcall]<IntPtr, PROPERTYKEY*, PROPVARIANT*, int> getValue =
            (delegate* unmanaged[Stdcall]<IntPtr, PROPERTYKEY*, PROPVARIANT*, int>)virtualTable[GetValueSlot];
        PROPVARIANT value = default;
        try
        {
            int result = getValue(propertyStore, &key, &value);
            return result >= 0 ? value.GetString() : null;
        }
        finally
        {
            _ = PropVariantClear(ref value);
        }
    }

    /// <summary>Releases a raw IPropertyStore pointer.</summary>
    public static unsafe void Release(IntPtr propertyStore)
    {
        if (propertyStore == IntPtr.Zero) return;
        void** virtualTable = *(void***)propertyStore;
        delegate* unmanaged[Stdcall]<IntPtr, uint> release =
            (delegate* unmanaged[Stdcall]<IntPtr, uint>)virtualTable[ReleaseSlot];
        _ = release(propertyStore);
    }
}
