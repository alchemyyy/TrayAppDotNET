using Avalonia.Controls;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
{
    private StackPanel BuildDevicesPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(AppStrings.Settings_Devices_SectionHeader)), p);

        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Devices_SetDefaultCommsToDefault_Title)),
            L(nameof(AppStrings.Settings_Devices_SetDefaultCommsToDefault_Description)),
            _settings.SetDefaultCommsToDefault,
            v => _settings.SetDefaultCommsToDefault = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Devices_SetDefaultCommsToDefault_SearchKeywords))
            ],
            node: ControlMap.Settings.DevicesPage.SetDefaultCommsToDefault));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Devices_ShowNotPresent_Title)),
            L(nameof(AppStrings.Settings_Devices_ShowNotPresent_Description)),
            _settings.ShowNotPresentDevices,
            v => _settings.ShowNotPresentDevices = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Devices_ShowNotPresent_SearchKeywords))
            ],
            node: ControlMap.Settings.DevicesPage.ShowNotPresent));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Devices_ActivateRecordingDevicesForPeakMeters_Title)),
            L(nameof(AppStrings.Settings_Devices_ActivateRecordingDevicesForPeakMeters_Description)),
            _settings.ActivateRecordingDevicesForPeakMeters,
            enabled => _settings.ActivateRecordingDevicesForPeakMeters = enabled,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Devices_ActivateRecordingDevicesForPeakMeters_SearchKeywords))
            ],
            node: ControlMap.Settings.DevicesPage.ActivateRecordingDevicesForPeakMeters));

        string playback = L(nameof(AppStrings.Settings_Common_Playback));
        string recording = L(nameof(AppStrings.Settings_Common_Recording));
        stack.Children.Add(PairColumnHeader(
            L(nameof(AppStrings.Settings_Devices_VisibilityColumn_Header)),
            L(nameof(AppStrings.Settings_Common_Playback)),
            L(nameof(AppStrings.Settings_Common_Recording)),
            p));
        stack.Children.Add(PairBoolCard(
            L(nameof(AppStrings.Settings_Devices_ShowRecording_Title)),
            L(nameof(AppStrings.Settings_Devices_ShowRecording_Description)),
            playback,
            recording,
            leftValue: null,
            setLeft: null,
            _settings.ShowRecordingDevices,
            v => _settings.ShowRecordingDevices = v,
            p,
            showRight: true,
            afterSave: RefreshCurrentPage,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Devices_ShowRecording_SearchKeywords))
            ],
            node: ControlMap.Settings.DevicesPage.ShowRecording.ID));
        stack.Children.Add(PairBoolCard(
            L(nameof(AppStrings.Settings_Devices_ShowDisabled_Title)),
            L(nameof(AppStrings.Settings_Devices_ShowDisabled_Description)),
            playback,
            recording,
            _settings.ShowDisabledPlaybackDevices,
            v => _settings.ShowDisabledPlaybackDevices = v,
            _settings.ShowDisabledRecordingDevices,
            v => _settings.ShowDisabledRecordingDevices = v,
            p,
            showRight: _settings.ShowRecordingDevices,
            afterSave: RefreshCurrentPage,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Devices_ShowDisabled_SearchKeywords))
            ],
            node: ControlMap.Settings.DevicesPage.ShowDisabled.ID));

        bool hideDefaultCards = _settings.ShowDisabledPlaybackDevices
                                && (!_settings.ShowRecordingDevices || _settings.ShowDisabledRecordingDevices);
        stack.Children.Add(Maybe(!hideDefaultCards, PairBoolCard(
            L(nameof(AppStrings.Settings_Devices_ShowDefaultEvenIfDisabled_Title)),
            L(nameof(AppStrings.Settings_Devices_ShowDefaultEvenIfDisabled_Description)),
            playback,
            recording,
            _settings.ShowDefaultPlaybackDeviceEvenIfDisabled,
            v => _settings.ShowDefaultPlaybackDeviceEvenIfDisabled = v,
            _settings.ShowDefaultRecordingDeviceEvenIfDisabled,
            v => _settings.ShowDefaultRecordingDeviceEvenIfDisabled = v,
            p,
            !_settings.ShowDisabledPlaybackDevices,
            _settings is { ShowRecordingDevices: true, ShowDisabledRecordingDevices: false },
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Devices_ShowDefaultEvenIfDisabled_SearchKeywords))
            ],
            node: ControlMap.Settings.DevicesPage.ShowDefaultEvenIfDisabled.ID)));
        stack.Children.Add(Maybe(!hideDefaultCards, PairBoolCard(
            L(nameof(AppStrings.Settings_Devices_ShowDefaultCommsEvenIfDisabled_Title)),
            L(nameof(AppStrings.Settings_Devices_ShowDefaultCommsEvenIfDisabled_Description)),
            playback,
            recording,
            _settings.ShowDefaultCommsPlaybackDeviceEvenIfDisabled,
            v => _settings.ShowDefaultCommsPlaybackDeviceEvenIfDisabled = v,
            _settings.ShowDefaultCommsRecordingDeviceEvenIfDisabled,
            v => _settings.ShowDefaultCommsRecordingDeviceEvenIfDisabled = v,
            p,
            !_settings.ShowDisabledPlaybackDevices,
            _settings is { ShowRecordingDevices: true, ShowDisabledRecordingDevices: false },
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Devices_ShowDefaultCommsEvenIfDisabled_SearchKeywords))
            ],
            node: ControlMap.Settings.DevicesPage.ShowDefaultCommsEvenIfDisabled.ID)));
        stack.Children.Add(PairBoolCard(
            L(nameof(AppStrings.Settings_Devices_ShowDisconnectedPlayback_Title)),
            L(nameof(AppStrings.Settings_Devices_ShowDisconnectedPlayback_Description)),
            playback,
            recording,
            _settings.ShowDisconnectedPlaybackDevices,
            v => _settings.ShowDisconnectedPlaybackDevices = v,
            _settings.ShowDisconnectedRecordingDevices,
            v => _settings.ShowDisconnectedRecordingDevices = v,
            p,
            showRight: _settings.ShowRecordingDevices,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Devices_ShowDisconnectedPlayback_SearchKeywords))
            ],
            node: ControlMap.Settings.DevicesPage.ShowDisconnected.ID));

        stack.Children.Add(PairColumnHeader(
            L(nameof(AppStrings.Settings_Devices_RowButtons_Header)),
            L(nameof(AppStrings.Settings_Common_Playback)),
            L(nameof(AppStrings.Settings_Common_Recording)),
            p));
        stack.Children.Add(PairBoolCard(L(nameof(AppStrings.Settings_Devices_ShowPlaybackLockButton_Title)),
            L(nameof(AppStrings.Settings_Devices_ShowPlaybackLockButton_Description)),
            playback, recording, _settings.ShowLockButtonForPlayback, v => _settings.ShowLockButtonForPlayback = v,
            _settings.ShowLockButtonForRecording, v => _settings.ShowLockButtonForRecording = v, p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Devices_ShowPlaybackLockButton_SearchKeywords))
            ],
            node: ControlMap.Settings.DevicesPage.ShowLockButton.ID));
        stack.Children.Add(PairBoolCard(L(nameof(AppStrings.Settings_Devices_ShowPlaybackEqualizerAPOButton_Title)),
            L(nameof(AppStrings.Settings_Devices_ShowPlaybackEqualizerAPOButton_Description)),
            playback, recording, _settings.ShowEqualizerAPOButtonForPlayback,
            v => _settings.ShowEqualizerAPOButtonForPlayback = v, _settings.ShowEqualizerAPOButtonForRecording,
            v => _settings.ShowEqualizerAPOButtonForRecording = v, p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Devices_ShowPlaybackEqualizerAPOButton_SearchKeywords))
            ],
            node: ControlMap.Settings.DevicesPage.ShowEqualizerAPOButton.ID));
        stack.Children.Add(PairBoolCard(L(nameof(AppStrings.Settings_Devices_ShowPlaybackDefaultDeviceButton_Title)),
            L(nameof(AppStrings.Settings_Devices_ShowPlaybackDefaultDeviceButton_Description)),
            playback, recording, _settings.ShowDefaultDeviceButtonForPlayback,
            v => _settings.ShowDefaultDeviceButtonForPlayback = v, _settings.ShowDefaultDeviceButtonForRecording,
            v => _settings.ShowDefaultDeviceButtonForRecording = v, p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Devices_ShowPlaybackDefaultDeviceButton_SearchKeywords))
            ],
            node: ControlMap.Settings.DevicesPage.ShowDefaultDeviceButton.ID));
        stack.Children.Add(PairBoolCard(L(nameof(AppStrings.Settings_Devices_ShowPlaybackBatteryButton_Title)),
            L(nameof(AppStrings.Settings_Devices_ShowPlaybackBatteryButton_Description)),
            playback, recording, _settings.ShowBatteryButtonForPlayback,
            v => _settings.ShowBatteryButtonForPlayback = v, _settings.ShowBatteryButtonForRecording,
            v => _settings.ShowBatteryButtonForRecording = v, p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Devices_ShowPlaybackBatteryButton_SearchKeywords))
            ],
            node: ControlMap.Settings.DevicesPage.ShowBatteryButton.ID));
        stack.Children.Add(PairBoolCard(L(nameof(AppStrings.Settings_Devices_ShowRecordingListenButton_Title)),
            L(nameof(AppStrings.Settings_Devices_ShowRecordingListenButton_Description)),
            playback, recording, leftValue: null, setLeft: null, _settings.ShowListenButtonForRecording,
            v => _settings.ShowListenButtonForRecording = v, p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Devices_ShowRecordingListenButton_SearchKeywords))
            ],
            node: ControlMap.Settings.DevicesPage.ShowListenButton.ID));

        return stack;
    }
}
