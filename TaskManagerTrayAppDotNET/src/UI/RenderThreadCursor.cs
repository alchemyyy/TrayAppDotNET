using Avalonia;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Reads the live Win32 cursor for a composition handler on the render thread.</summary>
internal static class RenderThreadCursor
{
    /// <summary>
    /// Maps the cursor to host-local DIPs while it is over the owning top-level window. Any other top-level window
    /// under the cursor, including a popup of this app, reports no position.
    /// </summary>
    /// <param name="windowHandle">The HWND of the host's top level.</param>
    /// <param name="clientOrigin">The host's origin in the top level's client DIPs.</param>
    /// <param name="renderScaling">The top level's render scaling.</param>
    /// <param name="localPosition">The cursor in host-local DIPs.</param>
    public static bool TryGetLocalPosition(
        IntPtr windowHandle,
        Point clientOrigin,
        double renderScaling,
        out Point localPosition)
    {
        localPosition = default;
        if (!User32.GetCursorPos(out User32.POINT screenPosition)) return false;

        IntPtr pointedWindow = User32.WindowFromPoint(screenPosition);
        if (pointedWindow == IntPtr.Zero
            || User32.GetAncestor(pointedWindow, User32.GA_ROOT) != windowHandle)
            return false;

        User32.POINT clientPosition = screenPosition;
        if (!User32.ScreenToClient(windowHandle, ref clientPosition)) return false;

        localPosition = new Point(
            clientPosition.X / renderScaling - clientOrigin.X,
            clientPosition.Y / renderScaling - clientOrigin.Y);
        return true;
    }
}
