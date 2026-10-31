using System.Runtime.InteropServices;
using TrayAppDotNETCommon.Interop;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class PropertyStoreLayoutTests
{
    [Fact]
    public void PropVariantHasTheNativeX64Size()
    {
        // GetValue and PropVariantClear write the full native PROPVARIANT; a shorter struct is a stack overwrite
        Assert.Equal(expected: 24, Marshal.SizeOf<PROPVARIANT>());
        Assert.Equal(expected: 8, Marshal.OffsetOf<PROPVARIANT>(nameof(PROPVARIANT.p1)).ToInt32());
    }

    [Fact]
    public void PropertyKeyHasTheNativeSize() =>
        Assert.Equal(expected: 20, Marshal.SizeOf<PROPERTYKEY>());

    [Fact]
    public void InterfaceIDIsTheSDKValue()
    {
        Guid sdkValue = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
        Assert.Equal(sdkValue, PropertyStore.InterfaceID);
        Assert.Equal(sdkValue, typeof(IPropertyStore).GUID);
    }

    [Fact]
    public void AppUserModelIDKeyIsPKEYAppUserModelID()
    {
        Assert.Equal(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyStore.AppUserModelIDKey.fmtid);
        Assert.Equal(expected: 5u, PropertyStore.AppUserModelIDKey.pid);
    }
}
