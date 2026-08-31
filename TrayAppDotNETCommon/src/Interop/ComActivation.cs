using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace TrayAppDotNETCommon.Interop;

public static class COMActivation
{
    private static readonly StrategyBasedComWrappers ComWrappers = new();
    private static int _registeredForMarshalling;

    public const uint ClsCtxInprocServer = 0x1;
    public const uint ClsCtxInprocHandler = 0x2;
    public const uint ClsCtxLocalServer = 0x4;
    public const uint ClsCtxRemoteServer = 0x10;

    public const uint ClsCtxAll =
        ClsCtxInprocServer | ClsCtxInprocHandler | ClsCtxLocalServer | ClsCtxRemoteServer;

    public static T CreateInstance<T>(Guid clsid, Guid iid, uint clsContext = ClsCtxAll)
        where T : class
    {
        EnsureRegisteredForMarshalling();

        int hr = CoCreateInstance(in clsid, IntPtr.Zero, clsContext, in iid, out IntPtr instance);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);

        return GetObjectForComInstance<T>(instance, releaseInputReference: true);
    }

    public static T GetObjectForComInstance<T>(IntPtr instance, bool releaseInputReference = false)
        where T : class
    {
        EnsureRegisteredForMarshalling();

        unsafe
        {
            void* unmanaged = (void*)instance;
            T? managed;
            try
            {
                managed = UniqueComInterfaceMarshaller<T>.ConvertToManaged(unmanaged);
            }
            finally
            {
                if (releaseInputReference) UniqueComInterfaceMarshaller<T>.Free(unmanaged);
            }

            return managed ??
                   throw new InvalidOperationException($"Failed to marshal COM interface {typeof(T).FullName}.");
        }
    }

    public static void EnsureRegisteredForMarshalling()
    {
        if (Interlocked.Exchange(ref _registeredForMarshalling, value: 1) != 0) return;

        try
        {
            System.Runtime.InteropServices.ComWrappers.RegisterForMarshalling(ComWrappers);
        }
        catch (InvalidOperationException)
        {
            // Another component registered a process-wide marshaller first. Direct calls through
            // this helper still use the local StrategyBasedComWrappers instance.
        }
    }

    public static bool TryReleaseGeneratedComObject(object? obj)
    {
        if (obj is not ComObject comObject) return false;

        // GetObjectForComInstance uses UniqueComInterfaceMarshaller, so its ComObject wrappers are
        // UniqueInstance RCWs and support deterministic release through FinalRelease.
        comObject.FinalRelease();
        return true;
    }

    [DllImport("ole32.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int CoCreateInstance(
        in Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        out IntPtr ppv);
}
