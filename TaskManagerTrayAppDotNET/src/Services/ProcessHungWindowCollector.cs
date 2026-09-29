using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Finds processes that own a hung user-interactive window, using Task Manager's window rules.</summary>
/// <remarks>
/// Mirrors Taskmgr.exe 10.0.26100: WdcWindowMonitor::ShouldIncludeDesktopWindow selects the windows,
/// IsHungAppWindow decides the hang, and a ghost window is charged to the process that owns its hung window.
/// </remarks>
internal sealed class ProcessHungWindowCollector
{
    private const int WindowExtendedStyle = -20;
    private const uint GetWindowOwner = 4;
    private const long WindowStyleExtendedToolWindow = 0x00000080L;
    private const long WindowStyleExtendedApplicationWindow = 0x00040000L;
    private const long WindowStyleExtendedNoActivate = 0x08000000L;
    private const int ClassNameCapacity = 256;
    private const string TaskbarWindowClassName = "Shell_TrayWnd";

    private readonly NativeMethods.EnumWindowsCallback _enumerateWindowCallback;
    private readonly HashSet<int> _notRespondingProcessIDs = new(16);

    public ProcessHungWindowCollector()
    {
        _enumerateWindowCallback = OnEnumerateWindow;
    }

    /// <summary>Replaces the hung-process set with one enumeration of the desktop's top-level windows.</summary>
    public void Capture()
    {
        _notRespondingProcessIDs.Clear();
        _ = NativeMethods.EnumWindows(_enumerateWindowCallback, IntPtr.Zero);
    }

    /// <summary>Returns whether the latest capture found a hung window owned by the process.</summary>
    public bool IsNotResponding(int processID) => _notRespondingProcessIDs.Contains(processID);

    /// <summary>Applies Task Manager's user-interactive rule to a visible top-level window.</summary>
    internal static bool IsUserInteractiveWindow(
        long extendedStyle,
        bool hasVisibleOwner,
        bool isTaskbar,
        bool hasGhostWindow)
    {
        // WS_EX_APPWINDOW overrides both the tool-window style and an owning window
        bool isApplicationWindow = (extendedStyle & WindowStyleExtendedApplicationWindow) != 0;
        if (!isApplicationWindow
            && (extendedStyle & (WindowStyleExtendedToolWindow | WindowStyleExtendedNoActivate)) != 0)
            return false;
        if (!isApplicationWindow && hasVisibleOwner) return false;

        // A ghosted window is represented by its ghost, which the enumeration reaches separately
        return !isTaskbar && !hasGhostWindow;
    }

    private bool OnEnumerateWindow(IntPtr windowHandle, IntPtr parameter)
    {
        _ = parameter;
        if (!NativeMethods.IsWindowVisible(windowHandle)) return true;

        long extendedStyle = NativeMethods.GetWindowLongPtr(windowHandle, WindowExtendedStyle).ToInt64();
        IntPtr ownerWindowHandle = NativeMethods.GetWindow(windowHandle, GetWindowOwner);
        bool hasVisibleOwner = ownerWindowHandle != IntPtr.Zero
                               && NativeMethods.IsWindowVisible(ownerWindowHandle);
        if (!IsUserInteractiveWindow(
                extendedStyle,
                hasVisibleOwner,
                IsTaskbar(windowHandle),
                NativeMethods.GhostWindowFromHungWindow(windowHandle) != IntPtr.Zero))
            return true;
        if (!NativeMethods.IsHungAppWindow(windowHandle)) return true;

        IntPtr hungWindowHandle = NativeMethods.HungWindowFromGhostWindow(windowHandle);
        IntPtr processWindowHandle = hungWindowHandle != IntPtr.Zero ? hungWindowHandle : windowHandle;
        _ = NativeMethods.GetWindowThreadProcessId(processWindowHandle, out uint processID);
        if (processID is > 0 and <= int.MaxValue)
            _notRespondingProcessIDs.Add((int)processID);
        return true;
    }

    private static unsafe bool IsTaskbar(IntPtr windowHandle)
    {
        char* className = stackalloc char[ClassNameCapacity];
        int length = NativeMethods.GetClassName(windowHandle, className, ClassNameCapacity);
        return length > 0
               && new ReadOnlySpan<char>(className, length).Equals(
                   TaskbarWindowClassName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static class NativeMethods
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr windowHandle);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

        [DllImport("user32.dll", EntryPoint = "GetClassNameW")]
        public static extern unsafe int GetClassName(IntPtr windowHandle, char* className, int maxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsHungAppWindow(IntPtr windowHandle);

        // Exported by name from user32 but absent from the SDK headers; Taskmgr.exe imports both
        [DllImport("user32.dll")]
        public static extern IntPtr GhostWindowFromHungWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern IntPtr HungWindowFromGhostWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processID);
    }
}
