using Avalonia.Controls;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.UI.Tray;

/// <summary>What a left click on the tray icon does to the window it toggles.</summary>
public enum TrayToggleAction
{
    /// <summary>Shows a hidden or minimized window; a flyout docks at the tray first.</summary>
    Show,

    /// <summary>Brings a visible window the user was not working in forward where it is, without moving it.</summary>
    Activate,

    Hide,

    /// <summary>The click's own taskbar activation already hid the window, so the click must not reopen it.</summary>
    None
}

/// <summary>
/// The one close-on-click guard for windows a tray icon toggles. The press on the icon activates the taskbar before
/// the app hears about the click, which already hides a docked flyout on deactivation; the window was the
/// foreground before the taskbar took it, so the click means "close", not "open again". Only a taskbar activation
/// within the double-click time before the click is that press: a flyout an earlier taskbar click hid opens again.
/// </summary>
/// <remarks>Create and dispose it on the UI thread; it owns the app's <see cref="TaskbarForegroundTracker"/>.</remarks>
public sealed class TrayWindowToggle : IDisposable
{
    private readonly TaskbarForegroundTracker _tracker = new();

    /// <summary>Decides the click for <paramref name="window"/>; null means nothing is shown yet.</summary>
    public TrayToggleAction Decide(Window? window)
    {
        if (window == null) return TrayToggleAction.Show;

        IntPtr handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        TrayToggleAction action = Decide(
            window.IsVisible,
            window.WindowState == WindowState.Minimized,
            _tracker.WasForegroundBeforeTaskbar(handle),
            IsPressOfThisClick(_tracker.MillisecondsSinceTaskbarActivation, User32.GetDoubleClickTime()));

        // The window is closed either way now, so the next click starts from "not foreground"
        if (action is TrayToggleAction.Hide or TrayToggleAction.None) _tracker.Forget(handle);
        return action;
    }

    /// <summary>Whether the taskbar took the foreground recently enough to be the press of the click being handled.</summary>
    internal static bool IsPressOfThisClick(uint? millisecondsSinceTaskbarActivation, uint doubleClickTime) =>
        millisecondsSinceTaskbarActivation is { } elapsed && elapsed <= doubleClickTime;

    internal static TrayToggleAction Decide(
        bool isVisible,
        bool isMinimized,
        bool wasForegroundBeforeTaskbar,
        bool taskbarActivatedByThisClick)
    {
        if (!isVisible)
        {
            return wasForegroundBeforeTaskbar && taskbarActivatedByThisClick
                ? TrayToggleAction.None
                : TrayToggleAction.Show;
        }

        if (isMinimized) return TrayToggleAction.Show;
        return wasForegroundBeforeTaskbar ? TrayToggleAction.Hide : TrayToggleAction.Activate;
    }

    public void Dispose() => _tracker.Dispose();
}
