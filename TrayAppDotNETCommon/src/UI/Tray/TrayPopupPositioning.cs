using Avalonia;

namespace TrayAppDotNETCommon.UI.Tray;

/// <summary>Calculates tray popup positions in physical screen pixels.</summary>
public static class TrayPopupPositioning
{
    /// <summary>
    /// Centers a popup on the tray icon, falls back to the bottom-right corner, and clamps it inside the work area.
    /// </summary>
    public static PixelPoint ResolveDockedPosition(
        PixelRect workArea,
        PixelSize popupSize,
        PixelRect? trayIconRect,
        int edgePadding)
    {
        int padding = Math.Max(0, edgePadding);
        int popupWidth = Math.Max(1, popupSize.Width);
        int popupHeight = Math.Max(1, popupSize.Height);

        int minLeft = workArea.X + padding;
        int minTop = workArea.Y + padding;
        int maxLeft = Math.Max(minLeft, workArea.Right - popupWidth - padding);
        int maxTop = Math.Max(minTop, workArea.Bottom - popupHeight - padding);

        int requestedLeft = workArea.Right - popupWidth - padding;
        int requestedTop = workArea.Bottom - popupHeight - padding;
        if (trayIconRect is { } iconRect)
        {
            requestedLeft = iconRect.Center.X - popupWidth / 2;
            requestedTop = iconRect.Center.Y - popupHeight / 2;
        }

        return new PixelPoint(
            Math.Clamp(requestedLeft, minLeft, maxLeft),
            Math.Clamp(requestedTop, minTop, maxTop));
    }
}
