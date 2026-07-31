using Avalonia;
using Avalonia.Controls;

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
        int popupWidth = Math.Max(1, popupSize.Width);
        int popupHeight = Math.Max(1, popupSize.Height);
        int padding = Math.Max(0, edgePadding);

        int requestedLeft = workArea.Right - popupWidth - padding;
        int requestedTop = workArea.Bottom - popupHeight - padding;
        if (trayIconRect is { } iconRect)
        {
            requestedLeft = iconRect.Center.X - popupWidth / 2;
            requestedTop = iconRect.Center.Y - popupHeight / 2;
        }

        return ClampToWorkArea(
            workArea,
            new PixelSize(popupWidth, popupHeight),
            new PixelPoint(requestedLeft, requestedTop),
            padding);
    }

    /// <summary>Clamps a popup position inside a monitor work area.</summary>
    public static PixelPoint ClampToWorkArea(
        PixelRect workArea,
        PixelSize popupSize,
        PixelPoint target,
        int edgePadding)
    {
        int padding = Math.Max(0, edgePadding);
        int popupWidth = Math.Max(1, popupSize.Width);
        int popupHeight = Math.Max(1, popupSize.Height);
        int minLeft = workArea.X + padding;
        int minTop = workArea.Y + padding;
        int maxLeft = Math.Max(minLeft, workArea.Right - popupWidth - padding);
        int maxTop = Math.Max(minTop, workArea.Bottom - popupHeight - padding);

        return new PixelPoint(
            Math.Clamp(target.X, minLeft, maxLeft),
            Math.Clamp(target.Y, minTop, maxTop));
    }

    /// <summary>Clamps a saved popup position inside the monitor that contains it.</summary>
    public static PixelPoint ClampToSavedMonitor(
        Screens? screens,
        PixelRect fallbackWorkArea,
        PixelSize popupSize,
        PixelPoint savedPosition,
        int edgePadding)
    {
        PixelRect workArea = TrayWorkArea.Resolve(screens, savedPosition, fallbackWorkArea);
        return ClampToWorkArea(workArea, popupSize, savedPosition, edgePadding);
    }
}
