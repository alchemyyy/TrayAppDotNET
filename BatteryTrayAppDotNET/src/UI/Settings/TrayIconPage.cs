#pragma warning disable CA1822

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Settings;
using GlyphApplicator = TrayAppDotNETCommon.Visuals.GlyphApplicator;

namespace BatteryTrayAppDotNET.UI.Settings;

public sealed partial class BatterySettingsWindow
{
    private StackPanel BuildTrayIconPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_TrayIcon_SectionHeader)), p);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_TrayIcon_ContextMenu_Header)), p));
        stack.Children.Add(ComboCard(
            L(nameof(CommonStrings.Settings_TrayIcon_MenuPosition_Title)),
            L(nameof(CommonStrings.Settings_TrayIcon_MenuPosition_Description)),
            [
                (nameof(TrayAppDotNETContextMenuPosition.Classic), L(nameof(CommonStrings.Settings_TrayIcon_MenuPosition_Classic))),
                (nameof(TrayAppDotNETContextMenuPosition.Modern), L(nameof(CommonStrings.Settings_TrayIcon_MenuPosition_Modern)))
            ],
            _settings.ContextMenuPosition.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayAppDotNETContextMenuPosition value))
                    _settings.ContextMenuPosition = value;
            },
            p,
            autoSizeToText: true,
            autoSizeMode: SettingsComboBoxAutoSizeMode.SelectedItem,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_TrayIcon_MenuPosition_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.ContextMenuPosition));

        return stack;
    }
}
