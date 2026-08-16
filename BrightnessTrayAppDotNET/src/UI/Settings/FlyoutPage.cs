using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Controls;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private StackPanel BuildFlyoutPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(AppStrings.Settings_Flyout_SectionHeader)), p);

        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_RestoreUndockState_Title)),
            L(nameof(AppStrings.Settings_Flyout_RestoreUndockState_Description)),
            _settings.RestoreFlyoutUndockedOnStartup,
            v => _settings.RestoreFlyoutUndockedOnStartup = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_RestoreUndockState_SearchKeywords))
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Flyout_Visibility_Header)),
            p));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowUndockButton_Title)),
            L(nameof(AppStrings.Settings_Flyout_ShowUndockButton_Description)),
            _settings.AllowFlyoutUndock,
            v => _settings.AllowFlyoutUndock = v,
            p,
            afterSave: () => RebuildShell(BrightnessSettingsPage.Flyout),
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_ShowUndockButton_SearchKeywords))
            ]));
        stack.Children.Add(Maybe(_settings.AllowFlyoutUndock, BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ClampUndockedToScreen_Title)),
            L(nameof(AppStrings.Settings_Flyout_ClampUndockedToScreen_Description)),
            _settings.ClampUndockedFlyoutToScreen,
            v => _settings.ClampUndockedFlyoutToScreen = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_ClampUndockedToScreen_SearchKeywords))
            ])));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowMonitorPowerButtons_Title)),
            L(nameof(AppStrings.Settings_Flyout_ShowMonitorPowerButtons_Description)),
            _settings.ShowFlyoutMonitorPowerButtons,
            v => _settings.ShowFlyoutMonitorPowerButtons = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_ShowMonitorPowerButtons_SearchKeywords))
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowDisplayNumberBadge_Title)),
            L(nameof(AppStrings.Settings_Flyout_ShowDisplayNumberBadge_Description)),
            _settings.ShowFlyoutMonitorNumberBadge,
            v => _settings.ShowFlyoutMonitorNumberBadge = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_ShowDisplayNumberBadge_SearchKeywords))
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowDisplaySettingsButton_Title)),
            L(nameof(AppStrings.Settings_Flyout_ShowDisplaySettingsButton_Description)),
            _settings.ShowFlyoutDisplaySettingsButton,
            v => _settings.ShowFlyoutDisplaySettingsButton = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_ShowDisplaySettingsButton_SearchKeywords))
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowPowerButton_Title)),
            L(nameof(AppStrings.Settings_Flyout_ShowPowerButton_Description)),
            _settings.ShowFlyoutFooterPowerButton,
            v => _settings.ShowFlyoutFooterPowerButton = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_ShowPowerButton_SearchKeywords))
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowMasterSlider_Title)),
            L(nameof(AppStrings.Settings_Flyout_ShowMasterSlider_Description)),
            _settings.ShowMasterSlider,
            v => _settings.ShowMasterSlider = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_ShowMasterSlider_SearchKeywords))
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowIndividualSliders_Title)),
            L(nameof(AppStrings.Settings_Flyout_ShowIndividualSliders_Description)),
            _settings.ShowIndividualSliders,
            v => _settings.ShowIndividualSliders = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_ShowIndividualSliders_SearchKeywords))
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowEnvironmentalCurvesButton_Title)),
            L(nameof(AppStrings.Settings_Flyout_ShowEnvironmentalCurvesButton_Description)),
            _settings.ShowEnvironmentalCurvesButton,
            v => _settings.ShowEnvironmentalCurvesButton = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_ShowEnvironmentalCurvesButton_SearchKeywords))
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowNightLightKelvinLabel_Title)),
            L(nameof(AppStrings.Settings_Flyout_ShowNightLightKelvinLabel_Description)),
            _settings.ShowNightLightKelvinLabel,
            v => _settings.ShowNightLightKelvinLabel = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_ShowNightLightKelvinLabel_SearchKeywords))
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Flyout_Behavior_Header)),
            p));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_PowerButtonOnlyEnabled_Title)),
            L(nameof(AppStrings.Settings_Flyout_PowerButtonOnlyEnabled_Description)),
            _settings.FooterPowerButtonOnlyEnabledMonitors,
            v => _settings.FooterPowerButtonOnlyEnabledMonitors = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_PowerButtonOnlyEnabled_SearchKeywords))
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_NumberKeysSwitchProfile_Title)),
            L(nameof(AppStrings.Settings_Flyout_NumberKeysSwitchProfile_Description)),
            _settings.FlyoutNumberKeysSwitchProfile,
            v => _settings.FlyoutNumberKeysSwitchProfile = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_NumberKeysSwitchProfile_SearchKeywords))
            ]));
        stack.Children.Add(StringComboCard(
            L(nameof(AppStrings.Settings_Flyout_MasterSliderTracking_Title)),
            L(nameof(AppStrings.Settings_Flyout_MasterSliderTracking_Description)),
            [
                (MasterSliderMode.Lowest, L(nameof(AppStrings.Settings_Flyout_MasterSliderTracking_Lowest))),
                (MasterSliderMode.Average, L(nameof(AppStrings.Settings_Flyout_MasterSliderTracking_Average))),
                (MasterSliderMode.Highest, L(nameof(AppStrings.Settings_Flyout_MasterSliderTracking_Highest)))
            ],
            _settings.MasterSliderMode,
            v => _settings.MasterSliderMode = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_MasterSliderTracking_SearchKeywords))
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_PreserveSliderOffsets_Title)),
            L(nameof(AppStrings.Settings_Flyout_PreserveSliderOffsets_Description)),
            _settings.PreserveMasterSliderOffsets,
            v => _settings.PreserveMasterSliderOffsets = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_PreserveSliderOffsets_SearchKeywords))
            ]));
        stack.Children.Add(IntCard(
            L(nameof(AppStrings.Settings_Flyout_MouseWheelStep_Title)),
            L(nameof(AppStrings.Settings_Flyout_MouseWheelStep_Description)),
            _settings.FlyoutScrollWheelStep,
            AppSettings.FlyoutScrollWheelStepMin,
            AppSettings.FlyoutScrollWheelStepMax,
            v => _settings.FlyoutScrollWheelStep = v,
            p,
            "%",
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_MouseWheelStep_SearchKeywords))
            ]));

        return stack;
    }
}
