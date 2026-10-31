using System.Globalization;
using System.Runtime.InteropServices;

namespace TrayAppDotNETCommon.Interop.Wmi;

/// <summary>
/// A VARIANT with the native x64 size (24 bytes). WMI's IWbemClassObject.Get fills one; release it with
/// <see cref="WmiNative.VariantClear"/>. The decoders never sign-extend a negative signed value into an unsigned
/// result and reject arrays.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct WmiVariant
{
    public const ushort VtTypeMask = 0x0FFF;
    public const ushort VtArray = 0x2000;
    public const ushort VtI2 = 2;
    public const ushort VtI4 = 3;
    public const ushort VtBStr = 8;
    public const ushort VtBool = 11;
    public const ushort VtI1 = 16;
    public const ushort VtUi1 = 17;
    public const ushort VtUi2 = 18;
    public const ushort VtUi4 = 19;
    public const ushort VtI8 = 20;
    public const ushort VtUi8 = 21;
    public const ushort VtInt = 22;
    public const ushort VtUint = 23;

    [FieldOffset(0)] public ushort Vt;
    [FieldOffset(8)] public sbyte CVal;
    [FieldOffset(8)] public byte BVal;
    [FieldOffset(8)] public short IVal;
    [FieldOffset(8)] public ushort UiVal;
    [FieldOffset(8)] public int LVal;
    [FieldOffset(8)] public uint UlVal;
    [FieldOffset(8)] public long LlVal;
    [FieldOffset(8)] public ulong UllVal;
    [FieldOffset(8)] public short BoolVal;
    [FieldOffset(8)] public IntPtr BstrVal;

    private readonly bool IsArray => (Vt & VtArray) != 0;

    private readonly ushort ElementType => (ushort)(Vt & VtTypeMask);

    /// <summary>Reads a non-array BSTR.</summary>
    public readonly bool TryGetString(out string value)
    {
        value = string.Empty;
        if (IsArray || ElementType != VtBStr || BstrVal == IntPtr.Zero) return false;
        value = Marshal.PtrToStringBSTR(BstrVal);
        return true;
    }

    /// <summary>Reads any non-negative integer type, or a BSTR holding a decimal number (WMI's uint64 form).</summary>
    public readonly bool TryGetUInt64(out ulong value)
    {
        value = 0;
        if (IsArray) return false;
        switch (ElementType)
        {
            case VtUi1:
                value = BVal;
                return true;
            case VtUi2:
                value = UiVal;
                return true;
            case VtUi4:
            case VtUint:
                value = UlVal;
                return true;
            case VtUi8:
                value = UllVal;
                return true;
            case VtI1 when CVal >= 0:
                value = (ulong)CVal;
                return true;
            case VtI2 when IVal >= 0:
                value = (ulong)IVal;
                return true;
            case VtI4 when LVal >= 0:
            case VtInt when LVal >= 0:
                value = (ulong)LVal;
                return true;
            case VtI8 when LlVal >= 0:
                value = (ulong)LlVal;
                return true;
            case VtBStr when BstrVal != IntPtr.Zero:
                return ulong.TryParse(
                    Marshal.PtrToStringBSTR(BstrVal),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value);
            default:
                return false;
        }
    }

    /// <summary>Reads a non-negative integer that fits 32 bits.</summary>
    public readonly bool TryGetUInt32(out uint value)
    {
        value = 0;
        if (!TryGetUInt64(out ulong wide) || wide > uint.MaxValue) return false;
        value = (uint)wide;
        return true;
    }

    /// <summary>Reads a VT_BOOL (VARIANT_TRUE is -1).</summary>
    public readonly bool TryGetBoolean(out bool value)
    {
        value = false;
        if (IsArray || ElementType != VtBool) return false;
        value = BoolVal != 0;
        return true;
    }
}
