namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Finds processes that own a hung user-interactive window, using Task Manager's window rules.</summary>
/// <remarks>
/// Mirrors Taskmgr.exe 10.0.26100: WdcWindowMonitor::ShouldIncludeDesktopWindow selects the windows,
/// IsHungAppWindow decides the hang, and a ghost window is charged to the process that owns its hung window.
/// </remarks>
internal sealed class ProcessHungWindowCollector
{
    private const int WindowExtendedStyle = -20;
    private const uint GetWindowOwner = User32.GW_OWNER;
    private const long WindowStyleExtendedToolWindow = 0x00000080L;
    private const long WindowStyleExtendedApplicationWindow = 0x00040000L;
    private const long WindowStyleExtendedNoActivate = 0x08000000L;
    private const int ClassNameCapacity = 256;

    private readonly User32.EnumWindowsProc _enumerateWindowCallback;
    private readonly HashSet<int> _notRespondingProcessIDs = new(16);
    private bool _callbackFailureLogged;

    public ProcessHungWindowCollector() => _enumerateWindowCallback = OnEnumerateWindow;

    /// <summary>Replaces the hung-process set with one enumeration of the desktop's top-level windows.</summary>
    public void Capture()
    {
        _notRespondingProcessIDs.Clear();
        _ = User32.EnumWindows(_enumerateWindowCallback, IntPtr.Zero);
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
        // An exception must never unwind into user32: under Native AOT that terminates the process
        try
        {
            CollectWindow(windowHandle);
        }
        catch (Exception exception)
        {
            if (!_callbackFailureLogged)
            {
                _callbackFailureLogged = true;
                TADNLog.Log($"ProcessHungWindowCollector: window enumeration callback failed: {exception}");
            }
        }

        return true;
    }

    private void CollectWindow(IntPtr windowHandle)
    {
        if (!User32.IsWindowVisible(windowHandle)) return;

        long extendedStyle = User32.GetWindowLongPtr(windowHandle, WindowExtendedStyle).ToInt64();
        IntPtr ownerWindowHandle = User32.GetWindow(windowHandle, GetWindowOwner);
        bool hasVisibleOwner = ownerWindowHandle != IntPtr.Zero
                               && User32.IsWindowVisible(ownerWindowHandle);
        if (!IsUserInteractiveWindow(
                extendedStyle,
                hasVisibleOwner,
                IsTaskbar(windowHandle),
                User32.GhostWindowFromHungWindow(windowHandle) != IntPtr.Zero))
            return;
        if (!User32.IsHungAppWindow(windowHandle)) return;

        IntPtr hungWindowHandle = User32.HungWindowFromGhostWindow(windowHandle);
        IntPtr processWindowHandle = hungWindowHandle != IntPtr.Zero ? hungWindowHandle : windowHandle;
        _ = User32.GetWindowThreadProcessId(processWindowHandle, out uint processID);
        if (processID is > 0 and <= int.MaxValue)
            _notRespondingProcessIDs.Add((int)processID);
    }

    private static unsafe bool IsTaskbar(IntPtr windowHandle)
    {
        char* className = stackalloc char[ClassNameCapacity];
        int length = User32.GetClassName(windowHandle, className, ClassNameCapacity);
        return length > 0
               && new ReadOnlySpan<char>(className, length).Equals(
                   TrayAppDotNETConstants.TaskbarWindowClassName,
                   StringComparison.OrdinalIgnoreCase);
    }
}
