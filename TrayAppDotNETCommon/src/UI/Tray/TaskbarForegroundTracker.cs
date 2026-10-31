using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.UI.Tray;

/// <summary>
/// Remembers the last foreground window outside the taskbar. A click on a notification icon can activate the
/// taskbar before the icon's owner hears about it, so the owner cannot read the pre-click foreground window then.
/// </summary>
/// <remarks>
/// Create and dispose it on a UI thread. The out-of-context WinEvent callback runs on the creating thread's message
/// loop.
/// </remarks>
public sealed class TaskbarForegroundTracker : IDisposable
{
    private const uint EventSystemForeground = User32.EVENT_SYSTEM_FOREGROUND;
    private const uint RootOwnerAncestor = 3;
    private const int ClassNameCapacity = 256;

    // The primary and secondary taskbars, and the notification overflow flyouts of Windows 11 and Windows 10
    private static readonly HashSet<string> TaskbarWindowClassNames = new(StringComparer.Ordinal)
    {
        TrayAppDotNETConstants.TaskbarWindowClassName,
        "Shell_SecondaryTrayWnd",
        "TopLevelWindowForOverflowXamlIsland",
        "NotifyIconOverflowWindow"
    };

    private static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> TaskbarWindowClassNameLookup =
        TaskbarWindowClassNames.GetAlternateLookup<ReadOnlySpan<char>>();

    private readonly User32.WinEventProc _callback;
    private IntPtr _hook;
    private IntPtr _lastWindowOutsideTaskbar;
    private uint _taskbarActivationTime;
    private bool _hasTaskbarActivation;
    private bool _callbackFailureLogged;

    public TaskbarForegroundTracker()
    {
        _callback = OnWinEvent;
        IntPtr foregroundWindow = User32.GetForegroundWindow();
        if (!IsTaskbarWindow(foregroundWindow)) RecordWindowOutsideTaskbar(foregroundWindow);
        _hook = User32.SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _callback,
            idProcess: 0,
            idThread: 0,
            User32.WINEVENT_OUTOFCONTEXT);
        if (_hook == IntPtr.Zero)
            TADNLog.Log("TaskbarForegroundTracker: SetWinEventHook failed; only the current foreground is known.");
    }

    /// <summary>
    /// Returns whether the window, or a window it owns, held the foreground before any taskbar interaction still in
    /// progress, such as the click that raised a notification icon callback.
    /// </summary>
    public bool WasForegroundBeforeTaskbar(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return false;

        IntPtr foregroundWindow = User32.GetForegroundWindow();
        IntPtr candidate = IsTaskbarWindow(foregroundWindow) ? _lastWindowOutsideTaskbar : foregroundWindow;
        return candidate != IntPtr.Zero && User32.GetAncestor(candidate, RootOwnerAncestor) == windowHandle;
    }

    /// <summary>
    /// Forgets that the window held the foreground. A window that hid while the taskbar was active hands the
    /// foreground to nothing, so without this the next click would still find it there.
    /// </summary>
    public void Forget(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || _lastWindowOutsideTaskbar == IntPtr.Zero) return;
        if (User32.GetAncestor(_lastWindowOutsideTaskbar, RootOwnerAncestor) == windowHandle)
            _lastWindowOutsideTaskbar = IntPtr.Zero;
    }

    /// <summary>
    /// Milliseconds since the foreground last moved to a taskbar window, or null when it has not since this tracker
    /// started. A recent move is the press of the click being handled; an old one is an earlier taskbar click.
    /// </summary>
    public uint? MillisecondsSinceTaskbarActivation =>
        _hasTaskbarActivation ? unchecked((uint)Environment.TickCount - _taskbarActivationTime) : null;

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;

        _ = User32.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    internal static bool IsTaskbarWindowClassName(ReadOnlySpan<char> className) =>
        TaskbarWindowClassNameLookup.Contains(className);

    // Runs inside the native WinEvent dispatch, where an escaping exception would end the process
    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr windowHandle,
        int objectID,
        int childID,
        uint eventThreadID,
        uint eventTime)
    {
        try
        {
            if (eventType == EventSystemForeground) RecordForegroundWindow(windowHandle, eventTime);
        }
        catch (Exception ex)
        {
            if (_callbackFailureLogged) return;

            _callbackFailureLogged = true;
            TADNLog.Log($"TaskbarForegroundTracker.OnWinEvent: {ex}");
        }
    }

    // The event time is a GetTickCount value, which Environment.TickCount shares
    private void RecordForegroundWindow(IntPtr windowHandle, uint eventTime)
    {
        if (windowHandle == IntPtr.Zero) return;

        if (IsTaskbarWindow(windowHandle))
        {
            _taskbarActivationTime = eventTime;
            _hasTaskbarActivation = true;
            return;
        }

        RecordWindowOutsideTaskbar(windowHandle);
    }

    private void RecordWindowOutsideTaskbar(IntPtr windowHandle)
    {
        if (windowHandle != IntPtr.Zero) _lastWindowOutsideTaskbar = windowHandle;
    }

    private static unsafe bool IsTaskbarWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return false;

        char* className = stackalloc char[ClassNameCapacity];
        int length = User32.GetClassName(windowHandle, className, ClassNameCapacity);
        return length > 0 && IsTaskbarWindowClassName(new ReadOnlySpan<char>(className, length));
    }
}
