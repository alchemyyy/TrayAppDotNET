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
    }

    /// <summary>Creates the anchored menu used to present saved process searches.</summary>
    internal static EditableContextMenuWindow CreateSavedSearchMenu(
        IReadOnlyList<EditableContextMenuEntry> entries,
        SettingsPalette palette,
        bool enableRoundedCorners,
        ITrayAppDotNETTrayMenuSettings trayMenuSettings)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(trayMenuSettings);

        TaskManagerContextMenuResources resources = TaskManagerContextMenuResources.Current;
        return new EditableContextMenuWindow(
            entries,
            new EditableContextMenuWindowOptions
            {
                Palette = palette,
                Rounded = enableRoundedCorners,
                FontSize = resources.AxamlTaskManagerContextMenu.FontSize,
                FontWeight = (FontWeight)resources.AxamlTaskManagerContextMenu.FontWeight,
                ItemHeight = resources.AxamlTaskManagerContextMenu.SavedSearchItemHeight,
                ItemHoverColor = palette.SearchListItemHover,
                ContextMenuSettings = trayMenuSettings,
                InvokeOnPointerReleased = true,
                ActivateOnShow = false,
                KeepOpenWhenOwnerActivated = true,
                RootBorderThickness = resources.AxamlTaskManagerContextMenu.AutocompleteBorderThickness,
                RootCornerRadius = resources.AxamlTaskManagerContextMenu.AutocompleteCornerRadius,
                RootPadding = resources.AxamlTaskManagerContextMenu.AutocompletePadding,
                ItemCornerRadius = resources.AxamlTaskManagerContextMenu.AutocompleteItemCornerRadius,
                ItemPadding = resources.AxamlTaskManagerContextMenu.SavedSearchItemPadding,
                ItemMargin = default,
                ItemMinWidth = 0,
                RowSpacing = 0
            });
    }

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
