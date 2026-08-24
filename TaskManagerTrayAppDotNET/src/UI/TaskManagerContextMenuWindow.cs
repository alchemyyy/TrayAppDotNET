using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Hosts Task Manager content context menus without changing the app's tray-menu visuals.</summary>
internal sealed class TaskManagerContextMenuWindow : TrayMenuWindow
{
    private const int ScreenEdgePadding = 8;
    private const int OffscreenCoordinate = -32_000;
    private const int FallbackWorkAreaWidth = 1920;
    private const int FallbackWorkAreaHeight = 1080;

    public TaskManagerContextMenuWindow(
        IReadOnlyList<TrayMenuEntry> entries,
        SettingsPalette palette,
        bool enableRoundedCorners,
        ITrayAppDotNETTrayMenuSettings trayMenuSettings)
        : base(entries, CreateOptions(palette, enableRoundedCorners, trayMenuSettings))
    {
    }

    /// <summary>Shows the menu at an arbitrary screen point within its owner's work area.</summary>
    public void ShowAt(Window owner, PixelPoint screenPosition)
    {
        ArgumentNullException.ThrowIfNull(owner);

        Opacity = 0;
        Position = new PixelPoint(OffscreenCoordinate, OffscreenCoordinate);
        Show(owner);
        Dispatcher.UIThread.Post(() => PositionAt(screenPosition), DispatcherPriority.Loaded);
    }

    private static TrayMenuWindowOptions CreateOptions(
        SettingsPalette palette,
        bool enableRoundedCorners,
        ITrayAppDotNETTrayMenuSettings trayMenuSettings)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(trayMenuSettings);

        TaskManagerContextMenuResources resources = TaskManagerContextMenuResources.Current;
        return new TrayMenuWindowOptions
        {
            Palette = palette,
            Rounded = enableRoundedCorners,
            FontSize = resources.ItemFontSize,
            FontWeight = resources.ItemFontWeight,
            ItemHeight = resources.ItemHeight,
            TrayMenuSettings = trayMenuSettings
        };
    }

    private void PositionAt(PixelPoint screenPosition)
    {
        if (!IsVisible) return;

        UpdateLayout();
        PixelRect workArea = (Screens.ScreenFromPoint(screenPosition) ?? Screens.Primary)?.WorkingArea
                             ?? new PixelRect(0, 0, FallbackWorkAreaWidth, FallbackWorkAreaHeight);
        int menuWidth = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        int menuHeight = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        int maximumX = Math.Max(workArea.X + ScreenEdgePadding, workArea.Right - menuWidth - ScreenEdgePadding);
        int maximumY = Math.Max(workArea.Y + ScreenEdgePadding, workArea.Bottom - menuHeight - ScreenEdgePadding);
        Position = new PixelPoint(
            Math.Clamp(screenPosition.X, workArea.X + ScreenEdgePadding, maximumX),
            Math.Clamp(screenPosition.Y, workArea.Y + ScreenEdgePadding, maximumY));
        Opacity = 1;
        Activate();
    }
}
