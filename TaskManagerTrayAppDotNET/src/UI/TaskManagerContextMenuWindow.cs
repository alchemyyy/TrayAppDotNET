using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Hosts Task Manager content context menus without changing the app's tray-menu visuals.</summary>
internal sealed class TaskManagerContextMenuWindow : TrayMenuWindow
{
    public TaskManagerContextMenuWindow(
        IReadOnlyList<TrayMenuEntry> entries,
        SettingsPalette palette,
        bool enableRoundedCorners,
        ITrayAppDotNETTrayMenuSettings trayMenuSettings)
        : base(entries, CreateOptions(palette, enableRoundedCorners, trayMenuSettings))
    {
    }

    internal static TrayMenuWindowOptions CreateOptions(
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
            FontSize = resources.AxamlTaskManagerContextMenu.FontSize,
            FontWeight = (FontWeight)resources.AxamlTaskManagerContextMenu.FontWeight,
            ItemHeight = resources.AxamlTaskManagerContextMenu.ItemHeight,
            TrayMenuSettings = trayMenuSettings
        };
    }
}
