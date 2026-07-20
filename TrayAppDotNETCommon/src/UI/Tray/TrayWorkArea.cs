using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.UI.Tray;

/// <summary>Resolves monitor work areas without depending on Avalonia's cached screen snapshot.</summary>
public static class TrayWorkArea
{
    /// <summary>
    /// Gets the current work area for an anchor point, falling back to Avalonia screen data when the
    /// native query is unavailable.
    /// </summary>
    public static PixelRect Resolve(Screens? screens, PixelPoint anchor, PixelRect fallback)
    {
        if (TryGetCurrent(anchor, out PixelRect currentWorkArea)) return currentWorkArea;

        return (screens?.ScreenFromPoint(anchor) ?? screens?.Primary)?.WorkingArea
               ?? fallback;
    }

    private static bool TryGetCurrent(PixelPoint anchor, out PixelRect workArea)
    {
        workArea = default;
        if (!OperatingSystem.IsWindows()) return false;

        User32.POINT point = new() { X = anchor.X, Y = anchor.Y };
        IntPtr monitor = User32.MonitorFromPoint(point, User32.MONITOR_DEFAULTTONULL);
        if (monitor == IntPtr.Zero) return false;

        User32.MONITORINFO monitorInfo = new()
        {
            Size = Marshal.SizeOf<User32.MONITORINFO>()
        };
        if (!User32.GetMonitorInfo(monitor, ref monitorInfo)) return false;

        int width = monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left;
        int height = monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top;
        if (width <= 0 || height <= 0) return false;

        workArea = new PixelRect(monitorInfo.WorkArea.Left, monitorInfo.WorkArea.Top, width, height);
        return true;
    }
}
