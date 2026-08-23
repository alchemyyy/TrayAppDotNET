using Avalonia.Input;

namespace TrayAppDotNETCommon.UI;

/// <summary>
/// Bounded application-lifetime standard cursors shared by programmatic UI generations.
/// </summary>
public static class TrayAppDotNETCursors
{
    public static Cursor Arrow { get; } = new(StandardCursorType.Arrow);
    public static Cursor Hand { get; } = new(StandardCursorType.Hand);
    public static Cursor IBeam { get; } = new(StandardCursorType.Ibeam);
    public static Cursor Cross { get; } = new(StandardCursorType.Cross);
    public static Cursor SizeWestEast { get; } = new(StandardCursorType.SizeWestEast);
    public static Cursor SizeAll { get; } = new(StandardCursorType.SizeAll);
    public static Cursor BottomRightCorner { get; } = new(StandardCursorType.BottomRightCorner);
}
