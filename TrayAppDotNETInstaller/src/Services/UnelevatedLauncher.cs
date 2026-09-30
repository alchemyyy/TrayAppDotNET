using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TrayAppDotNETInstaller.Services;

/// <summary>
/// Starts a program unelevated from an elevated installer, with its arguments intact. The desktop's Explorer runs
/// with the signed-in user's unelevated token, so the program is launched by Explorer rather than by this process.
/// Unlike "explorer.exe &lt;path&gt;", this passes arguments. The chain is the one TrayAppDotNETCommon's
/// ExplorerProcessLauncher walks: the desktop window from ShellWindows, its shell browser, the active view's
/// background object, and IShellDispatch2.ShellExecute. The .NET Framework marshals COM itself, so the interfaces
/// are declared rather than called through raw vtables.
/// </summary>
internal static class UnelevatedLauncher
{
    // CSIDL_DESKTOP, SWC_DESKTOP, SWFO_NEEDDISPATCH, SVGIO_BACKGROUND and SW_SHOWNORMAL
    private const int DesktopFolderID = 0;
    private const int DesktopWindowClass = 8;
    private const int FindNeedsDispatch = 1;
    private const uint ViewBackgroundItem = 0;
    private const int ShowNormal = 1;

    // ShellExecute reports a failure as an instance handle value of 32 or less, the way the Win32 function does
    private const int LastShellExecuteErrorCode = 32;

    private const string ApplicationPropertyName = "Application";
    private const string ShellExecuteMethodName = "ShellExecute";

    private static readonly Guid ShellWindowsClassID = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid TopLevelBrowserServiceID = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid DispatchInterfaceID = new("00020400-0000-0000-C000-000000000046");

    /// <summary>
    /// Asks the desktop Explorer to start <paramref name="executablePath"/> with <paramref name="arguments"/>, an
    /// already quoted command line. Never throws; <paramref name="failure"/> explains a false result.
    /// </summary>
    public static bool TryShellExecute(
        string executablePath,
        string arguments,
        string workingDirectory,
        out string? failure)
    {
        object? shellWindows = null;
        object? desktopWindow = null;
        object? shellBrowser = null;
        IShellView? shellView = null;
        object? folderView = null;
        object? application = null;
        try
        {
            Type? shellWindowsType = Type.GetTypeFromCLSID(ShellWindowsClassID, throwOnError: false);
            if (shellWindowsType == null)
            {
                failure = "the ShellWindows class is not registered";
                return false;
            }

            shellWindows = Activator.CreateInstance(shellWindowsType);
            if (shellWindows is not IShellWindows shellWindowList)
            {
                failure = "the ShellWindows object is not a window list";
                return false;
            }

            object? location = DesktopFolderID;
            object? locationRoot = null;
            desktopWindow = shellWindowList.FindWindowSW(
                ref location,
                ref locationRoot,
                DesktopWindowClass,
                out int _,
                FindNeedsDispatch);
            if (desktopWindow is not IComServiceProvider serviceProvider)
            {
                failure = "the desktop window was not found";
                return false;
            }

            Guid serviceID = TopLevelBrowserServiceID;
            Guid browserInterfaceID = typeof(IShellBrowser).GUID;
            shellBrowser = serviceProvider.QueryService(ref serviceID, ref browserInterfaceID);
            if (shellBrowser is not IShellBrowser browser)
            {
                failure = "the desktop window has no shell browser";
                return false;
            }

            shellView = browser.QueryActiveShellView();
            Guid dispatchInterfaceID = DispatchInterfaceID;
            folderView = shellView.GetItemObject(ViewBackgroundItem, ref dispatchInterfaceID);
            application = folderView.GetType().InvokeMember(
                ApplicationPropertyName,
                BindingFlags.GetProperty,
                binder: null,
                folderView,
                args: null,
                CultureInfo.InvariantCulture);
            if (application == null)
            {
                failure = "the desktop view has no shell application object";
                return false;
            }

            // IShellDispatch2.ShellExecute(File, vArgs, vDir, vOperation, vShow); an empty operation is the default verb
            object? result = application.GetType().InvokeMember(
                ShellExecuteMethodName,
                BindingFlags.InvokeMethod,
                binder: null,
                application,
                [executablePath, arguments, workingDirectory, string.Empty, ShowNormal],
                CultureInfo.InvariantCulture);
            if (result is int errorCode && errorCode is > 0 and <= LastShellExecuteErrorCode)
            {
                failure = $"ShellExecute failed with {errorCode}";
                return false;
            }

            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            // Explorer may be missing, restarting or replaced by another shell; the caller has a fallback
            Exception cause = exception is TargetInvocationException { InnerException: not null } invocation
                ? invocation.InnerException
                : exception;
            failure = $"{cause.GetType().Name}: {cause.Message}";
            return false;
        }
        finally
        {
            Release(application);
            Release(folderView);
            Release(shellView);
            Release(shellBrowser);
            Release(desktopWindow);
            Release(shellWindows);
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject == null || !Marshal.IsComObject(comObject)) return;

        Marshal.ReleaseComObject(comObject);
    }

    /// <summary>IShellWindows from exdisp.idl, a dual interface.</summary>
    [ComImport]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        // The members ahead of FindWindowSW are declared only so it lands in its vtable slot; none is called
        int Count { get; }

        void Item();

        void NewEnumeration();

        void Register();

        void RegisterPending();

        void Revoke();

        void OnNavigate();

        void OnActivated();

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(
            ref object? location,
            ref object? locationRoot,
            int windowClass,
            out int windowHandle,
            int options);
    }

    /// <summary>The COM IServiceProvider, named apart from System.IServiceProvider.</summary>
    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IComServiceProvider
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object QueryService(ref Guid serviceID, ref Guid interfaceID);
    }

    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        // IOleWindow, then the IShellBrowser members ahead of QueryActiveShellView, declared only to hold their
        // vtable slots; none is called
        void GetWindow();

        void ContextSensitiveHelp();

        void InsertMenusSB();

        void SetMenuSB();

        void RemoveMenusSB();

        void SetStatusTextSB();

        void EnableModelessSB();

        void TranslateAcceleratorSB();

        void BrowseObject();

        void GetViewStateStream();

        void GetControlWindow();

        void SendControlMsg();

        IShellView QueryActiveShellView();
    }

    [ComImport]
    [Guid("000214E3-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        // IOleWindow, then the IShellView members ahead of GetItemObject, declared only to hold their vtable
        // slots; none is called
        void GetWindow();

        void ContextSensitiveHelp();

        void TranslateAccelerator();

        void EnableModeless();

        void UIActivate();

        void Refresh();

        void CreateViewWindow();

        void DestroyViewWindow();

        void GetCurrentInfo();

        void AddPropertySheetPages();

        void SaveViewState();

        void SelectItem();

        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetItemObject(uint item, ref Guid interfaceID);
    }
}
