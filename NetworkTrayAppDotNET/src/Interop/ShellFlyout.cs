using System.Runtime.InteropServices;

namespace NetworkTrayAppDotNET.Interop;

/// <summary>
/// Invokes the undocumented Windows shell flyouts (Win10 NetworkFlyoutExperienceManager,
/// Win11 ControlCenterExperienceManager) via raw COM. These COM contracts are stable across
/// recent Windows builds but unsupported, so the call paths are guarded with try/catch and
/// callers should fall back to ms-* URIs on failure.
/// </summary>
internal static unsafe partial class ShellFlyout
{
    private static readonly Guid CLSID_ImmersiveShell = new("c2f03a33-21f5-47fa-b4bb-156362a2f239");
    private static readonly Guid IID_IServiceProvider = new("6d5140c1-7436-11ce-8034-00aa006009fa");
    private static readonly Guid SID_ShellExperienceManagerFactory = new("2e8fcb18-a0ee-41ad-8ef8-77fb3a370ca5");
    private static readonly Guid IID_NetworkFlyoutExperienceManager = new("c9ddc674-b44b-4c67-9d79-2b237d9be05a");
    private static readonly Guid IID_ControlCenterExperienceManager = new("d669a58e-6b18-4d1d-9004-a8862adb0a20");

    private const int CLSCTX_LOCAL_SERVER = 4;

    // 'in' / 'out' Guid parameters need DllImport - LibraryImport doesn't marshal them the same way.
    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        in Guid rclsid,
        IntPtr pUnkOuter,
        int dwClsContext,
        in Guid riid,
        out IntPtr ppv);

    [LibraryImport("combase.dll", EntryPoint = "WindowsCreateString", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(IntPtr hstring);

    public static bool ShowNetworkFlyoutWin10() =>
        ShowFlyoutCOM("Windows.Internal.ShellExperience.NetworkFlyout", IID_NetworkFlyoutExperienceManager);

    public static bool ShowControlCenter() =>
        ShowFlyoutCOM("Windows.Internal.ShellExperience.ControlCenter", IID_ControlCenterExperienceManager);

    private ref struct ComHandles
    {
        public IntPtr ServiceProvider;
        public IntPtr Factory;
        public IntPtr ExperienceManager;
        public IntPtr Flyout;
        public IntPtr HString;

        public void Dispose()
        {
            if (HString != IntPtr.Zero) _ = WindowsDeleteString(HString);
            Release(Flyout);
            Release(ExperienceManager);
            Release(Factory);
            Release(ServiceProvider);
        }
    }

    private static bool TryAcquireFlyoutInterfaces(string experienceName, Guid experienceIID, ref ComHandles handles)
    {
        int hr = CoCreateInstance(CLSID_ImmersiveShell, IntPtr.Zero, CLSCTX_LOCAL_SERVER,
            IID_IServiceProvider, out handles.ServiceProvider);
        if (hr < 0 || handles.ServiceProvider == IntPtr.Zero) return false;

        if (!TryQueryService(
                handles.ServiceProvider,
                SID_ShellExperienceManagerFactory,
                SID_ShellExperienceManagerFactory,
                out handles.Factory))
            return false;

        hr = WindowsCreateString(experienceName, experienceName.Length, out handles.HString);
        if (hr < 0 || handles.HString == IntPtr.Zero) return false;

        IntPtr* vtable = *(IntPtr**)handles.Factory;
        delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int> getExperienceManager =
            (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)vtable[VTABLE_GET_EXPERIENCE_MANAGER];
        IntPtr experienceManager = IntPtr.Zero;
        hr = getExperienceManager(handles.Factory, handles.HString, &experienceManager);
        handles.ExperienceManager = experienceManager;
        if (hr < 0 || handles.ExperienceManager == IntPtr.Zero) return false;

        return TryQueryInterface(handles.ExperienceManager, experienceIID, out handles.Flyout);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WFRect
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;
    }

    private const int VTABLE_QUERY_INTERFACE = 0;
    private const int VTABLE_RELEASE = 2;
    private const int VTABLE_QUERY_SERVICE = 3;
    private const int VTABLE_SHOW_FLYOUT = 6;
    private const int VTABLE_GET_EXPERIENCE_MANAGER = 6;

    private static bool TryQueryInterface(IntPtr unknown, Guid iid, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (unknown == IntPtr.Zero) return false;

        IntPtr* vtable = *(IntPtr**)unknown;
        delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int> queryInterface =
            (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)vtable[VTABLE_QUERY_INTERFACE];

        Guid iidLocal = iid;
        IntPtr localResult = IntPtr.Zero;
        int hr = queryInterface(unknown, &iidLocal, &localResult);
        result = localResult;
        return hr >= 0 && result != IntPtr.Zero;
    }

    private static bool TryQueryService(IntPtr serviceProvider, Guid service, Guid iid, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (serviceProvider == IntPtr.Zero) return false;

        IntPtr* vtable = *(IntPtr**)serviceProvider;
        delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, IntPtr*, int> queryService =
            (delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, IntPtr*, int>)vtable[VTABLE_QUERY_SERVICE];

        Guid serviceLocal = service;
        Guid iidLocal = iid;
        IntPtr localResult = IntPtr.Zero;
        int hr = queryService(serviceProvider, &serviceLocal, &iidLocal, &localResult);
        result = localResult;
        return hr >= 0 && result != IntPtr.Zero;
    }

    private static void Release(IntPtr unknown)
    {
        if (unknown == IntPtr.Zero) return;

        IntPtr* vtable = *(IntPtr**)unknown;
        delegate* unmanaged[Stdcall]<IntPtr, uint> release =
            (delegate* unmanaged[Stdcall]<IntPtr, uint>)vtable[VTABLE_RELEASE];
        _ = release(unknown);
    }

    private static bool ShowFlyoutCOM(string experienceName, Guid experienceIID)
    {
        ComHandles handles = new();
        try
        {
            if (!TryAcquireFlyoutInterfaces(experienceName, experienceIID, ref handles)) return false;

            IntPtr* flyoutVtable = *(IntPtr**)handles.Flyout;
            delegate* unmanaged[Stdcall]<IntPtr, WFRect*, int> showFlyout =
                (delegate* unmanaged[Stdcall]<IntPtr, WFRect*, int>)flyoutVtable[VTABLE_SHOW_FLYOUT];
            WFRect rect = default;
            int hr = showFlyout(handles.Flyout, &rect);

            return hr >= 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            handles.Dispose();
        }
    }
}
