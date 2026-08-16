using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Controls;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private StackPanel BuildFlyoutPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(AppStrings.Settings_Flyout_SectionHeader), "Flyout"), p);

        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_RestoreUndockState_Title), "Restore undock state on startup"),
            L(nameof(AppStrings.Settings_Flyout_RestoreUndockState_Description),
                "When the app launches, restore the flyout's docked or undocked state from the previous session. When off, the flyout always opens docked."),
            _settings.RestoreFlyoutUndockedOnStartup,
            v => _settings.RestoreFlyoutUndockedOnStartup = v,
            p,
            searchKeywords:
            [
                L("Settings_Flyout_RestoreUndockState_SearchKeywords",
                    "remember floating detached window")
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Flyout_Visibility_Header), "Visibility"),
            p));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowUndockButton_Title), "Show undock button"),
            L(nameof(AppStrings.Settings_Flyout_ShowUndockButton_Description),
                "Show the undock button in the flyout. When off, the flyout always stays anchored to the tray."),
            _settings.AllowFlyoutUndock,
            v => _settings.AllowFlyoutUndock = v,
            p,
            afterSave: () => RebuildShell(BrightnessSettingsPage.Flyout),
            searchKeywords:
            [
                L("Settings_Flyout_ShowUndockButton_SearchKeywords",
                    "detach float popup window")
            ]));
        stack.Children.Add(Maybe(_settings.AllowFlyoutUndock, BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ClampUndockedToScreen_Title), "Keep undocked flyout on screen"),
            L(nameof(AppStrings.Settings_Flyout_ClampUndockedToScreen_Description),
                "Keep the undocked flyout fully inside one monitor's work area when it restores or repositions."),
            _settings.ClampUndockedFlyoutToScreen,
            v => _settings.ClampUndockedFlyoutToScreen = v,
            p,
            searchKeywords:
            [
                L("Settings_Flyout_ClampUndockedToScreen_SearchKeywords",
                    "monitor work area bounds floating window")
            ])));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowMonitorPowerButtons_Title), "Show monitor power buttons"),
            L(nameof(AppStrings.Settings_Flyout_ShowMonitorPowerButtons_Description),
                "Display a per-monitor power off button next to each monitor in the brightness flyout."),
            _settings.ShowFlyoutMonitorPowerButtons,
            v => _settings.ShowFlyoutMonitorPowerButtons = v,
            p,
            searchKeywords:
            [
                L("Settings_Flyout_ShowMonitorPowerButtons_SearchKeywords",
                    "turn screens off individually")
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowDisplayNumberBadge_Title), "Show display number on monitor icons"),
            L(nameof(AppStrings.Settings_Flyout_ShowDisplayNumberBadge_Description),
                "Overlay the OS-assigned display number inside each monitor icon in the brightness flyout."),
            _settings.ShowFlyoutMonitorNumberBadge,
            v => _settings.ShowFlyoutMonitorNumberBadge = v,
            p,
            searchKeywords:
            [
                L("Settings_Flyout_ShowDisplayNumberBadge_SearchKeywords",
                    "screen identifier overlay badge")
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowDisplaySettingsButton_Title), "Show display settings button"),
            L(nameof(AppStrings.Settings_Flyout_ShowDisplaySettingsButton_Description),
                "Show the link to Windows display settings in the brightness flyout footer."),
            _settings.ShowFlyoutDisplaySettingsButton,
            v => _settings.ShowFlyoutDisplaySettingsButton = v,
            p,
            searchKeywords:
            [
                L("Settings_Flyout_ShowDisplaySettingsButton_SearchKeywords",
                    "Windows screen configuration shortcut")
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowPowerButton_Title), "Show power button"),
            L(nameof(AppStrings.Settings_Flyout_ShowPowerButton_Description), "Show a power button in the brightness flyout footer."),
            _settings.ShowFlyoutFooterPowerButton,
            v => _settings.ShowFlyoutFooterPowerButton = v,
            p,
            searchKeywords:
            [
                L("Settings_Flyout_ShowPowerButton_SearchKeywords",
                    "turn screens off footer")
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowMasterSlider_Title), "Show master slider"),
            L(nameof(AppStrings.Settings_Flyout_ShowMasterSlider_Description),
                "Show the All Displays master slider in the brightness flyout."),
            _settings.ShowMasterSlider,
            v => _settings.ShowMasterSlider = v,
            p,
            searchKeywords:
            [
                L("Settings_Flyout_ShowMasterSlider_SearchKeywords",
                    "all screens brightness control")
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowIndividualSliders_Title), "Show individual sliders"),
            L(nameof(AppStrings.Settings_Flyout_ShowIndividualSliders_Description),
                "Show the per-monitor sliders in the brightness flyout."),
            _settings.ShowIndividualSliders,
            v => _settings.ShowIndividualSliders = v,
            p,
            searchKeywords:
            [
                L("Settings_Flyout_ShowIndividualSliders_SearchKeywords",
                    "per display brightness controls")
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowEnvironmentalCurvesButton_Title), "Show environmental curves button"),
            L(nameof(AppStrings.Settings_Flyout_ShowEnvironmentalCurvesButton_Description),
                "Show the environmental curves toggle button in the brightness flyout footer."),
            _settings.ShowEnvironmentalCurvesButton,
            v => _settings.ShowEnvironmentalCurvesButton = v,
            p,
            searchKeywords:
            [
                L("Settings_Flyout_ShowEnvironmentalCurvesButton_SearchKeywords",
                    "adaptive automatic brightness shortcut")
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowNightLightKelvinLabel_Title), "Show Kelvin label on night light slider"),
            L(nameof(AppStrings.Settings_Flyout_ShowNightLightKelvinLabel_Description),
                "Display the current color temperature (e.g. 4500K) above the night light slider in the brightness flyout."),
            _settings.ShowNightLightKelvinLabel,
            v => _settings.ShowNightLightKelvinLabel = v,
            p,
            searchKeywords:
            [
                L("Settings_Flyout_ShowNightLightKelvinLabel_SearchKeywords",
                    "color temperature degrees warmth")
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Flyout_Behavior_Header), "Behavior"),
            p));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_PowerButtonOnlyEnabled_Title), "Power button affects only enabled monitors"),
            L(nameof(AppStrings.Settings_Flyout_PowerButtonOnlyEnabled_Description),
                "When on, the footer power button only powers off monitors enabled in the app. When off, it powers off every monitor."),
            _settings.FooterPowerButtonOnlyEnabledMonitors,
            v => _settings.FooterPowerButtonOnlyEnabledMonitors = v,
            p,
            searchKeywords:
            [
                L("Settings_Flyout_PowerButtonOnlyEnabled_SearchKeywords",
                    "exclude disabled screens shutdown")
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_NumberKeysSwitchProfile_Title), "Number keys switch profile in flyout"),
            L(nameof(AppStrings.Settings_Flyout_NumberKeysSwitchProfile_Description),
                "While the brightness flyout is focused, press 1-4 to switch to the matching profile."),
            _settings.FlyoutNumberKeysSwitchProfile,
            v => _settings.FlyoutNumberKeysSwitchProfile = v,
            p,
            searchKeywords:
            [
                L("Settings_Flyout_NumberKeysSwitchProfile_SearchKeywords",
                    "keyboard shortcuts presets one two three four")
            ]));
        stack.Children.Add(StringComboCard(
            L(nameof(AppStrings.Settings_Flyout_MasterSliderTracking_Title), "Master slider tracking"),
            L(nameof(AppStrings.Settings_Flyout_MasterSliderTracking_Description),
                "How the master slider reflects the individual monitor sliders when it's not driving them."),
            [
                (MasterSliderMode.Lowest, L(nameof(AppStrings.Settings_Flyout_MasterSliderTracking_Lowest), "Lowest")),
                (MasterSliderMode.Average, L(nameof(AppStrings.Settings_Flyout_MasterSliderTracking_Average), "Average")),
                (MasterSliderMode.Highest, L(nameof(AppStrings.Settings_Flyout_MasterSliderTracking_Highest), "Highest"))
            ],
            _settings.MasterSliderMode,
            v => _settings.MasterSliderMode = v,
            p,
            searchKeywords:
            [
                L("Settings_Flyout_MasterSliderTracking_SearchKeywords",
                    "aggregate minimum mean maximum monitors")
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_PreserveSliderOffsets_Title),
                "Preserve slider offsets (prevent slider offset degeneration)"),
            L(nameof(AppStrings.Settings_Flyout_PreserveSliderOffsets_Description),
                "When the master slider pushes an individual monitor past 0% or 100%, retain the overflow so later master adjustments restore the original brightness differences between monitors."),
            _settings.PreserveMasterSliderOffsets,
            v => _settings.PreserveMasterSliderOffsets = v,
            p,
            searchKeywords:
            [
                L("Settings_Flyout_PreserveSliderOffsets_SearchKeywords",
                    "relative brightness differences overflow")
            ]));
        stack.Children.Add(IntCard(
            L(nameof(AppStrings.Settings_Flyout_MouseWheelStep_Title), "Mouse wheel step"),
            L(nameof(AppStrings.Settings_Flyout_MouseWheelStep_Description),
                "How many percent each mouse wheel notch adjusts a brightness slider in the flyout."),
            _settings.FlyoutScrollWheelStep,
            AppSettings.FlyoutScrollWheelStepMin,
            AppSettings.FlyoutScrollWheelStepMax,
            v => _settings.FlyoutScrollWheelStep = v,
            p,
            "%",
            searchKeywords:
            [
                L("Settings_Flyout_MouseWheelStep_SearchKeywords",
                    "scroll sensitivity increment")
            ]));

        return stack;
    }
}
