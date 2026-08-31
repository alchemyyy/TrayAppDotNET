using Avalonia.Controls;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
{
    private StackPanel BuildDevicesPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc(nameof(AppStrings.Settings_Devices_SectionHeader)), p);

        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_Devices_SetDefaultCommsToDefault_Title)),
            Loc(nameof(AppStrings.Settings_Devices_SetDefaultCommsToDefault_Description)),
            _settings.SetDefaultCommsToDefault,
            v => _settings.SetDefaultCommsToDefault = v,
            p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Devices_SetDefaultCommsToDefault_SearchKeywords))
            ]));
        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_Devices_ShowNotPresent_Title)),
            Loc(nameof(AppStrings.Settings_Devices_ShowNotPresent_Description)),
            _settings.ShowNotPresentDevices,
            v => _settings.ShowNotPresentDevices = v,
            p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Devices_ShowNotPresent_SearchKeywords))
            ]));
        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_Devices_ActivateRecordingDevicesForPeakMeters_Title)),
            Loc(nameof(AppStrings.Settings_Devices_ActivateRecordingDevicesForPeakMeters_Description)),
            _settings.ActivateRecordingDevicesForPeakMeters,
            enabled => _settings.ActivateRecordingDevicesForPeakMeters = enabled,
            p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Devices_ActivateRecordingDevicesForPeakMeters_SearchKeywords))
            ]));

        string playback = Loc(nameof(AppStrings.Settings_Common_Playback));
        string recording = Loc(nameof(AppStrings.Settings_Common_Recording));
        stack.Children.Add(PairColumnHeader(Loc(nameof(AppStrings.Settings_Devices_VisibilityColumn_Header)), p));
        stack.Children.Add(PairBoolCard(
            Loc(nameof(AppStrings.Settings_Devices_ShowRecording_Title)),
            Loc(nameof(AppStrings.Settings_Devices_ShowRecording_Description)),
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
                Loc(nameof(AppStrings.Settings_Devices_ShowRecording_SearchKeywords))
            ]));
        stack.Children.Add(PairBoolCard(
            Loc(nameof(AppStrings.Settings_Devices_ShowDisabled_Title)),
            Loc(nameof(AppStrings.Settings_Devices_ShowDisabled_Description)),
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
                Loc(nameof(AppStrings.Settings_Devices_ShowDisabled_SearchKeywords))
            ]));

        bool hideDefaultCards = _settings.ShowDisabledPlaybackDevices
                                && (!_settings.ShowRecordingDevices || _settings.ShowDisabledRecordingDevices);
        stack.Children.Add(Maybe(!hideDefaultCards, PairBoolCard(
            Loc(nameof(AppStrings.Settings_Devices_ShowDefaultEvenIfDisabled_Title)),
            Loc(nameof(AppStrings.Settings_Devices_ShowDefaultEvenIfDisabled_Description)),
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
                Loc(nameof(AppStrings.Settings_Devices_ShowDefaultEvenIfDisabled_SearchKeywords))
            ])));
        stack.Children.Add(Maybe(!hideDefaultCards, PairBoolCard(
            Loc(nameof(AppStrings.Settings_Devices_ShowDefaultCommsEvenIfDisabled_Title)),
            Loc(nameof(AppStrings.Settings_Devices_ShowDefaultCommsEvenIfDisabled_Description)),
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
                Loc(nameof(AppStrings.Settings_Devices_ShowDefaultCommsEvenIfDisabled_SearchKeywords))
            ])));
        stack.Children.Add(PairBoolCard(
            Loc(nameof(AppStrings.Settings_Devices_ShowDisconnectedPlayback_Title)),
            Loc(nameof(AppStrings.Settings_Devices_ShowDisconnectedPlayback_Description)),
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
                Loc(nameof(AppStrings.Settings_Devices_ShowDisconnectedPlayback_SearchKeywords))
            ]));

        stack.Children.Add(PairColumnHeader(Loc(nameof(AppStrings.Settings_Devices_RowButtons_Header)), p));
        stack.Children.Add(PairBoolCard(Loc(nameof(AppStrings.Settings_Devices_ShowPlaybackLockButton_Title)),
            Loc(nameof(AppStrings.Settings_Devices_ShowPlaybackLockButton_Description)),
            playback, recording, _settings.ShowLockButtonForPlayback, v => _settings.ShowLockButtonForPlayback = v,
            _settings.ShowLockButtonForRecording, v => _settings.ShowLockButtonForRecording = v, p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Devices_ShowPlaybackLockButton_SearchKeywords))
            ]));
        stack.Children.Add(PairBoolCard(Loc(nameof(AppStrings.Settings_Devices_ShowPlaybackEqualizerAPOButton_Title)),
            Loc(nameof(AppStrings.Settings_Devices_ShowPlaybackEqualizerAPOButton_Description)),
            playback, recording, _settings.ShowEqualizerAPOButtonForPlayback,
            v => _settings.ShowEqualizerAPOButtonForPlayback = v, _settings.ShowEqualizerAPOButtonForRecording,
            v => _settings.ShowEqualizerAPOButtonForRecording = v, p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Devices_ShowPlaybackEqualizerAPOButton_SearchKeywords))
            ]));
        stack.Children.Add(PairBoolCard(Loc(nameof(AppStrings.Settings_Devices_ShowPlaybackDefaultDeviceButton_Title)),
            Loc(nameof(AppStrings.Settings_Devices_ShowPlaybackDefaultDeviceButton_Description)),
            playback, recording, _settings.ShowDefaultDeviceButtonForPlayback,
            v => _settings.ShowDefaultDeviceButtonForPlayback = v, _settings.ShowDefaultDeviceButtonForRecording,
            v => _settings.ShowDefaultDeviceButtonForRecording = v, p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Devices_ShowPlaybackDefaultDeviceButton_SearchKeywords))
            ]));
        stack.Children.Add(PairBoolCard(Loc(nameof(AppStrings.Settings_Devices_ShowPlaybackBatteryButton_Title)),
            Loc(nameof(AppStrings.Settings_Devices_ShowPlaybackBatteryButton_Description)),
            playback, recording, _settings.ShowBatteryButtonForPlayback,
            v => _settings.ShowBatteryButtonForPlayback = v, _settings.ShowBatteryButtonForRecording,
            v => _settings.ShowBatteryButtonForRecording = v, p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Devices_ShowPlaybackBatteryButton_SearchKeywords))
            ]));
        stack.Children.Add(PairBoolCard(Loc(nameof(AppStrings.Settings_Devices_ShowRecordingListenButton_Title)),
            Loc(nameof(AppStrings.Settings_Devices_ShowRecordingListenButton_Description)),
            playback, recording, leftValue: null, setLeft: null, _settings.ShowListenButtonForRecording,
            v => _settings.ShowListenButtonForRecording = v, p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Devices_ShowRecordingListenButton_SearchKeywords))
            ]));

        ControlNames.AssignLogicalSubtree(stack, nameof(VolumeSettingsPage.Devices));
        return stack;
    }
}
