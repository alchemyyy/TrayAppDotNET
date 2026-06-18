#pragma warning disable CA1822

using Avalonia;
using Avalonia.Controls;
using NetworkTrayAppDotNET.Models;

namespace NetworkTrayAppDotNET.UI;

public sealed partial class NetworkSettingsWindow
{
    private StackPanel BuildTrayIconPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc("Settings_TrayIcon_SectionHeader"), p);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            Loc("Settings_TrayIcon_ContextMenu_Header"), p));
        stack.Children.Add(ComboCard(
            Loc("Settings_TrayIcon_MenuPosition_Title"),
            Loc("Settings_TrayIcon_MenuPosition_Description"),
            [
                ("Classic", Loc("Settings_TrayIcon_MenuPosition_Classic")),
                ("Modern", Loc("Settings_TrayIcon_MenuPosition_Modern")),
            ],
            _settings.ContextMenuPosition.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out ContextMenuPosition value)) _settings.ContextMenuPosition = value;
            },
            p,
            autoSizeToText: true,
            autoSizeMode: SettingsComboBoxAutoSizeMode.SelectedItem));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            Loc("Settings_TrayIcon_ModifiedActions_Header"), p));
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            Loc("Settings_TrayIcon_ModifiedActions_Description"), p, new Thickness(0, 0, 0, 8)));

        AddTrayClickActionCard(stack, Loc("Settings_TrayIcon_CtrlLeftClick_Title"), _settings.TrayCtrlLeftClickAction,
            v => _settings.TrayCtrlLeftClickAction = v, p);
        AddTrayClickActionCard(stack, Loc("Settings_TrayIcon_AltLeftClick_Title"), _settings.TrayAltLeftClickAction,
            v => _settings.TrayAltLeftClickAction = v, p);
        AddTrayClickActionCard(stack, Loc("Settings_TrayIcon_CtrlRightClick_Title"), _settings.TrayCtrlRightClickAction,
            v => _settings.TrayCtrlRightClickAction = v, p);
        AddTrayClickActionCard(stack, Loc("Settings_TrayIcon_AltRightClick_Title"), _settings.TrayAltRightClickAction,
            v => _settings.TrayAltRightClickAction = v, p);
        AddTrayClickActionCard(stack, Loc("Settings_TrayIcon_DoubleLeftClick_Title"), _settings.TrayDoubleClickAction,
            v => _settings.TrayDoubleClickAction = v, p);
        AddTrayClickActionCard(stack, Loc("Settings_TrayIcon_CtrlDoubleLeftClick_Title"),
            _settings.TrayCtrlDoubleLeftClickAction,
            v => _settings.TrayCtrlDoubleLeftClickAction = v, p);
        AddTrayClickActionCard(stack, Loc("Settings_TrayIcon_AltDoubleLeftClick_Title"),
            _settings.TrayAltDoubleLeftClickAction,
            v => _settings.TrayAltDoubleLeftClickAction = v, p);
        return stack;
    }

    private void AddTrayClickActionCard(
        StackPanel stack,
        string title,
        TrayClickAction selected,
        Action<TrayClickAction> set,
        SettingsPalette p)
    {
        stack.Children.Add(ComboCard(
            title,
            string.Empty,
            [
                ("Nothing", Loc("Settings_TrayIcon_ClickAction_Nothing")),
                ("OpenSettings", Loc("Settings_TrayIcon_ClickAction_OpenSettings")),
                ("OpenAdapterSettings", Loc("Settings_TrayIcon_ClickAction_OpenAdapterSettings")),
            ],
            selected.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayClickAction value))
                    set(value);
            },
            p,
            autoSizeToText: true,
            autoSizeMode: SettingsComboBoxAutoSizeMode.SelectedItem));
    }
}
