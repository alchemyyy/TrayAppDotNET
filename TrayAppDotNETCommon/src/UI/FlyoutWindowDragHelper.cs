using Avalonia;
using Avalonia.Controls;

namespace TrayAppDotNETCommon.UI;

/// <summary>
/// Shared pixel-space drag math for tray flyouts that can snap back to their docked tray position.
/// The owner decides when a drag starts, when to persist coordinates, and what "docked" means.
/// </summary>
public sealed class FlyoutWindowDragHelper
{
    private PixelPoint _grabOffset;
    private PixelPoint _startPosition;
    private PixelPoint _dockedPosition;
    private int _snapTolerance;

    public bool IsCurrentlySnapped { get; private set; }

    public void BeginDrag(
        PixelPoint pointerScreenPosition,
        PixelPoint windowPosition,
        PixelPoint dockedPosition,
        int snapTolerance)
    {
        _grabOffset = new PixelPoint(
            pointerScreenPosition.X - windowPosition.X,
            pointerScreenPosition.Y - windowPosition.Y);
        _startPosition = windowPosition;
        _dockedPosition = dockedPosition;
        _snapTolerance = Math.Max(0, snapTolerance);
        IsCurrentlySnapped = IsWithinSnapTolerance(windowPosition);
    }

    public PixelPoint ComputeNatural(PixelPoint pointerScreenPosition) =>
        new(pointerScreenPosition.X - _grabOffset.X, pointerScreenPosition.Y - _grabOffset.Y);

    public bool ExceedsThreshold(PixelPoint naturalPosition, double threshold)
    {
        int dx = naturalPosition.X - _startPosition.X;
        int dy = naturalPosition.Y - _startPosition.Y;
        return dx * dx + dy * dy >= threshold * threshold;
    }

    public void ApplyDragPosition(Window window, PixelPoint naturalPosition)
    {
        if (IsWithinSnapTolerance(naturalPosition))
        {
            window.Position = _dockedPosition;
            IsCurrentlySnapped = true;
            return;
        }

        window.Position = naturalPosition;
        IsCurrentlySnapped = false;
    }

    private bool IsWithinSnapTolerance(PixelPoint position) =>
        Math.Abs(position.X - _dockedPosition.X) <= _snapTolerance
        && Math.Abs(position.Y - _dockedPosition.Y) <= _snapTolerance;
}
