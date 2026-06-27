using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Controls;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private StackPanel BuildTrayIconPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L("Settings_TrayIcon_SectionHeader", "Tray Icon"), p);

        stack.Children.Add(BoolCard(
            L("Settings_TrayIcon_MouseWheel_Title", "Mouse wheel"),
            L("Settings_TrayIcon_MouseWheel_Description",
                "Enable scrolling the mouse wheel while hovering the tray icon."),
            _settings.TrayScrollEnabled,
            v => _settings.TrayScrollEnabled = v,
            p,
            afterSave: () => RebuildShell(BrightnessSettingsPage.TrayIcon)));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_TrayIcon_ContextMenu_Header", "Context menu"),
            p));
        stack.Children.Add(BoolCard(
            L("Settings_TrayIcon_ShowProfileSelectors_Title", "Show profile selectors"),
            L("Settings_TrayIcon_ShowProfileSelectors_Description",
                "Display brightness profile entries in the tray right-click menu."),
            _settings.ShowProfileSelectorsInMenu,
            v => _settings.ShowProfileSelectorsInMenu = v,
            p));
        stack.Children.Add(BoolCard(
            L("Settings_TrayIcon_ShowIndividualPowerSelectors_Title", "Show individual power selectors"),
            L("Settings_TrayIcon_ShowIndividualPowerSelectors_Description",
                "Display per-monitor power toggles in the tray right-click menu."),
            _settings.ShowMonitorPowerButtons,
            v => _settings.ShowMonitorPowerButtons = v,
            p));
        stack.Children.Add(BoolCard(
            L("Settings_TrayIcon_ShowAllDisplaysPowerSelector_Title", "Show all-displays power selector"),
            L("Settings_TrayIcon_ShowAllDisplaysPowerSelector_Description",
                "Display a single entry that powers every display off."),
            _settings.ShowAllDisplaysPowerButton,
            v => _settings.ShowAllDisplaysPowerButton = v,
            p));
        stack.Children.Add(StringComboCard(
            L("Settings_TrayIcon_MenuPosition_Title", "Menu position"),
            L("Settings_TrayIcon_MenuPosition_Description",
                "Classic opens at the cursor. Modern docks the menu above the taskbar like the Windows 11 system flyouts."),
            [
                (ContextMenuPosition.Classic, L("Settings_TrayIcon_MenuPosition_Classic", "Classic")),
                (ContextMenuPosition.Modern, L("Settings_TrayIcon_MenuPosition_Modern", "Modern")),
            ],
            _settings.ContextMenuPosition,
            v => _settings.ContextMenuPosition = v,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_TrayIcon_ModifiedActions_Header", "Modified actions"),
            p));
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            L("Settings_TrayIcon_ModifiedActions_Description",
                "Assign actions to modified clicks or scrolls on the tray icon."),
            p,
            new Avalonia.Thickness(0, 0, 0, 8)));
        AddWheelActionCard(
            stack,
            L("Settings_TrayIcon_MouseWheel_Title", "Mouse wheel"),
            _settings.TrayWheelAction,
            v => _settings.TrayWheelAction = v,
            p);
        AddWheelActionCard(
            stack,
            L("Settings_TrayIcon_CtrlMouseWheel_Title", "Ctrl + mouse wheel"),
            _settings.TrayCtrlWheelAction,
            v => _settings.TrayCtrlWheelAction = v,
            p);
        AddWheelActionCard(
            stack,
            L("Settings_TrayIcon_AltMouseWheel_Title", "Alt + mouse wheel"),
            _settings.TrayAltWheelAction,
            v => _settings.TrayAltWheelAction = v,
            p);
        AddTrayClickActionCard(stack, L("Settings_TrayIcon_CtrlLeftClick_Title", "Ctrl + left click"),
            _settings.TrayCtrlLeftClickAction, v => _settings.TrayCtrlLeftClickAction = v, p);
        AddTrayClickActionCard(stack, L("Settings_TrayIcon_AltLeftClick_Title", "Alt + left click"),
            _settings.TrayAltLeftClickAction, v => _settings.TrayAltLeftClickAction = v, p);
        AddTrayClickActionCard(stack, L("Settings_TrayIcon_CtrlRightClick_Title", "Ctrl + right click"),
            _settings.TrayCtrlRightClickAction, v => _settings.TrayCtrlRightClickAction = v, p);
        AddTrayClickActionCard(stack, L("Settings_TrayIcon_AltRightClick_Title", "Alt + right click"),
            _settings.TrayAltRightClickAction, v => _settings.TrayAltRightClickAction = v, p);
        AddTrayClickActionCard(stack, L("Settings_TrayIcon_DoubleLeftClick_Title", "Double left click"),
            _settings.TrayDoubleClickAction, v => _settings.TrayDoubleClickAction = v, p);
        AddTrayClickActionCard(stack, L("Settings_TrayIcon_CtrlDoubleLeftClick_Title", "Ctrl + double left click"),
            _settings.TrayCtrlDoubleLeftClickAction, v => _settings.TrayCtrlDoubleLeftClickAction = v, p);
        AddTrayClickActionCard(stack, L("Settings_TrayIcon_AltDoubleLeftClick_Title", "Alt + double left click"),
            _settings.TrayAltDoubleLeftClickAction, v => _settings.TrayAltDoubleLeftClickAction = v, p);

        return stack;
    }

    private void AddWheelActionCard(
        StackPanel stack,
        string title,
        TrayWheelTarget selected,
        Action<TrayWheelTarget> set,
        SettingsPalette p)
    {
        Border card = StringComboCard(title, string.Empty, TrayWheelOptions(), selected, set, p);
        card.IsEnabled = _settings.TrayScrollEnabled;
        stack.Children.Add(card);
    }

    private void AddTrayClickActionCard(
        StackPanel stack,
        string title,
        TrayClickAction selected,
        Action<TrayClickAction> set,
        SettingsPalette p) =>
        stack.Children.Add(StringComboCard(title, string.Empty, TrayClickOptions(), selected, set, p));

    private static IReadOnlyList<(TrayClickAction Value, string Text)> TrayClickOptions() =>
    [
        (TrayClickAction.Nothing, L("Settings_TrayIcon_ClickAction_Nothing", "Nothing")),
        (TrayClickAction.TurnOffAllDisplays,
            L("Settings_TrayIcon_ClickAction_AllDisplaysOff", "Turn off all displays")),
        (TrayClickAction.TurnOnAllDisplays, L("Settings_TrayIcon_ClickAction_AllDisplaysOn", "Turn on all displays")),
        (TrayClickAction.FullBright, L("Settings_TrayIcon_ClickAction_FullBright", "Full bright")),
        (TrayClickAction.FullDim, L("Settings_TrayIcon_ClickAction_FullDim", "Full dim")),
    ];

    private static IReadOnlyList<(TrayWheelTarget Value, string Text)> TrayWheelOptions() =>
    [
        (TrayWheelTarget.Nothing, L("Settings_TrayIcon_WheelAction_Nothing", "Nothing")),
        (TrayWheelTarget.Brightness, L("Settings_TrayIcon_WheelAction_Brightness", "Brightness")),
        (TrayWheelTarget.NightLight, L("Settings_TrayIcon_WheelAction_NightLight", "Night Light")),
    ];
}
