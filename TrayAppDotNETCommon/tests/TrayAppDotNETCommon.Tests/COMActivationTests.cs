using System.Runtime.InteropServices.Marshalling;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Interop.Wmi;
using TrayAppDotNETCommon.Utils;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class COMActivationTests
{
    [Fact]
    public void UniqueGeneratedCOMWrapperSupportsDeterministicRelease()
    {
        using ComApartmentScope apartment = ComApartmentScope.Enter(Ole32.COINIT_MULTITHREADED);
        Assert.True(apartment.IsUsable);
        IWbemLocator? locator = null;
        try
        {
            locator = COMActivation.CreateInstance<IWbemLocator>(
                WmiNative.ClsidWbemLocator,
                typeof(IWbemLocator).GUID);

            Assert.IsAssignableFrom<ComObject>(locator);
            Assert.True(COMActivation.TryReleaseGeneratedComObject(locator));
            locator = null;
        }
        finally
        {
            Safe.Release(locator);
        }
    }
}
