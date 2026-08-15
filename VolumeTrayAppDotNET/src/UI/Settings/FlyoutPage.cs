using Avalonia.Controls;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
{
    private StackPanel BuildFlyoutPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc(nameof(AppStrings.Settings_Flyout_SectionHeader)), p);

        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_Flyout_RestoreUndockState_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_RestoreUndockState_Description)),
            _settings.RestoreFlyoutUndockedOnStartup,
            v => _settings.RestoreFlyoutUndockedOnStartup = v,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc(nameof(AppStrings.Settings_Flyout_Visibility_Header)), p));
        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_Flyout_ShowUndockButton_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_ShowUndockButton_Description)),
            _settings.AllowFlyoutUndock,
            v => _settings.AllowFlyoutUndock = v,
            p,
            afterSave: RefreshCurrentPage));
        stack.Children.Add(Maybe(_settings.AllowFlyoutUndock, BoolCard(
            Loc(nameof(AppStrings.Settings_Flyout_ClampUndockedToScreen_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_ClampUndockedToScreen_Description)),
            _settings.ClampUndockedFlyoutToScreen,
            v => _settings.ClampUndockedFlyoutToScreen = v,
            p)));
        stack.Children.Add(StringComboCard(
            Loc(nameof(AppStrings.Settings_Flyout_CommunicationsButtonVisibility_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_CommunicationsButtonVisibility_Description)),
            [
                (CommunicationsButtonVisibility.AlwaysShow,
                    Loc(nameof(AppStrings.Settings_Flyout_CommunicationsButtonVisibility_AlwaysShow))),
                (CommunicationsButtonVisibility.WhenDuckingOn,
                    Loc(nameof(AppStrings.Settings_Flyout_CommunicationsButtonVisibility_WhenDuckingOn))),
                (CommunicationsButtonVisibility.Hidden, Loc(nameof(AppStrings.Settings_Flyout_CommunicationsButtonVisibility_Hidden)))
            ],
            _settings.FlyoutCommunicationsButtonVisibility,
            v => _settings.FlyoutCommunicationsButtonVisibility = v,
            p));
        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_Flyout_ShowRecordingDevices_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_ShowRecordingDevices_Description)),
            _settings.ShowRecordingDevicesInFlyout,
            v => _settings.ShowRecordingDevicesInFlyout = v,
            p,
            afterSave: RefreshCurrentPage));
        stack.Children.Add(StringComboCard(
            Loc(nameof(AppStrings.Settings_Flyout_DisconnectedBluetoothDevices_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_DisconnectedBluetoothDevices_Description)),
            [
                (FlyoutDisconnectedBluetoothDeviceVisibility.NeverShow,
                    Loc(nameof(AppStrings.Settings_Flyout_DisconnectedBluetoothDevices_NeverShow))),
                (FlyoutDisconnectedBluetoothDeviceVisibility.Show,
                    Loc(nameof(AppStrings.Settings_Flyout_DisconnectedBluetoothDevices_Show))),
                (FlyoutDisconnectedBluetoothDeviceVisibility.AlwaysShow,
                    Loc(nameof(AppStrings.Settings_Flyout_DisconnectedBluetoothDevices_AlwaysShow))),
                (FlyoutDisconnectedBluetoothDeviceVisibility.AlwaysShowIntermixed,
                    Loc(nameof(AppStrings.Settings_Flyout_DisconnectedBluetoothDevices_AlwaysShowIntermixed)))
            ],
            _settings.FlyoutDisconnectedBluetoothDeviceVisibility,
            v => _settings.FlyoutDisconnectedBluetoothDeviceVisibility = v,
            p));
        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_Flyout_ShowBluetoothDevicesOnlyWhenOn_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_ShowBluetoothDevicesOnlyWhenOn_Description)),
            _settings.ShowBluetoothDevicesOnlyWhenBluetoothIsOn,
            v => _settings.ShowBluetoothDevicesOnlyWhenBluetoothIsOn = v,
            p));
        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_Flyout_UseDynamicPlaybackVolumeGlyph_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_UseDynamicPlaybackVolumeGlyph_Description)),
            _settings.UseDynamicPlaybackVolumeGlyphInFlyout,
            v => _settings.UseDynamicPlaybackVolumeGlyphInFlyout = v,
            p));
        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_Flyout_ShowDeviceFormatText_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_ShowDeviceFormatText_Description)),
            _settings.ShowDeviceFormatText,
            v => _settings.ShowDeviceFormatText = v,
            p));
        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_Flyout_ShowDeviceCodecText_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_ShowDeviceCodecText_Description)),
            _settings.ShowDeviceCodecText,
            v => _settings.ShowDeviceCodecText = v,
            p));
        stack.Children.Add(StringComboCard(
            Loc(nameof(AppStrings.Settings_Flyout_SoundSettingsTarget_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_SoundSettingsTarget_Description)),
            [
                (SoundSettingsTarget.LegacySoundPanel, Loc(nameof(AppStrings.Settings_Flyout_SoundSettingsTarget_Legacy))),
                (SoundSettingsTarget.WindowsSettingsApp, Loc(nameof(AppStrings.Settings_Flyout_SoundSettingsTarget_Modern)))
            ],
            _settings.SoundSettingsTarget,
            v => _settings.SoundSettingsTarget = v,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc(nameof(AppStrings.Settings_Flyout_Layout_Header)), p));
        stack.Children.Add(StringComboCard(
            Loc(nameof(AppStrings.Settings_Flyout_DeviceLayout_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_DeviceLayout_Description)),
            [
                (FlyoutDeviceLayoutStyle.AppsAboveDevice, Loc(nameof(AppStrings.Settings_Flyout_DeviceLayout_AppsAbove))),
                (FlyoutDeviceLayoutStyle.AppsBelowDevice, Loc(nameof(AppStrings.Settings_Flyout_DeviceLayout_AppsBelow)))
            ],
            _settings.FlyoutDeviceLayout,
            v => _settings.FlyoutDeviceLayout = v,
            p));
        stack.Children.Add(StringComboCard(
            Loc(nameof(AppStrings.Settings_Flyout_DeviceTitlePosition_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_DeviceTitlePosition_Description)),
            [
                (FlyoutDeviceTitlePosition.BelowSlider, Loc(nameof(AppStrings.Settings_Flyout_DeviceTitlePosition_BelowSlider))),
                (FlyoutDeviceTitlePosition.AboveSlider, Loc(nameof(AppStrings.Settings_Flyout_DeviceTitlePosition_AboveSlider)))
            ],
            _settings.FlyoutDeviceTitlePosition,
            v => _settings.FlyoutDeviceTitlePosition = v,
            p));
        stack.Children.Add(StringComboCard(
            Loc(nameof(AppStrings.Settings_Flyout_DeviceSort_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_DeviceSort_Description)),
            [
                (FlyoutDeviceSortOrder.StateGrouped, Loc(nameof(AppStrings.Settings_Flyout_DeviceSort_StateGrouped))),
                (FlyoutDeviceSortOrder.WindowsEnumeration, Loc(nameof(AppStrings.Settings_Flyout_DeviceSort_WindowsEnumeration)))
            ],
            _settings.FlyoutDeviceSort,
            v => _settings.FlyoutDeviceSort = v,
            p));
        stack.Children.Add(Maybe(_settings.ShowRecordingDevicesInFlyout, BoolCard(
            Loc(nameof(AppStrings.Settings_Flyout_IntermixRecording_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_IntermixRecording_Description)),
            _settings.IntermixRecordingWithPlaybackInFlyout,
            v => _settings.IntermixRecordingWithPlaybackInFlyout = v,
            p)));
        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_Flyout_HeaderAtBottom_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_HeaderAtBottom_Description)),
            _settings.FlyoutHeaderAtBottom,
            v => _settings.FlyoutHeaderAtBottom = v,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc(nameof(AppStrings.Settings_General_PeakMeter_Header)), p));
        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_Theme_UnifiedPeakMeter_Title)),
            Loc(nameof(AppStrings.Settings_Theme_UnifiedPeakMeter_Description)),
            _settings.UnifiedPeakMeter,
            v => _settings.UnifiedPeakMeter = v,
            p,
            afterSave: RefreshCurrentPage));
        stack.Children.Add(Maybe(_settings.UnifiedPeakMeter, IntCard(
            Loc(nameof(AppStrings.Settings_Theme_UnifiedMeterBias_Title)),
            Loc(nameof(AppStrings.Settings_Theme_UnifiedMeterBias_Description)),
            _settings.UnifiedMeterLowChannelBiasMultiplier,
            AppSettings.UnifiedMeterLowChannelBiasMultiplierMin,
            AppSettings.UnifiedMeterLowChannelBiasMultiplierMax,
            v => _settings.UnifiedMeterLowChannelBiasMultiplier = v,
            p)));
        stack.Children.Add(IntCard(Loc(nameof(AppStrings.Settings_Theme_MeterPeakFps_Title)),
            Loc(nameof(AppStrings.Settings_Theme_MeterPeakFps_Description)),
            _settings.MeterPeakFps, AppSettings.MeterPeakFpsMin, AppSettings.MeterPeakFpsMax,
            v => _settings.MeterPeakFps = v, p));
        stack.Children.Add(IntCard(Loc(nameof(AppStrings.Settings_Theme_MeterPeakSampleRate_Title)),
            Loc(nameof(AppStrings.Settings_Theme_MeterPeakSampleRate_Description)),
            _settings.MeterPeakSampleRate, AppSettings.MeterPeakSampleRateMin, AppSettings.MeterPeakSampleRateMax,
            v => _settings.MeterPeakSampleRate = v, p));
        stack.Children.Add(IntCard(Loc(nameof(AppStrings.Settings_Theme_MeterPeakChangeCeiling_Title)),
            Loc(nameof(AppStrings.Settings_Theme_MeterPeakChangeCeiling_Description)),
            _settings.MeterPeakChangeCeiling, AppSettings.MeterPeakChangeCeilingMin,
            AppSettings.MeterPeakChangeCeilingMax, v => _settings.MeterPeakChangeCeiling = v, p));

        return stack;
    }
}
