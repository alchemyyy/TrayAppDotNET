using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using BrightnessTrayAppDotNET.Interop.WindowsBrightness;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Utils;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class COMActivationTests
{
    [Fact]
    public void UniqueGeneratedCOMWrapperSupportsDeterministicRelease()
    {
        int initializationResult = WmiNative.CoInitializeEx(IntPtr.Zero, WmiNative.CoinitMultithreaded);
        bool uninitialize = initializationResult is WmiNative.SOk or WmiNative.SFalse;
        if (initializationResult < 0 && initializationResult != WmiNative.RpcEChangedMode)
            Marshal.ThrowExceptionForHR(initializationResult);

        IWbemLocator? locator = null;
        try
        {
            locator = COMActivation.CreateInstance<IWbemLocator>(
                WmiNative.ClsidWbemLocator,
                typeof(IWbemLocator).GUID);

            Assert.IsAssignableFrom<ComObject>((object)locator);
            Assert.True(COMActivation.TryReleaseGeneratedComObject(locator));
            locator = null;
        }
        finally
        {
            Safe.Release(locator);
            if (uninitialize) WmiNative.CoUninitialize();
        }
    }
}
