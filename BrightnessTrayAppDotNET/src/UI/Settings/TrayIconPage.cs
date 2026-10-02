using Avalonia.Controls;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Controls;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private StackPanel BuildTrayIconPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(AppStrings.Settings_TrayIcon_SectionHeader)), p);

        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_TrayIcon_MouseWheel_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_MouseWheel_Description)),
            _settings.TrayScrollEnabled,
            v => _settings.TrayScrollEnabled = v,
            p,
            () => RebuildShell(BrightnessSettingsPage.TrayIcon),
            [
                L(nameof(AppStrings.Settings_TrayIcon_MouseWheel_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.MouseWheel));
        stack.Children.Add(Maybe(_settings.TrayScrollEnabled, IntCard(
            L(nameof(AppStrings.Settings_TrayIcon_MouseWheelStep_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_MouseWheelStep_Description)),
            _settings.FlyoutScrollWheelStep,
            AppSettings.FlyoutScrollWheelStepMin,
            AppSettings.FlyoutScrollWheelStepMax,
            v => _settings.FlyoutScrollWheelStep = v,
            p,
            suffix: "%",
            [
                L(nameof(AppStrings.Settings_TrayIcon_MouseWheelStep_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.MouseWheelStep)));
        stack.Children.Add(Maybe(_settings.TrayScrollEnabled, BoolCard(
            L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadScroll_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadScroll_Description)),
            _settings.PrecisionTouchpadScrollEnabled,
            v => _settings.PrecisionTouchpadScrollEnabled = v,
            p,
            () => RebuildShell(BrightnessSettingsPage.TrayIcon),
            [
                L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadScroll_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.PrecisionTouchpadScroll)));
        stack.Children.Add(Maybe(_settings is { TrayScrollEnabled: true, PrecisionTouchpadScrollEnabled: true },
            IntCard(
                L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadUnitsPerScrollStep_Title)),
                L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadUnitsPerScrollStep_Description)),
                _settings.PrecisionTouchpadUnitsPerScrollStep,
                AppSettings.PrecisionTouchpadUnitsPerScrollStepMin,
                AppSettings.PrecisionTouchpadUnitsPerScrollStepMax,
                v => _settings.PrecisionTouchpadUnitsPerScrollStep = v,
                p,
                L(nameof(AppStrings.Common_PercentSuffix)),
                [
                    L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadUnitsPerScrollStep_SearchKeywords))
                ],
                node: ControlMap.Settings.TrayIconPage.PrecisionTouchpadUnitsPerScrollStep)));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_TrayIcon_ContextMenu_Header)),
            p));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_TrayIcon_ShowProfileSelectors_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_ShowProfileSelectors_Description)),
            _settings.ShowProfileSelectorsInMenu,
            v => _settings.ShowProfileSelectorsInMenu = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_TrayIcon_ShowProfileSelectors_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.ShowProfileSelectors));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_TrayIcon_ShowIndividualPowerSelectors_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_ShowIndividualPowerSelectors_Description)),
            _settings.ShowMonitorPowerButtons,
            v => _settings.ShowMonitorPowerButtons = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_TrayIcon_ShowIndividualPowerSelectors_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.ShowIndividualPowerSelectors));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_TrayIcon_ShowAllDisplaysPowerSelector_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_ShowAllDisplaysPowerSelector_Description)),
            _settings.ShowAllDisplaysPowerButton,
            v => _settings.ShowAllDisplaysPowerButton = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_TrayIcon_ShowAllDisplaysPowerSelector_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.ShowAllDisplaysPowerSelector));
        stack.Children.Add(StringComboCard(
            L(nameof(AppStrings.Settings_TrayIcon_MenuPosition_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_MenuPosition_Description)),
            [
                (ContextMenuPosition.Classic, L(nameof(AppStrings.Settings_TrayIcon_MenuPosition_Classic))),
                (ContextMenuPosition.Modern, L(nameof(AppStrings.Settings_TrayIcon_MenuPosition_Modern)))
            ],
            _settings.ContextMenuPosition,
            v => _settings.ContextMenuPosition = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_TrayIcon_MenuPosition_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.MenuPosition));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_TrayIcon_ModifiedActions_Header)),
            p));
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            L(nameof(AppStrings.Settings_TrayIcon_ModifiedActions_Description)),
            p,
            new Avalonia.Thickness(left: 0, top: 0, right: 0, bottom: 8)));
        AddWheelActionCard(
            stack,
            L(nameof(AppStrings.Settings_TrayIcon_MouseWheel_Title)),
            _settings.TrayWheelAction,
            v => _settings.TrayWheelAction = v,
            p,
            ControlMap.Settings.TrayIconPage.WheelAction);
        AddWheelActionCard(
            stack,
            L(nameof(AppStrings.Settings_TrayIcon_CtrlMouseWheel_Title)),
            _settings.TrayCtrlWheelAction,
            v => _settings.TrayCtrlWheelAction = v,
            p,
            ControlMap.Settings.TrayIconPage.CtrlWheelAction);
        AddWheelActionCard(
            stack,
            L(nameof(AppStrings.Settings_TrayIcon_AltMouseWheel_Title)),
            _settings.TrayAltWheelAction,
            v => _settings.TrayAltWheelAction = v,
            p,
            ControlMap.Settings.TrayIconPage.AltWheelAction);
        AddTrayClickActionCard(stack, L(nameof(AppStrings.Settings_TrayIcon_CtrlLeftClick_Title)),
            _settings.TrayCtrlLeftClickAction, v => _settings.TrayCtrlLeftClickAction = v, p,
            ControlMap.Settings.TrayIconPage.CtrlLeftClickAction);
        AddTrayClickActionCard(stack, L(nameof(AppStrings.Settings_TrayIcon_AltLeftClick_Title)),
            _settings.TrayAltLeftClickAction, v => _settings.TrayAltLeftClickAction = v, p,
            ControlMap.Settings.TrayIconPage.AltLeftClickAction);
        AddTrayClickActionCard(stack, L(nameof(AppStrings.Settings_TrayIcon_CtrlRightClick_Title)),
            _settings.TrayCtrlRightClickAction, v => _settings.TrayCtrlRightClickAction = v, p,
            ControlMap.Settings.TrayIconPage.CtrlRightClickAction);
        AddTrayClickActionCard(stack, L(nameof(AppStrings.Settings_TrayIcon_AltRightClick_Title)),
            _settings.TrayAltRightClickAction, v => _settings.TrayAltRightClickAction = v, p,
            ControlMap.Settings.TrayIconPage.AltRightClickAction);
        AddTrayClickActionCard(stack, L(nameof(AppStrings.Settings_TrayIcon_DoubleLeftClick_Title)),
            _settings.TrayDoubleClickAction, v => _settings.TrayDoubleClickAction = v, p,
            ControlMap.Settings.TrayIconPage.DoubleClickAction);
        AddTrayClickActionCard(stack, L(nameof(AppStrings.Settings_TrayIcon_CtrlDoubleLeftClick_Title)),
            _settings.TrayCtrlDoubleLeftClickAction, v => _settings.TrayCtrlDoubleLeftClickAction = v, p,
            ControlMap.Settings.TrayIconPage.CtrlDoubleClickAction);
        AddTrayClickActionCard(stack, L(nameof(AppStrings.Settings_TrayIcon_AltDoubleLeftClick_Title)),
            _settings.TrayAltDoubleLeftClickAction, v => _settings.TrayAltDoubleLeftClickAction = v, p,
            ControlMap.Settings.TrayIconPage.AltDoubleClickAction);

        return stack;
    }

    private void AddWheelActionCard(
        StackPanel stack,
        string title,
        TrayWheelTarget selected,
        Action<TrayWheelTarget> set,
        SettingsPalette p,
        ControlMapNodeID node)
    {
        Border card = StringComboCard(
            title,
            string.Empty,
            TrayWheelOptions(),
            selected,
            set,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_TrayIcon_WheelActions_SearchKeywords))
            ],
            node: node);
        card.IsEnabled = _settings.TrayScrollEnabled;
        stack.Children.Add(card);
    }

    private void AddTrayClickActionCard(
        StackPanel stack,
        string title,
        TrayClickAction selected,
        Action<TrayClickAction> set,
        SettingsPalette p,
        ControlMapNodeID node) =>
        stack.Children.Add(StringComboCard(
            title,
            string.Empty,
            TrayClickOptions(),
            selected,
            set,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_TrayIcon_ClickActions_SearchKeywords))
            ],
            node: node));

    private static IReadOnlyList<(TrayClickAction Value, string Text)> TrayClickOptions() =>
    [
        (TrayClickAction.Nothing, L(nameof(AppStrings.Settings_TrayIcon_ClickAction_Nothing))),
        (TrayClickAction.TurnOffAllDisplays,
            L(nameof(AppStrings.Settings_TrayIcon_ClickAction_AllDisplaysOff))),
        (TrayClickAction.TurnOnAllDisplays, L(nameof(AppStrings.Settings_TrayIcon_ClickAction_AllDisplaysOn))),
        (TrayClickAction.FullBright, L(nameof(AppStrings.Settings_TrayIcon_ClickAction_FullBright))),
        (TrayClickAction.FullDim, L(nameof(AppStrings.Settings_TrayIcon_ClickAction_FullDim)))
    ];

    private static IReadOnlyList<(TrayWheelTarget Value, string Text)> TrayWheelOptions() =>
    [
        (TrayWheelTarget.Nothing, L(nameof(AppStrings.Settings_TrayIcon_WheelAction_Nothing))),
        (TrayWheelTarget.Brightness, L(nameof(AppStrings.Settings_TrayIcon_WheelAction_Brightness))),
        (TrayWheelTarget.NightLight, L(nameof(AppStrings.Settings_TrayIcon_WheelAction_NightLight)))
    ];
}
