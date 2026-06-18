using Avalonia.Controls;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
{
    private StackPanel BuildDevicesPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc("Settings_Devices_SectionHeader"), p);

        stack.Children.Add(BoolCard(
            Loc("Settings_Devices_SetDefaultCommsToDefault_Title"),
            Loc("Settings_Devices_SetDefaultCommsToDefault_Description"),
            _settings.SetDefaultCommsToDefault,
            v => _settings.SetDefaultCommsToDefault = v,
            p));
        stack.Children.Add(BoolCard(
            Loc("Settings_Devices_ShowNotPresent_Title"),
            Loc("Settings_Devices_ShowNotPresent_Description"),
            _settings.ShowNotPresentDevices,
            v => _settings.ShowNotPresentDevices = v,
            p));

        string playback = Loc("Settings_Common_Playback");
        string recording = Loc("Settings_Common_Recording");
        stack.Children.Add(PairColumnHeader(Loc("Settings_Devices_VisibilityColumn_Header"), p));
        stack.Children.Add(PairBoolCard(
            Loc("Settings_Devices_ShowRecording_Title"),
            Loc("Settings_Devices_ShowRecording_Description"),
            playback,
            recording,
            null,
            null,
            _settings.ShowRecordingDevices,
            v => _settings.ShowRecordingDevices = v,
            p,
            showRight: true,
            afterSave: RefreshCurrentPage));
        stack.Children.Add(PairBoolCard(
            Loc("Settings_Devices_ShowDisabled_Title"),
            Loc("Settings_Devices_ShowDisabled_Description"),
            playback,
            recording,
            _settings.ShowDisabledPlaybackDevices,
            v => _settings.ShowDisabledPlaybackDevices = v,
            _settings.ShowDisabledRecordingDevices,
            v => _settings.ShowDisabledRecordingDevices = v,
            p,
            showRight: _settings.ShowRecordingDevices,
            afterSave: RefreshCurrentPage));

        bool hideDefaultCards = _settings.ShowDisabledPlaybackDevices
                                && (!_settings.ShowRecordingDevices || _settings.ShowDisabledRecordingDevices);
        stack.Children.Add(Maybe(!hideDefaultCards, PairBoolCard(
            Loc("Settings_Devices_ShowDefaultEvenIfDisabled_Title"),
            Loc("Settings_Devices_ShowDefaultEvenIfDisabled_Description"),
            playback,
            recording,
            _settings.ShowDefaultPlaybackDeviceEvenIfDisabled,
            v => _settings.ShowDefaultPlaybackDeviceEvenIfDisabled = v,
            _settings.ShowDefaultRecordingDeviceEvenIfDisabled,
            v => _settings.ShowDefaultRecordingDeviceEvenIfDisabled = v,
            p,
            showLeft: !_settings.ShowDisabledPlaybackDevices,
            showRight: _settings.ShowRecordingDevices && !_settings.ShowDisabledRecordingDevices)));
        stack.Children.Add(Maybe(!hideDefaultCards, PairBoolCard(
            Loc("Settings_Devices_ShowDefaultCommsEvenIfDisabled_Title"),
            Loc("Settings_Devices_ShowDefaultCommsEvenIfDisabled_Description"),
            playback,
            recording,
            _settings.ShowDefaultCommsPlaybackDeviceEvenIfDisabled,
            v => _settings.ShowDefaultCommsPlaybackDeviceEvenIfDisabled = v,
            _settings.ShowDefaultCommsRecordingDeviceEvenIfDisabled,
            v => _settings.ShowDefaultCommsRecordingDeviceEvenIfDisabled = v,
            p,
            showLeft: !_settings.ShowDisabledPlaybackDevices,
            showRight: _settings.ShowRecordingDevices && !_settings.ShowDisabledRecordingDevices)));
        stack.Children.Add(PairBoolCard(
            Loc("Settings_Devices_ShowDisconnectedPlayback_Title"),
            Loc("Settings_Devices_ShowDisconnectedPlayback_Description"),
            playback,
            recording,
            _settings.ShowDisconnectedPlaybackDevices,
            v => _settings.ShowDisconnectedPlaybackDevices = v,
            _settings.ShowDisconnectedRecordingDevices,
            v => _settings.ShowDisconnectedRecordingDevices = v,
            p,
            showRight: _settings.ShowRecordingDevices));

        stack.Children.Add(PairColumnHeader(Loc("Settings_Devices_RowButtons_Header"), p));
        stack.Children.Add(PairBoolCard(Loc("Settings_Devices_ShowPlaybackLockButton_Title"),
            Loc("Settings_Devices_ShowPlaybackLockButton_Description"),
            playback, recording, _settings.ShowLockButtonForPlayback, v => _settings.ShowLockButtonForPlayback = v,
            _settings.ShowLockButtonForRecording, v => _settings.ShowLockButtonForRecording = v, p));
        stack.Children.Add(PairBoolCard(Loc("Settings_Devices_ShowPlaybackEqualizerAPOButton_Title"),
            Loc("Settings_Devices_ShowPlaybackEqualizerAPOButton_Description"),
            playback, recording, _settings.ShowEqualizerAPOButtonForPlayback,
            v => _settings.ShowEqualizerAPOButtonForPlayback = v, _settings.ShowEqualizerAPOButtonForRecording,
            v => _settings.ShowEqualizerAPOButtonForRecording = v, p));
        stack.Children.Add(PairBoolCard(Loc("Settings_Devices_ShowPlaybackDefaultDeviceButton_Title"),
            Loc("Settings_Devices_ShowPlaybackDefaultDeviceButton_Description"),
            playback, recording, _settings.ShowDefaultDeviceButtonForPlayback,
            v => _settings.ShowDefaultDeviceButtonForPlayback = v, _settings.ShowDefaultDeviceButtonForRecording,
            v => _settings.ShowDefaultDeviceButtonForRecording = v, p));
        stack.Children.Add(PairBoolCard(Loc("Settings_Devices_ShowPlaybackBatteryButton_Title"),
            Loc("Settings_Devices_ShowPlaybackBatteryButton_Description"),
            playback, recording, _settings.ShowBatteryButtonForPlayback,
            v => _settings.ShowBatteryButtonForPlayback = v, _settings.ShowBatteryButtonForRecording,
            v => _settings.ShowBatteryButtonForRecording = v, p));
        stack.Children.Add(PairBoolCard(Loc("Settings_Devices_ShowRecordingListenButton_Title"),
            Loc("Settings_Devices_ShowRecordingListenButton_Description"),
            playback, recording, null, null, _settings.ShowListenButtonForRecording,
            v => _settings.ShowListenButtonForRecording = v, p));

        return stack;
    }
}
