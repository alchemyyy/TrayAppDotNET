using Avalonia.Controls;
using TrayAppDotNETCommon.UI.ControlMapping;

namespace FanControlTrayAppDotNET.UI.Settings;

public sealed partial class FanSettingsWindow
{
    private StackPanel BuildFlyoutPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_Flyout_SectionHeader)), p);

        stack.Children.Add(BoolCard(
            L(nameof(CommonStrings.Settings_Flyout_RestoreUndockState_Title)),
            L(nameof(CommonStrings.Settings_Flyout_RestoreUndockState_Description)),
            _settings.RestoreFlyoutUndockedOnStartup,
            v => _settings.RestoreFlyoutUndockedOnStartup = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_RestoreUndockState_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.RestoreUndockedOnStartup));
        stack.Children.Add(BoolCard(
            L(nameof(CommonStrings.Settings_Flyout_ShowUndockButton_Title)),
            L(nameof(CommonStrings.Settings_Flyout_ShowUndockButton_Description)),
            _settings.AllowFlyoutUndock,
            v => _settings.AllowFlyoutUndock = v,
            p,
            () => RebuildShell(FanSettingsPage.Flyout),
            [
                L(nameof(AppStrings.Settings_Flyout_ShowUndockButton_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.AllowUndock));
        if (_settings.AllowFlyoutUndock)
        {
            stack.Children.Add(BoolCard(
                L(nameof(CommonStrings.Settings_Flyout_ClampUndockedToScreen_Title)),
                L(nameof(CommonStrings.Settings_Flyout_ClampUndockedToScreen_Description)),
                _settings.ClampUndockedFlyoutToScreen,
                v => _settings.ClampUndockedFlyoutToScreen = v,
                p,
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_Flyout_ClampUndockedToScreen_SearchKeywords))
                ],
                node: ControlMap.Settings.FlyoutPage.ClampUndockedToScreen));
        }

        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowNonFunctioningFans_Title)),
            L(nameof(AppStrings.Settings_Flyout_ShowNonFunctioningFans_Description)),
            _settings.ShowNonFunctioningFans,
            v => _settings.ShowNonFunctioningFans = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_ShowNonFunctioningFans_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.ShowNonFunctioningFans));
        stack.Children.Add(StringComboCard(
            L(nameof(AppStrings.Settings_Flyout_ShowMultipleSliderValues_Title)),
            L(nameof(AppStrings.Settings_Flyout_ShowMultipleSliderValues_Description)),
            MultipleSliderValuesOptions(),
            _settings.ShowMultipleSliderValuesMode,
            v => _settings.ShowMultipleSliderValuesMode = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_ShowMultipleSliderValues_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.ShowMultipleSliderValues));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_Flyout_Layout_Header)), p));
        stack.Children.Add(IntCard(L(nameof(AppStrings.Settings_Theme_CardSpacing_Title)),
            L(nameof(AppStrings.Settings_Theme_CardSpacing_Description)),
            _settings.FlyoutCardSpacing,
            min: 0, max: 48, v => _settings.FlyoutCardSpacing = v, p,
            searchKeywords: [L(nameof(AppStrings.Settings_Theme_CardSpacing_SearchKeywords))],
            node: ControlMap.Settings.FlyoutPage.CardSpacing));
        stack.Children.Add(IntCard(L(nameof(AppStrings.Settings_Theme_CardHorizontalInset_Title)),
            L(nameof(AppStrings.Settings_Theme_CardHorizontalInset_Description)),
            _settings.FlyoutCardHorizontalInset, min: 0, max: 48, v => _settings.FlyoutCardHorizontalInset = v, p,
            searchKeywords: [L(nameof(AppStrings.Settings_Theme_CardHorizontalInset_SearchKeywords))],
            node: ControlMap.Settings.FlyoutPage.CardHorizontalInset));
        stack.Children.Add(IntCard(L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarCardSpacing_Title)),
            L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarCardSpacing_Description)),
            _settings.FlyoutTitleBarCardSpacing, min: 0, max: 48, v => _settings.FlyoutTitleBarCardSpacing = v, p,
            searchKeywords: [L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarCardSpacing_SearchKeywords))],
            node: ControlMap.Settings.FlyoutPage.TitleBarCardSpacing));
        stack.Children.Add(BoolCard(L(nameof(AppStrings.Settings_Theme_CardBorders_Title)),
            L(nameof(AppStrings.Settings_Theme_CardBorders_Description)),
            _settings.EnableCardBorders, v => _settings.EnableCardBorders = v, p,
            searchKeywords: [L(nameof(AppStrings.Settings_Theme_CardBorders_SearchKeywords))],
            node: ControlMap.Settings.FlyoutPage.CardBorders));
        stack.Children.Add(BoolCard(L(nameof(AppStrings.Settings_Theme_HoverCardBorders_Title)),
            L(nameof(AppStrings.Settings_Theme_HoverCardBorders_Description)),
            _settings.EnableHoveredCardBorders, v => _settings.EnableHoveredCardBorders = v, p,
            searchKeywords: [L(nameof(AppStrings.Settings_Theme_HoverCardBorders_SearchKeywords))],
            node: ControlMap.Settings.FlyoutPage.HoveredCardBorders));
        stack.Children.Add(BoolCard(L(nameof(AppStrings.Settings_Theme_HideGroupedFanCardBorders_Title)),
            L(nameof(AppStrings.Settings_Theme_HideGroupedFanCardBorders_Description)),
            _settings.HideGroupedFanCardBorders, v => _settings.HideGroupedFanCardBorders = v, p,
            searchKeywords: [L(nameof(AppStrings.Settings_Theme_HideGroupedFanCardBorders_SearchKeywords))],
            node: ControlMap.Settings.FlyoutPage.HideGroupedFanCardBorders));
        stack.Children.Add(BoolCard(L(nameof(AppStrings.Settings_Theme_GroupedFanCardBackground_Title)),
            L(nameof(AppStrings.Settings_Theme_GroupedFanCardBackground_Description)),
            _settings.UseGroupBackgroundForGroupedFanCards, v => _settings.UseGroupBackgroundForGroupedFanCards = v,
            p,
            searchKeywords: [L(nameof(AppStrings.Settings_Theme_GroupedFanCardBackground_SearchKeywords))],
            node: ControlMap.Settings.FlyoutPage.GroupedFanCardBackground));
        stack.Children.Add(BoolCard(L(nameof(AppStrings.Settings_Theme_SquareFlyoutTitleBarCorners_Title)),
            L(nameof(AppStrings.Settings_Theme_SquareFlyoutTitleBarCorners_Description)),
            _settings.SquareFlyoutTitleBarCorners, v => _settings.SquareFlyoutTitleBarCorners = v, p,
            searchKeywords: [L(nameof(AppStrings.Settings_Theme_SquareFlyoutTitleBarCorners_SearchKeywords))],
            node: ControlMap.Settings.FlyoutPage.SquareTitleBarCorners));

        return stack;
    }

    private static IReadOnlyList<(MultipleSliderValuesDisplayMode Value, string Text)> MultipleSliderValuesOptions() =>
    [
        (MultipleSliderValuesDisplayMode.Disabled, L(nameof(AppStrings.Settings_Flyout_MultipleSliderValues_Disabled))),
        (MultipleSliderValuesDisplayMode.Enabled, L(nameof(AppStrings.Settings_Flyout_MultipleSliderValues_Enabled))),
        (MultipleSliderValuesDisplayMode.OnlyInManual,
            L(nameof(AppStrings.Settings_Flyout_MultipleSliderValues_OnlyInManual)))
    ];
}
