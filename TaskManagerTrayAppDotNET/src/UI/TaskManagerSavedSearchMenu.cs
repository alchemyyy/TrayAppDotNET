using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Builds the editable dropdown that presents saved process searches under the search box.</summary>
internal static class TaskManagerSavedSearchMenu
{
    public static EditableContextMenuWindow Create(
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
                ItemMargin = resources.AxamlTaskManagerContextMenu.SavedSearchItemMargin,
                ItemMinWidth = resources.AxamlTaskManagerContextMenu.SavedSearchItemMinWidth,
                RowSpacing = resources.AxamlTaskManagerContextMenu.SavedSearchRowSpacing
            });
    }
}
