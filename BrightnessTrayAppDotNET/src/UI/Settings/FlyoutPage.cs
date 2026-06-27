using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Controls;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private StackPanel BuildFlyoutPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L("Settings_Flyout_SectionHeader", "Flyout"), p);

        stack.Children.Add(BoolCard(
            L("Settings_Flyout_RestoreUndockState_Title", "Restore undock state on startup"),
            L("Settings_Flyout_RestoreUndockState_Description",
                "When the app launches, restore the flyout's docked or undocked state from the previous session. When off, the flyout always opens docked."),
            _settings.RestoreFlyoutUndockedOnStartup,
            v => _settings.RestoreFlyoutUndockedOnStartup = v,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_Flyout_Visibility_Header", "Visibility"),
            p));
        stack.Children.Add(BoolCard(
            L("Settings_Flyout_ShowUndockButton_Title", "Show undock button"),
            L("Settings_Flyout_ShowUndockButton_Description",
                "Show the undock button in the flyout. When off, the flyout always stays anchored to the tray."),
            _settings.AllowFlyoutUndock,
            v => _settings.AllowFlyoutUndock = v,
            p));
        stack.Children.Add(BoolCard(
            L("Settings_Flyout_ShowMonitorPowerButtons_Title", "Show monitor power buttons"),
            L("Settings_Flyout_ShowMonitorPowerButtons_Description",
                "Display a per-monitor power off button next to each monitor in the brightness flyout."),
            _settings.ShowFlyoutMonitorPowerButtons,
            v => _settings.ShowFlyoutMonitorPowerButtons = v,
            p));
        stack.Children.Add(BoolCard(
            L("Settings_Flyout_ShowDisplayNumberBadge_Title", "Show display number on monitor icons"),
            L("Settings_Flyout_ShowDisplayNumberBadge_Description",
                "Overlay the OS-assigned display number inside each monitor icon in the brightness flyout."),
            _settings.ShowFlyoutMonitorNumberBadge,
            v => _settings.ShowFlyoutMonitorNumberBadge = v,
            p));
        stack.Children.Add(BoolCard(
            L("Settings_Flyout_ShowDisplaySettingsButton_Title", "Show display settings button"),
            L("Settings_Flyout_ShowDisplaySettingsButton_Description",
                "Show the link to Windows display settings in the brightness flyout footer."),
            _settings.ShowFlyoutDisplaySettingsButton,
            v => _settings.ShowFlyoutDisplaySettingsButton = v,
            p));
        stack.Children.Add(BoolCard(
            L("Settings_Flyout_ShowPowerButton_Title", "Show power button"),
            L("Settings_Flyout_ShowPowerButton_Description", "Show a power button in the brightness flyout footer."),
            _settings.ShowFlyoutFooterPowerButton,
            v => _settings.ShowFlyoutFooterPowerButton = v,
            p));
        stack.Children.Add(BoolCard(
            L("Settings_Flyout_ShowMasterSlider_Title", "Show master slider"),
            L("Settings_Flyout_ShowMasterSlider_Description",
                "Show the All Displays master slider in the brightness flyout."),
            _settings.ShowMasterSlider,
            v => _settings.ShowMasterSlider = v,
            p));
        stack.Children.Add(BoolCard(
            L("Settings_Flyout_ShowIndividualSliders_Title", "Show individual sliders"),
            L("Settings_Flyout_ShowIndividualSliders_Description",
                "Show the per-monitor sliders in the brightness flyout."),
            _settings.ShowIndividualSliders,
            v => _settings.ShowIndividualSliders = v,
            p));
        stack.Children.Add(BoolCard(
            L("Settings_Flyout_ShowEnvironmentalCurvesButton_Title", "Show environmental curves button"),
            L("Settings_Flyout_ShowEnvironmentalCurvesButton_Description",
                "Show the environmental curves toggle button in the brightness flyout footer."),
            _settings.ShowEnvironmentalCurvesButton,
            v => _settings.ShowEnvironmentalCurvesButton = v,
            p));
        stack.Children.Add(BoolCard(
            L("Settings_Flyout_ShowNightLightKelvinLabel_Title", "Show Kelvin label on night light slider"),
            L("Settings_Flyout_ShowNightLightKelvinLabel_Description",
                "Display the current color temperature (e.g. 4500K) above the night light slider in the brightness flyout."),
            _settings.ShowNightLightKelvinLabel,
            v => _settings.ShowNightLightKelvinLabel = v,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_Flyout_Behavior_Header", "Behavior"),
            p));
        stack.Children.Add(BoolCard(
            L("Settings_Flyout_PowerButtonOnlyEnabled_Title", "Power button affects only enabled monitors"),
            L("Settings_Flyout_PowerButtonOnlyEnabled_Description",
                "When on, the footer power button only powers off monitors enabled in the app. When off, it powers off every monitor."),
            _settings.FooterPowerButtonOnlyEnabledMonitors,
            v => _settings.FooterPowerButtonOnlyEnabledMonitors = v,
            p));
        stack.Children.Add(BoolCard(
            L("Settings_Flyout_NumberKeysSwitchProfile_Title", "Number keys switch profile in flyout"),
            L("Settings_Flyout_NumberKeysSwitchProfile_Description",
                "While the brightness flyout is focused, press 1-4 to switch to the matching profile."),
            _settings.FlyoutNumberKeysSwitchProfile,
            v => _settings.FlyoutNumberKeysSwitchProfile = v,
            p));
        stack.Children.Add(StringComboCard(
            L("Settings_Flyout_MasterSliderTracking_Title", "Master slider tracking"),
            L("Settings_Flyout_MasterSliderTracking_Description",
                "How the master slider reflects the individual monitor sliders when it's not driving them."),
            [
                (MasterSliderMode.Lowest, L("Settings_Flyout_MasterSliderTracking_Lowest", "Lowest")),
                (MasterSliderMode.Average, L("Settings_Flyout_MasterSliderTracking_Average", "Average")),
                (MasterSliderMode.Highest, L("Settings_Flyout_MasterSliderTracking_Highest", "Highest")),
            ],
            _settings.MasterSliderMode,
            v => _settings.MasterSliderMode = v,
            p));
        stack.Children.Add(BoolCard(
            L("Settings_Flyout_PreserveSliderOffsets_Title",
                "Preserve slider offsets (prevent slider offset degeneration)"),
            L("Settings_Flyout_PreserveSliderOffsets_Description",
                "When the master slider pushes an individual monitor past 0% or 100%, retain the overflow so later master adjustments restore the original brightness differences between monitors."),
            _settings.PreserveMasterSliderOffsets,
            v => _settings.PreserveMasterSliderOffsets = v,
            p));
        stack.Children.Add(IntCard(
            L("Settings_Flyout_MouseWheelStep_Title", "Mouse wheel step"),
            L("Settings_Flyout_MouseWheelStep_Description",
                "How many percent each mouse wheel notch adjusts a brightness slider in the flyout."),
            _settings.FlyoutScrollWheelStep,
            1,
            50,
            v => _settings.FlyoutScrollWheelStep = v,
            p,
            "%"));

        return stack;
    }
}
