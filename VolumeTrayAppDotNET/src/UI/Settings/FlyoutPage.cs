using Avalonia.Controls;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
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
            node: ControlMap.Settings.FlyoutPage.RestoreUndockState));

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(L(nameof(CommonStrings.Settings_Flyout_Visibility_Header)), p));
        stack.Children.Add(BoolCard(
            L(nameof(CommonStrings.Settings_Flyout_ShowUndockButton_Title)),
            L(nameof(CommonStrings.Settings_Flyout_ShowUndockButton_Description)),
            _settings.AllowFlyoutUndock,
            v => _settings.AllowFlyoutUndock = v,
            p,
            RefreshCurrentPage,
            [
                L(nameof(AppStrings.Settings_Flyout_ShowUndockButton_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.ShowUndockButton));
        stack.Children.Add(Maybe(_settings.AllowFlyoutUndock, BoolCard(
            L(nameof(CommonStrings.Settings_Flyout_ClampUndockedToScreen_Title)),
            L(nameof(CommonStrings.Settings_Flyout_ClampUndockedToScreen_Description)),
            _settings.ClampUndockedFlyoutToScreen,
            v => _settings.ClampUndockedFlyoutToScreen = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_ClampUndockedToScreen_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.ClampUndockedToScreen)));
        stack.Children.Add(StringComboCard(
            L(nameof(AppStrings.Settings_Flyout_CommunicationsButtonVisibility_Title)),
            L(nameof(AppStrings.Settings_Flyout_CommunicationsButtonVisibility_Description)),
            [
                (CommunicationsButtonVisibility.AlwaysShow,
                    L(nameof(AppStrings.Settings_Flyout_CommunicationsButtonVisibility_AlwaysShow))),
                (CommunicationsButtonVisibility.WhenDuckingOn,
                    L(nameof(AppStrings.Settings_Flyout_CommunicationsButtonVisibility_WhenDuckingOn))),
                (CommunicationsButtonVisibility.Hidden,
                    L(nameof(AppStrings.Settings_Flyout_CommunicationsButtonVisibility_Hidden)))
            ],
            _settings.FlyoutCommunicationsButtonVisibility,
            v => _settings.FlyoutCommunicationsButtonVisibility = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_CommunicationsButtonVisibility_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.CommunicationsButtonVisibility));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowRecordingDevices_Title)),
            L(nameof(AppStrings.Settings_Flyout_ShowRecordingDevices_Description)),
            _settings.ShowRecordingDevicesInFlyout,
            v => _settings.ShowRecordingDevicesInFlyout = v,
            p,
            RefreshCurrentPage,
            [
                L(nameof(AppStrings.Settings_Flyout_ShowRecordingDevices_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.ShowRecordingDevices));
        stack.Children.Add(StringComboCard(
            L(nameof(AppStrings.Settings_Flyout_DisconnectedBluetoothDevices_Title)),
            L(nameof(AppStrings.Settings_Flyout_DisconnectedBluetoothDevices_Description)),
            [
                (FlyoutDisconnectedBluetoothDeviceVisibility.NeverShow,
                    L(nameof(AppStrings.Settings_Flyout_DisconnectedBluetoothDevices_NeverShow))),
                (FlyoutDisconnectedBluetoothDeviceVisibility.Show,
                    L(nameof(AppStrings.Settings_Flyout_DisconnectedBluetoothDevices_Show))),
                (FlyoutDisconnectedBluetoothDeviceVisibility.AlwaysShow,
                    L(nameof(AppStrings.Settings_Flyout_DisconnectedBluetoothDevices_AlwaysShow))),
                (FlyoutDisconnectedBluetoothDeviceVisibility.AlwaysShowIntermixed,
                    L(nameof(AppStrings.Settings_Flyout_DisconnectedBluetoothDevices_AlwaysShowIntermixed)))
            ],
            _settings.FlyoutDisconnectedBluetoothDeviceVisibility,
            v => _settings.FlyoutDisconnectedBluetoothDeviceVisibility = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_DisconnectedBluetoothDevices_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.DisconnectedBluetoothDevices));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_CollapseDisconnectedBluetoothDevices_Title)),
            L(nameof(AppStrings.Settings_Flyout_CollapseDisconnectedBluetoothDevices_Description)),
            _settings.CollapseDisconnectedBluetoothDevicesInFlyout,
            v => _settings.CollapseDisconnectedBluetoothDevicesInFlyout = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_CollapseDisconnectedBluetoothDevices_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.CollapseDisconnectedBluetoothDevices));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowBluetoothDevicesOnlyWhenOn_Title)),
            L(nameof(AppStrings.Settings_Flyout_ShowBluetoothDevicesOnlyWhenOn_Description)),
            _settings.ShowBluetoothDevicesOnlyWhenBluetoothIsOn,
            v => _settings.ShowBluetoothDevicesOnlyWhenBluetoothIsOn = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_ShowBluetoothDevicesOnlyWhenOn_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.ShowBluetoothDevicesOnlyWhenOn));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowBluetoothRadioButton_Title)),
            L(nameof(AppStrings.Settings_Flyout_ShowBluetoothRadioButton_Description)),
            _settings.ShowBluetoothRadioButtonInFlyoutHeader,
            value => _settings.ShowBluetoothRadioButtonInFlyoutHeader = value,
            p,
            RefreshCurrentPage,
            [
                L(nameof(AppStrings.Settings_Flyout_ShowBluetoothRadioButton_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.ShowBluetoothRadioButton));
        stack.Children.Add(Maybe(_settings.ShowBluetoothRadioButtonInFlyoutHeader, StringComboCard(
            L(nameof(AppStrings.Settings_Flyout_BluetoothRadioButtonClickGesture_Title)),
            L(nameof(AppStrings.Settings_Flyout_BluetoothRadioButtonClickGesture_Description)),
            [
                (BluetoothRadioButtonClickGesture.LeftClick,
                    L(nameof(AppStrings.Settings_Flyout_BluetoothRadioButtonClickGesture_LeftClick))),
                (BluetoothRadioButtonClickGesture.ControlLeftClick,
                    L(nameof(AppStrings.Settings_Flyout_BluetoothRadioButtonClickGesture_ControlLeftClick))),
                (BluetoothRadioButtonClickGesture.AltLeftClick,
                    L(nameof(AppStrings.Settings_Flyout_BluetoothRadioButtonClickGesture_AltLeftClick))),
                (BluetoothRadioButtonClickGesture.ShiftLeftClick,
                    L(nameof(AppStrings.Settings_Flyout_BluetoothRadioButtonClickGesture_ShiftLeftClick)))
            ],
            _settings.FlyoutBluetoothRadioButtonClickGesture,
            value => _settings.FlyoutBluetoothRadioButtonClickGesture = value,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_BluetoothRadioButtonClickGesture_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.BluetoothRadioButtonClickGesture)));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_UseDynamicPlaybackVolumeGlyph_Title)),
            L(nameof(AppStrings.Settings_Flyout_UseDynamicPlaybackVolumeGlyph_Description)),
            _settings.UseDynamicPlaybackVolumeGlyphInFlyout,
            v => _settings.UseDynamicPlaybackVolumeGlyphInFlyout = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_UseDynamicPlaybackVolumeGlyph_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.UseDynamicPlaybackVolumeGlyph));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowDeviceFormatText_Title)),
            L(nameof(AppStrings.Settings_Flyout_ShowDeviceFormatText_Description)),
            _settings.ShowDeviceFormatText,
            v => _settings.ShowDeviceFormatText = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_ShowDeviceFormatText_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.ShowDeviceFormatText));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_ShowDeviceCodecText_Title)),
            L(nameof(AppStrings.Settings_Flyout_ShowDeviceCodecText_Description)),
            _settings.ShowDeviceCodecText,
            v => _settings.ShowDeviceCodecText = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_ShowDeviceCodecText_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.ShowDeviceCodecText));
        stack.Children.Add(StringComboCard(
            L(nameof(AppStrings.Settings_Flyout_SoundSettingsTarget_Title)),
            L(nameof(AppStrings.Settings_Flyout_SoundSettingsTarget_Description)),
            [
                (SoundSettingsTarget.LegacySoundPanel,
                    L(nameof(AppStrings.Settings_Flyout_SoundSettingsTarget_Legacy))),
                (SoundSettingsTarget.WindowsSettingsApp,
                    L(nameof(AppStrings.Settings_Flyout_SoundSettingsTarget_Modern)))
            ],
            _settings.SoundSettingsTarget,
            v => _settings.SoundSettingsTarget = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_SoundSettingsTarget_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.SoundSettingsTarget));

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(L(nameof(CommonStrings.Settings_Flyout_Layout_Header)), p));
        stack.Children.Add(StringComboCard(
            L(nameof(AppStrings.Settings_Flyout_DeviceLayout_Title)),
            L(nameof(AppStrings.Settings_Flyout_DeviceLayout_Description)),
            [
                (FlyoutDeviceLayoutStyle.AppsAboveDevice,
                    L(nameof(AppStrings.Settings_Flyout_DeviceLayout_AppsAbove))),
                (FlyoutDeviceLayoutStyle.AppsBelowDevice,
                    L(nameof(AppStrings.Settings_Flyout_DeviceLayout_AppsBelow)))
            ],
            _settings.FlyoutDeviceLayout,
            v => _settings.FlyoutDeviceLayout = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_DeviceLayout_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.DeviceLayout));
        stack.Children.Add(StringComboCard(
            L(nameof(AppStrings.Settings_Flyout_DeviceTitlePosition_Title)),
            L(nameof(AppStrings.Settings_Flyout_DeviceTitlePosition_Description)),
            [
                (FlyoutDeviceTitlePosition.BelowSlider,
                    L(nameof(AppStrings.Settings_Flyout_DeviceTitlePosition_BelowSlider))),
                (FlyoutDeviceTitlePosition.AboveSlider,
                    L(nameof(AppStrings.Settings_Flyout_DeviceTitlePosition_AboveSlider)))
            ],
            _settings.FlyoutDeviceTitlePosition,
            v => _settings.FlyoutDeviceTitlePosition = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_DeviceTitlePosition_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.DeviceTitlePosition));
        stack.Children.Add(StringComboCard(
            L(nameof(AppStrings.Settings_Flyout_DeviceSort_Title)),
            L(nameof(AppStrings.Settings_Flyout_DeviceSort_Description)),
            [
                (FlyoutDeviceSortOrder.StateGrouped, L(nameof(AppStrings.Settings_Flyout_DeviceSort_StateGrouped))),
                (FlyoutDeviceSortOrder.WindowsEnumeration,
                    L(nameof(AppStrings.Settings_Flyout_DeviceSort_WindowsEnumeration)))
            ],
            _settings.FlyoutDeviceSort,
            v => _settings.FlyoutDeviceSort = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_DeviceSort_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.DeviceSort));
        stack.Children.Add(Maybe(_settings.ShowRecordingDevicesInFlyout, BoolCard(
            L(nameof(AppStrings.Settings_Flyout_IntermixRecording_Title)),
            L(nameof(AppStrings.Settings_Flyout_IntermixRecording_Description)),
            _settings.IntermixRecordingWithPlaybackInFlyout,
            v => _settings.IntermixRecordingWithPlaybackInFlyout = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_IntermixRecording_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.IntermixRecording)));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_HeaderAtBottom_Title)),
            L(nameof(AppStrings.Settings_Flyout_HeaderAtBottom_Description)),
            _settings.FlyoutHeaderAtBottom,
            v => _settings.FlyoutHeaderAtBottom = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_HeaderAtBottom_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.HeaderAtBottom));

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(L(nameof(AppStrings.Settings_General_PeakMeter_Header)), p));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Theme_UnifiedPeakMeter_Title)),
            L(nameof(AppStrings.Settings_Theme_UnifiedPeakMeter_Description)),
            _settings.UnifiedPeakMeter,
            v => _settings.UnifiedPeakMeter = v,
            p,
            RefreshCurrentPage,
            [
                L(nameof(AppStrings.Settings_Theme_UnifiedPeakMeter_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.UnifiedPeakMeter));
        stack.Children.Add(Maybe(_settings.UnifiedPeakMeter, IntCard(
            L(nameof(AppStrings.Settings_Theme_UnifiedMeterBias_Title)),
            L(nameof(AppStrings.Settings_Theme_UnifiedMeterBias_Description)),
            _settings.UnifiedMeterLowChannelBiasMultiplier,
            AppSettings.UnifiedMeterLowChannelBiasMultiplierMin,
            AppSettings.UnifiedMeterLowChannelBiasMultiplierMax,
            v => _settings.UnifiedMeterLowChannelBiasMultiplier = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_UnifiedMeterBias_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.UnifiedMeterBias)));
        stack.Children.Add(IntCard(L(nameof(AppStrings.Settings_Theme_MeterPeakFps_Title)),
            L(nameof(AppStrings.Settings_Theme_MeterPeakFps_Description)),
            _settings.MeterPeakFps, AppSettings.MeterPeakFpsMin, AppSettings.MeterPeakFpsMax,
            v => _settings.MeterPeakFps = v, p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_MeterPeakFps_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.MeterPeakFPS));
        stack.Children.Add(IntCard(L(nameof(AppStrings.Settings_Theme_MeterPeakSampleRate_Title)),
            L(nameof(AppStrings.Settings_Theme_MeterPeakSampleRate_Description)),
            _settings.MeterPeakSampleRate, AppSettings.MeterPeakSampleRateMin, AppSettings.MeterPeakSampleRateMax,
            v => _settings.MeterPeakSampleRate = v, p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_MeterPeakSampleRate_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.MeterPeakSampleRate));
        stack.Children.Add(IntCard(L(nameof(AppStrings.Settings_Theme_MeterPeakChangeCeiling_Title)),
            L(nameof(AppStrings.Settings_Theme_MeterPeakChangeCeiling_Description)),
            _settings.MeterPeakChangeCeiling, AppSettings.MeterPeakChangeCeilingMin,
            AppSettings.MeterPeakChangeCeilingMax, v => _settings.MeterPeakChangeCeiling = v, p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_MeterPeakChangeCeiling_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.MeterPeakChangeCeiling));

        return stack;
    }
}
