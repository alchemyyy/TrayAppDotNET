using Avalonia.Controls;
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
            afterSave: () => RebuildShell(BrightnessSettingsPage.TrayIcon),
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_TrayIcon_MouseWheel_SearchKeywords))
            ]));
        stack.Children.Add(Maybe(_settings.TrayScrollEnabled, IntCard(
            L(nameof(AppStrings.Settings_TrayIcon_MouseWheelStep_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_MouseWheelStep_Description)),
            _settings.FlyoutScrollWheelStep,
            AppSettings.FlyoutScrollWheelStepMin,
            AppSettings.FlyoutScrollWheelStepMax,
            v => _settings.FlyoutScrollWheelStep = v,
            p,
            "%",
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_TrayIcon_MouseWheelStep_SearchKeywords))
            ])));
        stack.Children.Add(Maybe(_settings.TrayScrollEnabled, BoolCard(
            L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadScroll_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadScroll_Description)),
            _settings.PrecisionTouchpadScrollEnabled,
            v => _settings.PrecisionTouchpadScrollEnabled = v,
            p,
            afterSave: () => RebuildShell(BrightnessSettingsPage.TrayIcon),
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadScroll_SearchKeywords))
            ])));
        stack.Children.Add(Maybe(_settings is { TrayScrollEnabled: true, PrecisionTouchpadScrollEnabled: true }, IntCard(
            L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadUnitsPerScrollStep_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadUnitsPerScrollStep_Description)),
            _settings.PrecisionTouchpadUnitsPerScrollStep,
            AppSettings.PrecisionTouchpadUnitsPerScrollStepMin,
            AppSettings.PrecisionTouchpadUnitsPerScrollStepMax,
            v => _settings.PrecisionTouchpadUnitsPerScrollStep = v,
            p,
            L(nameof(AppStrings.Common_PercentSuffix)),
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadUnitsPerScrollStep_SearchKeywords))
            ])));

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
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_TrayIcon_ShowIndividualPowerSelectors_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_ShowIndividualPowerSelectors_Description)),
            _settings.ShowMonitorPowerButtons,
            v => _settings.ShowMonitorPowerButtons = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_TrayIcon_ShowIndividualPowerSelectors_SearchKeywords))
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_TrayIcon_ShowAllDisplaysPowerSelector_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_ShowAllDisplaysPowerSelector_Description)),
            _settings.ShowAllDisplaysPowerButton,
            v => _settings.ShowAllDisplaysPowerButton = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_TrayIcon_ShowAllDisplaysPowerSelector_SearchKeywords))
            ]));
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
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_TrayIcon_ModifiedActions_Header)),
            p));
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            L(nameof(AppStrings.Settings_TrayIcon_ModifiedActions_Description)),
            p,
            new Avalonia.Thickness(0, 0, 0, 8)));
        AddWheelActionCard(
            stack,
            L(nameof(AppStrings.Settings_TrayIcon_MouseWheel_Title)),
            _settings.TrayWheelAction,
            v => _settings.TrayWheelAction = v,
            p);
        AddWheelActionCard(
            stack,
            L(nameof(AppStrings.Settings_TrayIcon_CtrlMouseWheel_Title)),
            _settings.TrayCtrlWheelAction,
            v => _settings.TrayCtrlWheelAction = v,
            p);
        AddWheelActionCard(
            stack,
            L(nameof(AppStrings.Settings_TrayIcon_AltMouseWheel_Title)),
            _settings.TrayAltWheelAction,
            v => _settings.TrayAltWheelAction = v,
            p);
        AddTrayClickActionCard(stack, L(nameof(AppStrings.Settings_TrayIcon_CtrlLeftClick_Title)),
            _settings.TrayCtrlLeftClickAction, v => _settings.TrayCtrlLeftClickAction = v, p);
        AddTrayClickActionCard(stack, L(nameof(AppStrings.Settings_TrayIcon_AltLeftClick_Title)),
            _settings.TrayAltLeftClickAction, v => _settings.TrayAltLeftClickAction = v, p);
        AddTrayClickActionCard(stack, L(nameof(AppStrings.Settings_TrayIcon_CtrlRightClick_Title)),
            _settings.TrayCtrlRightClickAction, v => _settings.TrayCtrlRightClickAction = v, p);
        AddTrayClickActionCard(stack, L(nameof(AppStrings.Settings_TrayIcon_AltRightClick_Title)),
            _settings.TrayAltRightClickAction, v => _settings.TrayAltRightClickAction = v, p);
        AddTrayClickActionCard(stack, L(nameof(AppStrings.Settings_TrayIcon_DoubleLeftClick_Title)),
            _settings.TrayDoubleClickAction, v => _settings.TrayDoubleClickAction = v, p);
        AddTrayClickActionCard(stack, L(nameof(AppStrings.Settings_TrayIcon_CtrlDoubleLeftClick_Title)),
            _settings.TrayCtrlDoubleLeftClickAction, v => _settings.TrayCtrlDoubleLeftClickAction = v, p);
        AddTrayClickActionCard(stack, L(nameof(AppStrings.Settings_TrayIcon_AltDoubleLeftClick_Title)),
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
            ]);
        card.IsEnabled = _settings.TrayScrollEnabled;
        stack.Children.Add(card);
    }

    private void AddTrayClickActionCard(
        StackPanel stack,
        string title,
        TrayClickAction selected,
        Action<TrayClickAction> set,
        SettingsPalette p) =>
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
            ]));

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
