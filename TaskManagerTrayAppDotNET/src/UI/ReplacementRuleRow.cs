using Avalonia.Controls;
using TrayAppDotNETCommon.UI.ControlMapping;
using TaskManagerGlyphCatalog = TaskManagerTrayAppDotNET.Visuals.GlyphCatalog;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>The control map nodes of one replacement rule row.</summary>
internal readonly record struct ReplacementRuleRowNodes(
    ControlMapNodeID Row,
    ControlMapNodeID MatchPattern,
    ControlMapNodeID Replacement,
    ControlMapNodeID Delete);

/// <summary>
/// One editable regex replacement rule: match pattern, replacement and a delete button, optionally after a leading
/// control. The settings window's hardware-name rules and a Performance card's description rules both use it.
/// </summary>
internal static class ReplacementRuleRow
{
    public static Border Build(
        string matchPattern,
        string replacement,
        string matchPatternTooltip,
        Action<string> matchPatternChanged,
        Action<string> replacementChanged,
        Action delete,
        ReplacementRuleRowNodes nodes,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Control? leading = null)
    {
        TextBox matchPatternBox = TrayAppDotNETSettingsUI.TextBox(palette, double.NaN, matchPattern)
            .MapTo(nodes.MatchPattern);
        matchPatternBox.MinWidth = resources.AxamlTaskManagerSettings.HardwareNameRuleTextMinimumWidth;
        matchPatternBox.PlaceholderText =
            LocalizationManager.Instance[nameof(AppStrings.Settings_Performance_HardwareNameReplacements_MatchPattern_Placeholder)];
        matchPatternBox.TextChanged += (_, _) => matchPatternChanged(matchPatternBox.Text ?? string.Empty);
        TrayAppDotNETToolTip.SetTip(matchPatternBox, matchPatternTooltip);

        TextBox replacementBox = TrayAppDotNETSettingsUI.TextBox(palette, double.NaN, replacement)
            .MapTo(nodes.Replacement);
        replacementBox.MinWidth = resources.AxamlTaskManagerSettings.HardwareNameRuleTextMinimumWidth;
        replacementBox.PlaceholderText =
            LocalizationManager.Instance[nameof(AppStrings.Settings_Performance_HardwareNameReplacements_Replacement_Placeholder)];
        replacementBox.TextChanged += (_, _) => replacementChanged(replacementBox.Text ?? string.Empty);
        TrayAppDotNETToolTip.SetTip(
            replacementBox,
            LocalizationManager.Instance[nameof(AppStrings.Settings_Performance_HardwareNameReplacements_Replacement_Tooltip)]);

        double deleteButtonSize = resources.AxamlTaskManagerSettings.HardwareNameRuleDeleteButtonSize;
        SettingsButton deleteButton = new(TaskManagerGlyphCatalog.CLOSE, palette, transparentBase: true)
        {
            Width = deleteButtonSize,
            Height = deleteButtonSize,
            MinHeight = deleteButtonSize,
            Padding = resources.AxamlTaskManagerSettings.HardwareNameRuleDeleteButtonPadding
        };
        deleteButton.MapTo(nodes.Delete);
        deleteButton.Click += (_, _) => delete();
        TrayAppDotNETToolTip.SetTip(
            deleteButton,
            LocalizationManager.Instance[nameof(AppStrings.Settings_Performance_HardwareNameReplacements_Delete_Tooltip)]);

        // ColumnSpacing separates even an empty column, so a row without a leading control has no column for it
        Grid row = new() { ColumnSpacing = resources.AxamlTaskManagerSettings.HardwareNameRuleColumnSpacing };
        int column = 0;
        if (leading != null)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            row.Children.Add(leading);
            column++;
        }

        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(matchPatternBox, column++);
        row.Children.Add(matchPatternBox);
        Grid.SetColumn(replacementBox, column++);
        row.Children.Add(replacementBox);
        Grid.SetColumn(deleteButton, column);
        row.Children.Add(deleteButton);
        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.ControlBackground),
            CornerRadius = resources.AxamlTaskManagerSettings.HardwareNameRuleRowCornerRadius,
            Padding = resources.AxamlTaskManagerSettings.HardwareNameRuleRowPadding,
            Child = row
        }.MapTo(nodes.Row);
    }
}
