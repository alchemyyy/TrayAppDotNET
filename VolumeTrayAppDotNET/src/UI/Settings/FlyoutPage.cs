using Avalonia.Controls;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
{
    private StackPanel BuildFlyoutPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc("Settings_Flyout_SectionHeader"), p);

        stack.Children.Add(BoolCard(
            Loc("Settings_Flyout_RestoreUndockState_Title"),
            Loc("Settings_Flyout_RestoreUndockState_Description"),
            _settings.RestoreFlyoutUndockedOnStartup,
            v => _settings.RestoreFlyoutUndockedOnStartup = v,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc("Settings_Flyout_Visibility_Header"), p));
        stack.Children.Add(BoolCard(
            Loc("Settings_Flyout_ShowUndockButton_Title"),
            Loc("Settings_Flyout_ShowUndockButton_Description"),
            _settings.AllowFlyoutUndock,
            v => _settings.AllowFlyoutUndock = v,
            p,
            afterSave: RefreshCurrentPage));
        stack.Children.Add(Maybe(_settings.AllowFlyoutUndock, BoolCard(
            Loc("Settings_Flyout_ClampUndockedToScreen_Title"),
            Loc("Settings_Flyout_ClampUndockedToScreen_Description"),
            _settings.ClampUndockedFlyoutToScreen,
            v => _settings.ClampUndockedFlyoutToScreen = v,
            p)));
        stack.Children.Add(StringComboCard(
            Loc("Settings_Flyout_CommunicationsButtonVisibility_Title"),
            Loc("Settings_Flyout_CommunicationsButtonVisibility_Description"),
            [
                (CommunicationsButtonVisibility.AlwaysShow,
                    Loc("Settings_Flyout_CommunicationsButtonVisibility_AlwaysShow")),
                (CommunicationsButtonVisibility.WhenDuckingOn,
                    Loc("Settings_Flyout_CommunicationsButtonVisibility_WhenDuckingOn")),
                (CommunicationsButtonVisibility.Hidden, Loc("Settings_Flyout_CommunicationsButtonVisibility_Hidden")),
            ],
            _settings.FlyoutCommunicationsButtonVisibility,
            v => _settings.FlyoutCommunicationsButtonVisibility = v,
            p));
        stack.Children.Add(BoolCard(
            Loc("Settings_Flyout_ShowRecordingDevices_Title"),
            Loc("Settings_Flyout_ShowRecordingDevices_Description"),
            _settings.ShowRecordingDevicesInFlyout,
            v => _settings.ShowRecordingDevicesInFlyout = v,
            p,
            afterSave: RefreshCurrentPage));
        stack.Children.Add(BoolCard(
            Loc("Settings_Flyout_UseDynamicPlaybackVolumeGlyph_Title"),
            Loc("Settings_Flyout_UseDynamicPlaybackVolumeGlyph_Description"),
            _settings.UseDynamicPlaybackVolumeGlyphInFlyout,
            v => _settings.UseDynamicPlaybackVolumeGlyphInFlyout = v,
            p));
        stack.Children.Add(BoolCard(
            Loc("Settings_Flyout_ShowDeviceFormatText_Title"),
            Loc("Settings_Flyout_ShowDeviceFormatText_Description"),
            _settings.ShowDeviceFormatText,
            v => _settings.ShowDeviceFormatText = v,
            p));
        stack.Children.Add(BoolCard(
            Loc("Settings_Flyout_ShowDeviceCodecText_Title"),
            Loc("Settings_Flyout_ShowDeviceCodecText_Description"),
            _settings.ShowDeviceCodecText,
            v => _settings.ShowDeviceCodecText = v,
            p));
        stack.Children.Add(StringComboCard(
            Loc("Settings_Flyout_SoundSettingsTarget_Title"),
            Loc("Settings_Flyout_SoundSettingsTarget_Description"),
            [
                (SoundSettingsTarget.LegacySoundPanel, Loc("Settings_Flyout_SoundSettingsTarget_Legacy")),
                (SoundSettingsTarget.WindowsSettingsApp, Loc("Settings_Flyout_SoundSettingsTarget_Modern")),
            ],
            _settings.SoundSettingsTarget,
            v => _settings.SoundSettingsTarget = v,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc("Settings_Flyout_Layout_Header"), p));
        stack.Children.Add(StringComboCard(
            Loc("Settings_Flyout_DeviceLayout_Title"),
            Loc("Settings_Flyout_DeviceLayout_Description"),
            [
                (FlyoutDeviceLayoutStyle.AppsAboveDevice, Loc("Settings_Flyout_DeviceLayout_AppsAbove")),
                (FlyoutDeviceLayoutStyle.AppsBelowDevice, Loc("Settings_Flyout_DeviceLayout_AppsBelow")),
            ],
            _settings.FlyoutDeviceLayout,
            v => _settings.FlyoutDeviceLayout = v,
            p));
        stack.Children.Add(StringComboCard(
            Loc("Settings_Flyout_DeviceTitlePosition_Title"),
            Loc("Settings_Flyout_DeviceTitlePosition_Description"),
            [
                (FlyoutDeviceTitlePosition.BelowSlider, Loc("Settings_Flyout_DeviceTitlePosition_BelowSlider")),
                (FlyoutDeviceTitlePosition.AboveSlider, Loc("Settings_Flyout_DeviceTitlePosition_AboveSlider")),
            ],
            _settings.FlyoutDeviceTitlePosition,
            v => _settings.FlyoutDeviceTitlePosition = v,
            p));
        stack.Children.Add(StringComboCard(
            Loc("Settings_Flyout_DeviceSort_Title"),
            Loc("Settings_Flyout_DeviceSort_Description"),
            [
                (FlyoutDeviceSortOrder.StateGrouped, Loc("Settings_Flyout_DeviceSort_StateGrouped")),
                (FlyoutDeviceSortOrder.WindowsEnumeration, Loc("Settings_Flyout_DeviceSort_WindowsEnumeration")),
            ],
            _settings.FlyoutDeviceSort,
            v => _settings.FlyoutDeviceSort = v,
            p));
        stack.Children.Add(Maybe(_settings.ShowRecordingDevicesInFlyout, BoolCard(
            Loc("Settings_Flyout_IntermixRecording_Title"),
            Loc("Settings_Flyout_IntermixRecording_Description"),
            _settings.IntermixRecordingWithPlaybackInFlyout,
            v => _settings.IntermixRecordingWithPlaybackInFlyout = v,
            p)));
        stack.Children.Add(BoolCard(
            Loc("Settings_Flyout_HeaderAtBottom_Title"),
            Loc("Settings_Flyout_HeaderAtBottom_Description"),
            _settings.FlyoutHeaderAtBottom,
            v => _settings.FlyoutHeaderAtBottom = v,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc("Settings_General_PeakMeter_Header"), p));
        stack.Children.Add(BoolCard(
            Loc("Settings_Theme_UnifiedPeakMeter_Title"),
            Loc("Settings_Theme_UnifiedPeakMeter_Description"),
            _settings.UnifiedPeakMeter,
            v => _settings.UnifiedPeakMeter = v,
            p,
            afterSave: RefreshCurrentPage));
        stack.Children.Add(Maybe(_settings.UnifiedPeakMeter, IntCard(
            Loc("Settings_Theme_UnifiedMeterBias_Title"),
            Loc("Settings_Theme_UnifiedMeterBias_Description"),
            _settings.UnifiedMeterLowChannelBiasMultiplier,
            AppSettings.UnifiedMeterLowChannelBiasMultiplierMin,
            AppSettings.UnifiedMeterLowChannelBiasMultiplierMax,
            v => _settings.UnifiedMeterLowChannelBiasMultiplier = v,
            p)));
        stack.Children.Add(IntCard(Loc("Settings_Theme_MeterPeakFps_Title"),
            Loc("Settings_Theme_MeterPeakFps_Description"),
            _settings.MeterPeakFps, AppSettings.MeterPeakFpsMin, AppSettings.MeterPeakFpsMax,
            v => _settings.MeterPeakFps = v, p));
        stack.Children.Add(IntCard(Loc("Settings_Theme_MeterPeakSampleRate_Title"),
            Loc("Settings_Theme_MeterPeakSampleRate_Description"),
            _settings.MeterPeakSampleRate, AppSettings.MeterPeakSampleRateMin, AppSettings.MeterPeakSampleRateMax,
            v => _settings.MeterPeakSampleRate = v, p));
        stack.Children.Add(IntCard(Loc("Settings_Theme_MeterPeakChangeCeiling_Title"),
            Loc("Settings_Theme_MeterPeakChangeCeiling_Description"),
            _settings.MeterPeakChangeCeiling, AppSettings.MeterPeakChangeCeilingMin,
            AppSettings.MeterPeakChangeCeilingMax, v => _settings.MeterPeakChangeCeiling = v, p));

        return stack;
    }
}
