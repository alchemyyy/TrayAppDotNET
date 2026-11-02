using Avalonia;
using Avalonia.Input;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Resolves pointer positions from the live OS cursor instead of a queued input event.</summary>
internal static class LatestPointerPosition
{
    /// <summary>
    /// Returns the current mouse position relative to <paramref name="relativeTo"/>, so a backlog of queued
    /// PointerMoved events cannot replay obsolete positions; other pointer types keep the event position.
    /// </summary>
    public static Point GetLatestPosition(this PointerEventArgs eventArgs, Visual relativeTo)
    {
        if (eventArgs.Pointer.Type == PointerType.Mouse
            && OperatingSystem.IsWindows()
            && User32.GetCursorPos(out User32.POINT cursorPosition))
            return relativeTo.PointToClient(new PixelPoint(cursorPosition.X, cursorPosition.Y));

        return eventArgs.GetPosition(relativeTo);
    }
}
