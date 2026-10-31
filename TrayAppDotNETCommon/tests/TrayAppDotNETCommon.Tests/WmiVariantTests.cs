using System.Runtime.InteropServices;
using TrayAppDotNETCommon.Interop.Wmi;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class WmiVariantTests
{
    [Fact]
    public void VariantHasTheNativeX64Size() =>
        Assert.Equal(expected: 24, Marshal.SizeOf<WmiVariant>());

    [Fact]
    public void NegativeSignedValuesAreNeverSignExtended()
    {
        WmiVariant i4 = new() { Vt = WmiVariant.VtI4, LVal = -1 };
        WmiVariant integer = new() { Vt = WmiVariant.VtInt, LVal = -5 };
        WmiVariant i8 = new() { Vt = WmiVariant.VtI8, LlVal = -2 };

        Assert.False(i4.TryGetUInt64(out _));
        Assert.False(integer.TryGetUInt64(out _));
        Assert.False(i8.TryGetUInt64(out _));
    }

    [Fact]
    public void UnsignedAndWideIntegersDecode()
    {
        Assert.True(new WmiVariant { Vt = WmiVariant.VtUi4, UlVal = 4_000_000_000 }.TryGetUInt64(out ulong ui4));
        Assert.Equal(expected: 4_000_000_000UL, ui4);
        Assert.True(new WmiVariant { Vt = WmiVariant.VtUi8, UllVal = ulong.MaxValue }.TryGetUInt64(out ulong ui8));
        Assert.Equal(ulong.MaxValue, ui8);
        Assert.True(new WmiVariant { Vt = WmiVariant.VtI4, LVal = 42 }.TryGetUInt32(out uint i4));
        Assert.Equal(expected: 42u, i4);
        Assert.False(new WmiVariant { Vt = WmiVariant.VtUi8, UllVal = ulong.MaxValue }.TryGetUInt32(out _));
    }

    [Fact]
    public void NumericStringsDecodeAsWmiUInt64()
    {
        IntPtr bstr = Marshal.StringToBSTR("17179869184");
        try
        {
            WmiVariant variant = new() { Vt = WmiVariant.VtBStr, BstrVal = bstr };
            Assert.True(variant.TryGetUInt64(out ulong value));
            Assert.Equal(expected: 17_179_869_184UL, value);
            Assert.True(variant.TryGetString(out string text));
            Assert.Equal(expected: "17179869184", text);
        }
        finally
        {
            Marshal.FreeBSTR(bstr);
        }
    }

    [Fact]
    public void ArraysAreRejected()
    {
        WmiVariant array = new() { Vt = WmiVariant.VtBStr | WmiVariant.VtArray, BstrVal = new IntPtr(1) };
        Assert.False(array.TryGetString(out _));
        Assert.False(new WmiVariant { Vt = WmiVariant.VtUi4 | WmiVariant.VtArray }.TryGetUInt64(out _));
    }

    [Fact]
    public void BooleansDecode()
    {
        Assert.True(new WmiVariant { Vt = WmiVariant.VtBool, BoolVal = -1 }.TryGetBoolean(out bool value));
        Assert.True(value);
    }

    [Fact]
    public void EnumerationStepsAreBoundedByTheTimeoutResult()
    {
        Assert.Equal(WmiEnumerationStep.Object, WmiConnection.ClassifyNext(hr: 0, returned: 1, new IntPtr(1)));
        Assert.Equal(WmiEnumerationStep.End, WmiConnection.ClassifyNext(hr: 1, returned: 0, IntPtr.Zero));
        Assert.Equal(
            WmiEnumerationStep.TimedOut,
            WmiConnection.ClassifyNext(WmiNative.WbemSTimedOut, returned: 0, IntPtr.Zero));
        Assert.Equal(
            WmiEnumerationStep.Failed,
            WmiConnection.ClassifyNext(unchecked((int)0x80041003), returned: 0, IntPtr.Zero));
    }
}
