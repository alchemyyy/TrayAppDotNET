using Avalonia.Controls;

namespace FanControlTrayAppDotNET.UI.Settings;

public sealed partial class FanSettingsWindow
{
    private StackPanel BuildTrayIconPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_TrayIcon_SectionHeader)), p);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_TrayIcon_Tooltip_Header)), p));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_TrayIcon_ShowCPUTemp_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_ShowCPUTemp_Description)),
            _settings.ShowCPUTempInTooltip, v => _settings.ShowCPUTempInTooltip = v, p,
            searchKeywords: [L(nameof(AppStrings.Settings_TrayIcon_ShowCPUTemp_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.CPUTemperatureTooltip));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_TrayIcon_ShowGPUTemp_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_ShowGPUTemp_Description)),
            _settings.ShowGPUTempInTooltip, v => _settings.ShowGPUTempInTooltip = v, p,
            searchKeywords: [L(nameof(AppStrings.Settings_TrayIcon_ShowGPUTemp_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.GPUTemperatureTooltip));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_TrayIcon_ContextMenu_Header)), p));
        stack.Children.Add(StringComboCard(
            L(nameof(CommonStrings.Settings_TrayIcon_MenuPosition_Title)),
            L(nameof(CommonStrings.Settings_TrayIcon_MenuPosition_Description)),
            [
                (TrayAppDotNETContextMenuPosition.Classic, L(nameof(CommonStrings.Settings_TrayIcon_MenuPosition_Classic))),
                (TrayAppDotNETContextMenuPosition.Modern, L(nameof(CommonStrings.Settings_TrayIcon_MenuPosition_Modern)))
            ],
            _settings.ContextMenuPosition,
            v => _settings.ContextMenuPosition = v,
            p,
            searchKeywords: [L(nameof(AppStrings.Settings_TrayIcon_MenuPosition_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.ContextMenuPosition));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_TrayIcon_ModifiedActions_Header)), p));
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            L(nameof(AppStrings.Settings_TrayIcon_ModifiedActions_Description)), p));
        stack.Children.Add(StringComboCard(
            L(nameof(CommonStrings.Settings_TrayIcon_DoubleLeftClick_Title)),
            string.Empty,
            TrayClickActionOptions(),
            _settings.TrayDoubleClickAction,
            v => _settings.TrayDoubleClickAction = v,
            p,
            searchKeywords: [L(nameof(AppStrings.Settings_TrayIcon_ClickActions_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.DoubleClickAction));
        stack.Children.Add(StringComboCard(
            L(nameof(CommonStrings.Settings_TrayIcon_CtrlLeftClick_Title)),
            string.Empty,
            TrayClickActionOptions(),
            _settings.TrayCtrlLeftClickAction,
            v => _settings.TrayCtrlLeftClickAction = v,
            p,
            searchKeywords: [L(nameof(AppStrings.Settings_TrayIcon_ClickActions_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.CtrlLeftClickAction));
        stack.Children.Add(StringComboCard(
            L(nameof(CommonStrings.Settings_TrayIcon_AltLeftClick_Title)),
            string.Empty,
            TrayClickActionOptions(),
            _settings.TrayAltLeftClickAction,
            v => _settings.TrayAltLeftClickAction = v,
            p,
            searchKeywords: [L(nameof(AppStrings.Settings_TrayIcon_ClickActions_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.AltLeftClickAction));
        stack.Children.Add(StringComboCard(
            L(nameof(CommonStrings.Settings_TrayIcon_CtrlRightClick_Title)),
            string.Empty,
            TrayClickActionOptions(),
            _settings.TrayCtrlRightClickAction,
            v => _settings.TrayCtrlRightClickAction = v,
            p,
            searchKeywords: [L(nameof(AppStrings.Settings_TrayIcon_ClickActions_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.CtrlRightClickAction));
        stack.Children.Add(StringComboCard(
            L(nameof(CommonStrings.Settings_TrayIcon_AltRightClick_Title)),
            string.Empty,
            TrayClickActionOptions(),
            _settings.TrayAltRightClickAction,
            v => _settings.TrayAltRightClickAction = v,
            p,
            searchKeywords: [L(nameof(AppStrings.Settings_TrayIcon_ClickActions_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.AltRightClickAction));
        stack.Children.Add(StringComboCard(
            L(nameof(CommonStrings.Settings_TrayIcon_CtrlDoubleLeftClick_Title)),
            string.Empty,
            TrayClickActionOptions(),
            _settings.TrayCtrlDoubleLeftClickAction,
            v => _settings.TrayCtrlDoubleLeftClickAction = v,
            p,
            searchKeywords: [L(nameof(AppStrings.Settings_TrayIcon_ClickActions_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.CtrlDoubleClickAction));
        stack.Children.Add(StringComboCard(
            L(nameof(CommonStrings.Settings_TrayIcon_AltDoubleLeftClick_Title)),
            string.Empty,
            TrayClickActionOptions(),
            _settings.TrayAltDoubleLeftClickAction,
            v => _settings.TrayAltDoubleLeftClickAction = v,
            p,
            searchKeywords: [L(nameof(AppStrings.Settings_TrayIcon_ClickActions_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.AltDoubleClickAction));
        return stack;
    }

    private static IReadOnlyList<(TrayClickAction Value, string Text)> TrayClickActionOptions() =>
    [
        (TrayClickAction.Nothing, L(nameof(CommonStrings.Settings_TrayIcon_ClickAction_Nothing))),
        (TrayClickAction.OpenSettings, L(nameof(CommonStrings.Settings_TrayIcon_ClickAction_OpenSettings)))
    ];
}
