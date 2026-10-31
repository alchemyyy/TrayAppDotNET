using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Hosts Task Manager content context menus without changing the app's tray-menu visuals.</summary>
internal sealed class TaskManagerContextMenuWindow : ContextMenuWindow
{
    public TaskManagerContextMenuWindow(
        IReadOnlyList<ContextMenuEntry> entries,
        SettingsPalette palette,
        bool enableRoundedCorners,
        ITrayAppDotNETTrayMenuSettings trayMenuSettings)
        : base(entries, CreateOptions(palette, enableRoundedCorners, trayMenuSettings))
    {
#if DEBUG
        TaskManagerContextMenuResources.ResourcesReloaded += OnAXAMLResourcesReloaded;
        Closed += OnWindowClosed;
#endif
    }

#if DEBUG
    private void OnAXAMLResourcesReloaded() => CloseForWarmEviction();

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        TaskManagerContextMenuResources.ResourcesReloaded -= OnAXAMLResourcesReloaded;
        Closed -= OnWindowClosed;
    }
#endif

    internal static ContextMenuWindowOptions CreateOptions(
        SettingsPalette palette,
        bool enableRoundedCorners,
        ITrayAppDotNETTrayMenuSettings trayMenuSettings)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(trayMenuSettings);

        TaskManagerContextMenuResources resources = TaskManagerContextMenuResources.Current;
        return new ContextMenuWindowOptions
        {
            Palette = palette,
            Rounded = enableRoundedCorners,
            FontSize = resources.AxamlTaskManagerContextMenu.FontSize,
            FontWeight = (FontWeight)resources.AxamlTaskManagerContextMenu.FontWeight,
            ItemHeight = resources.AxamlTaskManagerContextMenu.ItemHeight,
            ContextMenuSettings = trayMenuSettings
        };
    }
}
