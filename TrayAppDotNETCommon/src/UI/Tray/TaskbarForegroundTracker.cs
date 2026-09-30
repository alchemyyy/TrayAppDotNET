using System.Runtime.InteropServices;
using System.Text;
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
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint RootOwnerAncestor = 3;
    private const int ClassNameCapacity = 256;

    // The primary and secondary taskbars, and the notification overflow flyouts of Windows 11 and Windows 10
    private static readonly HashSet<string> TaskbarWindowClassNames = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "TopLevelWindowForOverflowXamlIsland",
        "NotifyIconOverflowWindow"
    };

    private readonly WinEventCallback _callback;
    private readonly StringBuilder _classNameBuffer = new(ClassNameCapacity);
    private IntPtr _hook;
    private IntPtr _lastWindowOutsideTaskbar;

    public TaskbarForegroundTracker()
    {
        _callback = OnWinEvent;
        RecordForegroundWindow(User32.GetForegroundWindow());
        _hook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _callback,
            processID: 0,
            threadID: 0,
            WinEventOutOfContext);
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

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;

        _ = UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    internal static bool IsTaskbarWindowClassName(string className) =>
        TaskbarWindowClassNames.Contains(className);

    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr windowHandle,
        int objectID,
        int childID,
        uint eventThreadID,
        uint eventTime)
    {
        if (eventType == EventSystemForeground) RecordForegroundWindow(windowHandle);
    }

    private void RecordForegroundWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || IsTaskbarWindow(windowHandle)) return;
        _lastWindowOutsideTaskbar = windowHandle;
    }

    private bool IsTaskbarWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return false;

        _classNameBuffer.Clear();
        int length = GetClassNameW(windowHandle, _classNameBuffer, _classNameBuffer.Capacity);
        return length > 0 && IsTaskbarWindowClassName(_classNameBuffer.ToString(startIndex: 0, length));
    }

    private delegate void WinEventCallback(
        IntPtr hook,
        uint eventType,
        IntPtr windowHandle,
        int objectID,
        int childID,
        uint eventThreadID,
        uint eventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMinimum,
        uint eventMaximum,
        IntPtr moduleHandle,
        WinEventCallback callback,
        uint processID,
        uint threadID,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr windowHandle, StringBuilder className, int maximumCount);
}
