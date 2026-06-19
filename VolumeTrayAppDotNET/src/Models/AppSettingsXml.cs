using System.Xml.Linq;
using static TrayAppDotNETCommon.Models.TrayAppDotNETSettingsXml;

namespace VolumeTrayAppDotNET.Models;

public partial class AppSettings
{
    private void SaveXml(Stream stream)
    {
        XDocument document = new(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("AppSettings",
                Bool(nameof(RunOnStartup), RunOnStartup),
                Bool(nameof(Autosave), Autosave),
                Bool(nameof(SetDefaultCommsToDefault), SetDefaultCommsToDefault),
                Bool(nameof(ShowDisabledPlaybackDevices), ShowDisabledPlaybackDevices),
                Bool(nameof(ShowDefaultPlaybackDeviceEvenIfDisabled), ShowDefaultPlaybackDeviceEvenIfDisabled),
                Bool(nameof(ShowDefaultCommsPlaybackDeviceEvenIfDisabled),
                    ShowDefaultCommsPlaybackDeviceEvenIfDisabled),
                Bool(nameof(ShowDisconnectedPlaybackDevices), ShowDisconnectedPlaybackDevices),
                Bool(nameof(ShowRecordingDevices), ShowRecordingDevices),
                Bool(nameof(ShowDisabledRecordingDevices), ShowDisabledRecordingDevices),
                Bool(nameof(ShowDefaultRecordingDeviceEvenIfDisabled), ShowDefaultRecordingDeviceEvenIfDisabled),
                Bool(nameof(ShowDefaultCommsRecordingDeviceEvenIfDisabled),
                    ShowDefaultCommsRecordingDeviceEvenIfDisabled),
                Bool(nameof(ShowDisconnectedRecordingDevices), ShowDisconnectedRecordingDevices),
                Bool(nameof(DefaultAppDrawerExpanded), DefaultAppDrawerExpanded),
                Text(nameof(LastKnownDefaultPlaybackDeviceID), LastKnownDefaultPlaybackDeviceID),
                Text(nameof(LastKnownDefaultCommsPlaybackDeviceID), LastKnownDefaultCommsPlaybackDeviceID),
                Text(nameof(LastKnownDefaultRecordingDeviceID), LastKnownDefaultRecordingDeviceID),
                Text(nameof(LastKnownDefaultCommsRecordingDeviceID), LastKnownDefaultCommsRecordingDeviceID),
                Bool(nameof(ShowNotPresentDevices), ShowNotPresentDevices),
                Bool(nameof(ShowTrayMenuRecordingLink), ShowTrayMenuRecordingLink),
                Bool(nameof(ShowTrayMenuSoundsLink), ShowTrayMenuSoundsLink),
                Bool(nameof(ShowTrayMenuCommunicationsLink), ShowTrayMenuCommunicationsLink),
                Bool(nameof(ShowTrayMenuDeviceLinks), ShowTrayMenuDeviceLinks),
                Bool(nameof(UseLogarithmicVolumeScale), UseLogarithmicVolumeScale),
                Bool(nameof(PlayDeviceVolumeChangeSound), PlayDeviceVolumeChangeSound),
                Bool(nameof(SuppressDeviceVolumeChangeSoundWhenAudioPlaying),
                    SuppressDeviceVolumeChangeSoundWhenAudioPlaying),
                Int(nameof(DingSuppressionPeakThresholdPercent), DingSuppressionPeakThresholdPercent),
                Bool(nameof(PlayAppVolumeChangeSound), PlayAppVolumeChangeSound),
                Enum(nameof(ContextMenuPosition), ContextMenuPosition),
                Int(nameof(ContextMenuFontSize), ContextMenuFontSize),
                Enum(nameof(TrayMenuPlaybackDeviceNameStyle), TrayMenuPlaybackDeviceNameStyle),
                Enum(nameof(TrayMenuRecordingDeviceNameStyle), TrayMenuRecordingDeviceNameStyle),
                Int(nameof(TrayMenuDeviceNameMaxLength), TrayMenuDeviceNameMaxLength),
                Enum(nameof(ThemeMode), ThemeMode),
                NullableThemeColorElement(nameof(TextColor), TextColor),
                NullableThemeColorElement(nameof(BackgroundColor), BackgroundColor),
                Enum(nameof(TrayIconStyle), TrayIconStyle),
                NullableThemeColorElement(nameof(TrayIconColor), TrayIconColor),
                Bool(nameof(EnableRoundedCorners), EnableRoundedCorners),
                Enum(nameof(AnimationMode), AnimationMode),
                Int(nameof(ToolTipShowDelayMs), ToolTipShowDelayMs),
                Bool(nameof(UnifiedPeakMeter), UnifiedPeakMeter),
                Int(nameof(UnifiedMeterLowChannelBiasMultiplier), UnifiedMeterLowChannelBiasMultiplier),
                Int(nameof(MeterPeakFps), MeterPeakFps),
                Int(nameof(MeterPeakSampleRate), MeterPeakSampleRate),
                Int(nameof(MeterPeakChangeCeiling), MeterPeakChangeCeiling),
                Int(nameof(IconRetryIntervalMs), IconRetryIntervalMs),
                Int("IconLruLimit", IconLRULimit),
                Text(nameof(MeterPeakColorHex), MeterPeakColorHex),
                Text(nameof(MeterPeakStereoColorHex), MeterPeakStereoColorHex),
                SliderThumbElement(SerializedSliderThumb),
                Bool(nameof(TrayScrollEnabled), TrayScrollEnabled),
                Int(nameof(WheelVolumeStepPercent), WheelVolumeStepPercent),
                Enum(nameof(TrayDoubleClickAction), TrayDoubleClickAction),
                Enum(nameof(TrayCtrlLeftClickAction), TrayCtrlLeftClickAction),
                Enum(nameof(TrayAltLeftClickAction), TrayAltLeftClickAction),
                Enum(nameof(TrayCtrlRightClickAction), TrayCtrlRightClickAction),
                Enum(nameof(TrayAltRightClickAction), TrayAltRightClickAction),
                Enum(nameof(TrayCtrlDoubleLeftClickAction), TrayCtrlDoubleLeftClickAction),
                Enum(nameof(TrayAltDoubleLeftClickAction), TrayAltDoubleLeftClickAction),
                Bool(nameof(AllowFlyoutUndock), AllowFlyoutUndock),
                Bool(nameof(ClampUndockedFlyoutToScreen), ClampUndockedFlyoutToScreen),
                Bool(nameof(RestoreFlyoutUndockedOnStartup), RestoreFlyoutUndockedOnStartup),
                Bool(nameof(FlyoutUndocked), FlyoutUndocked),
                Bool(nameof(FlyoutHasSavedPosition), FlyoutHasSavedPosition),
                Double(nameof(FlyoutLeft), FlyoutLeft),
                Double(nameof(FlyoutTop), FlyoutTop),
                Bool(nameof(FlyoutHeaderAtBottom), FlyoutHeaderAtBottom),
                Enum(nameof(SoundSettingsTarget), SoundSettingsTarget),
                Enum(nameof(FlyoutDeviceLayout), FlyoutDeviceLayout),
                Enum(nameof(FlyoutDeviceTitlePosition), FlyoutDeviceTitlePosition),
                Enum(nameof(FlyoutDeviceSort), FlyoutDeviceSort),
                Bool(nameof(ShowRecordingDevicesInFlyout), ShowRecordingDevicesInFlyout),
                Bool(nameof(IntermixRecordingWithPlaybackInFlyout), IntermixRecordingWithPlaybackInFlyout),
                Enum(nameof(FlyoutCommunicationsButtonVisibility), FlyoutCommunicationsButtonVisibility),
                Bool(nameof(ShowLockButtonForPlayback), ShowLockButtonForPlayback),
                Bool(nameof(ShowEqualizerAPOButtonForPlayback), ShowEqualizerAPOButtonForPlayback),
                Bool(nameof(ShowDefaultDeviceButtonForPlayback), ShowDefaultDeviceButtonForPlayback),
                Bool(nameof(ShowBatteryButtonForPlayback), ShowBatteryButtonForPlayback),
                Bool(nameof(ShowLockButtonForRecording), ShowLockButtonForRecording),
                Bool(nameof(ShowEqualizerAPOButtonForRecording), ShowEqualizerAPOButtonForRecording),
                Bool(nameof(ShowListenButtonForRecording), ShowListenButtonForRecording),
                Bool(nameof(ShowDefaultDeviceButtonForRecording), ShowDefaultDeviceButtonForRecording),
                Bool(nameof(ShowBatteryButtonForRecording), ShowBatteryButtonForRecording),
                Bool(nameof(ShowDeviceFormatText), ShowDeviceFormatText),
                Bool(nameof(ShowDeviceCodecText), ShowDeviceCodecText),
                Enum(nameof(CaptureActivityIndicator), CaptureActivityIndicator),
                Enum(nameof(RecordingAppDrawerDisplayType), RecordingAppDrawerDisplayType),
                Enum(nameof(AppDrawerIconsCenterMode), AppDrawerIconsCenterMode),
                Int(nameof(AppDrawerIconsCenterSoftMax), AppDrawerIconsCenterSoftMax),
                Int(nameof(AppDrawerIconScalePercent), AppDrawerIconScalePercent),
                Int(nameof(AppDrawerIconsPerRow), AppDrawerIconsPerRow),
                Enum(nameof(AppDrawerStackDirection), AppDrawerStackDirection),
                Int(nameof(PlaybackAppDrawerSlidersMaxApps), PlaybackAppDrawerSlidersMaxApps),
                Int(nameof(PlaybackAppDrawerIconsMaxRows), PlaybackAppDrawerIconsMaxRows),
                Int(nameof(RecordingAppDrawerSlidersMaxApps), RecordingAppDrawerSlidersMaxApps),
                Int(nameof(RecordingAppDrawerIconsMaxRows), RecordingAppDrawerIconsMaxRows),
                Bool(nameof(CheckForUpdatesEnabled), CheckForUpdatesEnabled),
                Bool(nameof(ShowUpdateNotificationsEnabled), ShowUpdateNotificationsEnabled),
                Bool(nameof(ShowUpdateButtonInFlyout), ShowUpdateButtonInFlyout),
                Int(nameof(UpdateCheckIntervalMs), UpdateCheckIntervalMs),
                Bool(nameof(KeepFlyoutWarm), KeepFlyoutWarm),
                Bool(nameof(KeepTrayContextMenuWarm), KeepTrayContextMenuWarm),
                HotkeysElement(Hotkeys)));

        SaveDocument(stream, document);
    }

    private static AppSettings LoadXml(Stream stream)
    {
        XElement root = LoadRoot(
            stream,
            "AppSettings",
            "Missing AppSettings root.",
            "Unexpected AppSettings root.");

        AppSettings settings = new() { SuppressChangeNotification = true };
        try
        {
            settings.RunOnStartup = ReadBool(root, nameof(RunOnStartup), settings.RunOnStartup);
            settings.Autosave = ReadBool(root, nameof(Autosave), settings.Autosave);
            settings.SetDefaultCommsToDefault =
                ReadBool(root, nameof(SetDefaultCommsToDefault), settings.SetDefaultCommsToDefault);
            settings.ShowDisabledPlaybackDevices = ReadBool(root, nameof(ShowDisabledPlaybackDevices),
                settings.ShowDisabledPlaybackDevices);
            settings.ShowDefaultPlaybackDeviceEvenIfDisabled = ReadBool(root,
                nameof(ShowDefaultPlaybackDeviceEvenIfDisabled), settings.ShowDefaultPlaybackDeviceEvenIfDisabled);
            settings.ShowDefaultCommsPlaybackDeviceEvenIfDisabled = ReadBool(root,
                nameof(ShowDefaultCommsPlaybackDeviceEvenIfDisabled),
                settings.ShowDefaultCommsPlaybackDeviceEvenIfDisabled);
            settings.ShowDisconnectedPlaybackDevices = ReadBool(root, nameof(ShowDisconnectedPlaybackDevices),
                settings.ShowDisconnectedPlaybackDevices);
            settings.ShowRecordingDevices = ReadBool(root, nameof(ShowRecordingDevices), settings.ShowRecordingDevices);
            settings.ShowDisabledRecordingDevices = ReadBool(root, nameof(ShowDisabledRecordingDevices),
                settings.ShowDisabledRecordingDevices);
            settings.ShowDefaultRecordingDeviceEvenIfDisabled = ReadBool(root,
                nameof(ShowDefaultRecordingDeviceEvenIfDisabled), settings.ShowDefaultRecordingDeviceEvenIfDisabled);
            settings.ShowDefaultCommsRecordingDeviceEvenIfDisabled = ReadBool(root,
                nameof(ShowDefaultCommsRecordingDeviceEvenIfDisabled),
                settings.ShowDefaultCommsRecordingDeviceEvenIfDisabled);
            settings.ShowDisconnectedRecordingDevices = ReadBool(root, nameof(ShowDisconnectedRecordingDevices),
                settings.ShowDisconnectedRecordingDevices);
            settings.DefaultAppDrawerExpanded =
                ReadBool(root, nameof(DefaultAppDrawerExpanded), settings.DefaultAppDrawerExpanded);
            settings.LastKnownDefaultPlaybackDeviceID = ReadNullableString(root,
                nameof(LastKnownDefaultPlaybackDeviceID), settings.LastKnownDefaultPlaybackDeviceID);
            settings.LastKnownDefaultCommsPlaybackDeviceID = ReadNullableString(root,
                nameof(LastKnownDefaultCommsPlaybackDeviceID), settings.LastKnownDefaultCommsPlaybackDeviceID);
            settings.LastKnownDefaultRecordingDeviceID = ReadNullableString(root,
                nameof(LastKnownDefaultRecordingDeviceID), settings.LastKnownDefaultRecordingDeviceID);
            settings.LastKnownDefaultCommsRecordingDeviceID = ReadNullableString(root,
                nameof(LastKnownDefaultCommsRecordingDeviceID), settings.LastKnownDefaultCommsRecordingDeviceID);
            settings.ShowNotPresentDevices =
                ReadBool(root, nameof(ShowNotPresentDevices), settings.ShowNotPresentDevices);
            settings.ShowTrayMenuRecordingLink = ReadBool(root, nameof(ShowTrayMenuRecordingLink),
                settings.ShowTrayMenuRecordingLink);
            settings.ShowTrayMenuSoundsLink =
                ReadBool(root, nameof(ShowTrayMenuSoundsLink), settings.ShowTrayMenuSoundsLink);
            settings.ShowTrayMenuCommunicationsLink = ReadBool(root, nameof(ShowTrayMenuCommunicationsLink),
                settings.ShowTrayMenuCommunicationsLink);
            settings.ShowTrayMenuDeviceLinks =
                ReadBool(root, nameof(ShowTrayMenuDeviceLinks), settings.ShowTrayMenuDeviceLinks);
            settings.UseLogarithmicVolumeScale = ReadBool(root, nameof(UseLogarithmicVolumeScale),
                settings.UseLogarithmicVolumeScale);
            settings.PlayDeviceVolumeChangeSound = ReadBool(root, nameof(PlayDeviceVolumeChangeSound),
                settings.PlayDeviceVolumeChangeSound);
            settings.SuppressDeviceVolumeChangeSoundWhenAudioPlaying = ReadBool(root,
                nameof(SuppressDeviceVolumeChangeSoundWhenAudioPlaying),
                settings.SuppressDeviceVolumeChangeSoundWhenAudioPlaying);
            settings.DingSuppressionPeakThresholdPercent = ReadInt(root, nameof(DingSuppressionPeakThresholdPercent),
                settings.DingSuppressionPeakThresholdPercent);
            settings.PlayAppVolumeChangeSound =
                ReadBool(root, nameof(PlayAppVolumeChangeSound), settings.PlayAppVolumeChangeSound);
            settings.ContextMenuPosition = ReadEnum(root, nameof(ContextMenuPosition), settings.ContextMenuPosition);
            settings.ContextMenuFontSize = ReadInt(root, nameof(ContextMenuFontSize), settings.ContextMenuFontSize);
            settings.TrayMenuPlaybackDeviceNameStyle = ReadEnum(root, nameof(TrayMenuPlaybackDeviceNameStyle),
                settings.TrayMenuPlaybackDeviceNameStyle);
            settings.TrayMenuRecordingDeviceNameStyle = ReadEnum(root, nameof(TrayMenuRecordingDeviceNameStyle),
                settings.TrayMenuRecordingDeviceNameStyle);
            settings.TrayMenuDeviceNameMaxLength = ReadInt(root, nameof(TrayMenuDeviceNameMaxLength),
                settings.TrayMenuDeviceNameMaxLength);
            settings.ThemeMode = ReadEnum(root, nameof(ThemeMode), settings.ThemeMode);
            settings.TextColor = ReadNullableThemeColor(root, nameof(TextColor), settings.TextColor);
            settings.BackgroundColor = ReadNullableThemeColor(root, nameof(BackgroundColor), settings.BackgroundColor);
            settings.TrayIconStyle = ReadEnum(root, nameof(TrayIconStyle), settings.TrayIconStyle);
            settings.TrayIconColor = ReadNullableThemeColor(root, nameof(TrayIconColor), settings.TrayIconColor);
            settings.EnableRoundedCorners = ReadBool(root, nameof(EnableRoundedCorners), settings.EnableRoundedCorners);
            settings.AnimationMode = ReadEnum(root, nameof(AnimationMode), settings.AnimationMode);
            settings.ToolTipShowDelayMs = ReadInt(root, nameof(ToolTipShowDelayMs), settings.ToolTipShowDelayMs);
            settings.UnifiedPeakMeter = ReadBool(root, nameof(UnifiedPeakMeter), settings.UnifiedPeakMeter);
            settings.UnifiedMeterLowChannelBiasMultiplier = ReadInt(root, nameof(UnifiedMeterLowChannelBiasMultiplier),
                settings.UnifiedMeterLowChannelBiasMultiplier);
            settings.MeterPeakFps = ReadInt(root, nameof(MeterPeakFps), settings.MeterPeakFps);
            settings.MeterPeakSampleRate = ReadInt(root, nameof(MeterPeakSampleRate), settings.MeterPeakSampleRate);
            settings.MeterPeakChangeCeiling =
                ReadInt(root, nameof(MeterPeakChangeCeiling), settings.MeterPeakChangeCeiling);
            settings.IconRetryIntervalMs = ReadInt(root, nameof(IconRetryIntervalMs), settings.IconRetryIntervalMs);
            settings.IconLRULimit = ReadInt(root, "IconLruLimit", settings.IconLRULimit);
            settings.MeterPeakColorHex = ReadString(root, nameof(MeterPeakColorHex), settings.MeterPeakColorHex);
            settings.MeterPeakStereoColorHex =
                ReadString(root, nameof(MeterPeakStereoColorHex), settings.MeterPeakStereoColorHex);
            settings.SerializedSliderThumb = ReadSliderThumb(root.Element("SliderThumb"));
            settings.TrayScrollEnabled = ReadBool(root, nameof(TrayScrollEnabled), settings.TrayScrollEnabled);
            settings.WheelVolumeStepPercent =
                ReadInt(root, nameof(WheelVolumeStepPercent), settings.WheelVolumeStepPercent);
            settings.TrayDoubleClickAction =
                ReadEnum(root, nameof(TrayDoubleClickAction), settings.TrayDoubleClickAction);
            settings.TrayCtrlLeftClickAction =
                ReadEnum(root, nameof(TrayCtrlLeftClickAction), settings.TrayCtrlLeftClickAction);
            settings.TrayAltLeftClickAction =
                ReadEnum(root, nameof(TrayAltLeftClickAction), settings.TrayAltLeftClickAction);
            settings.TrayCtrlRightClickAction =
                ReadEnum(root, nameof(TrayCtrlRightClickAction), settings.TrayCtrlRightClickAction);
            settings.TrayAltRightClickAction =
                ReadEnum(root, nameof(TrayAltRightClickAction), settings.TrayAltRightClickAction);
            settings.TrayCtrlDoubleLeftClickAction = ReadEnum(root, nameof(TrayCtrlDoubleLeftClickAction),
                settings.TrayCtrlDoubleLeftClickAction);
            settings.TrayAltDoubleLeftClickAction = ReadEnum(root, nameof(TrayAltDoubleLeftClickAction),
                settings.TrayAltDoubleLeftClickAction);
            settings.AllowFlyoutUndock = ReadBool(root, nameof(AllowFlyoutUndock), settings.AllowFlyoutUndock);
            settings.ClampUndockedFlyoutToScreen = ReadBool(root, nameof(ClampUndockedFlyoutToScreen),
                settings.ClampUndockedFlyoutToScreen);
            settings.RestoreFlyoutUndockedOnStartup = ReadBool(root, nameof(RestoreFlyoutUndockedOnStartup),
                settings.RestoreFlyoutUndockedOnStartup);
            settings.FlyoutUndocked = ReadBool(root, nameof(FlyoutUndocked), settings.FlyoutUndocked);
            settings.FlyoutHasSavedPosition =
                ReadBool(root, nameof(FlyoutHasSavedPosition), settings.FlyoutHasSavedPosition);
            settings.FlyoutLeft = ReadDouble(root, nameof(FlyoutLeft), settings.FlyoutLeft);
            settings.FlyoutTop = ReadDouble(root, nameof(FlyoutTop), settings.FlyoutTop);
            settings.FlyoutHeaderAtBottom = ReadBool(root, nameof(FlyoutHeaderAtBottom), settings.FlyoutHeaderAtBottom);
            settings.SoundSettingsTarget = ReadEnum(root, nameof(SoundSettingsTarget), settings.SoundSettingsTarget);
            settings.FlyoutDeviceLayout = ReadEnum(root, nameof(FlyoutDeviceLayout), settings.FlyoutDeviceLayout);
            settings.FlyoutDeviceTitlePosition = ReadEnum(root, nameof(FlyoutDeviceTitlePosition),
                settings.FlyoutDeviceTitlePosition);
            settings.FlyoutDeviceSort = ReadEnum(root, nameof(FlyoutDeviceSort), settings.FlyoutDeviceSort);
            settings.ShowRecordingDevicesInFlyout = ReadBool(root, nameof(ShowRecordingDevicesInFlyout),
                settings.ShowRecordingDevicesInFlyout);
            settings.IntermixRecordingWithPlaybackInFlyout = ReadBool(root,
                nameof(IntermixRecordingWithPlaybackInFlyout), settings.IntermixRecordingWithPlaybackInFlyout);
            settings.FlyoutCommunicationsButtonVisibility = ReadEnum(root, nameof(FlyoutCommunicationsButtonVisibility),
                settings.FlyoutCommunicationsButtonVisibility);
            settings.ShowLockButtonForPlayback = ReadBool(root, nameof(ShowLockButtonForPlayback),
                settings.ShowLockButtonForPlayback);
            settings.ShowEqualizerAPOButtonForPlayback = ReadBool(root, nameof(ShowEqualizerAPOButtonForPlayback),
                settings.ShowEqualizerAPOButtonForPlayback);
            settings.ShowDefaultDeviceButtonForPlayback = ReadBool(root, nameof(ShowDefaultDeviceButtonForPlayback),
                settings.ShowDefaultDeviceButtonForPlayback);
            settings.ShowBatteryButtonForPlayback = ReadBool(root, nameof(ShowBatteryButtonForPlayback),
                settings.ShowBatteryButtonForPlayback);
            settings.ShowLockButtonForRecording = ReadBool(root, nameof(ShowLockButtonForRecording),
                settings.ShowLockButtonForRecording);
            settings.ShowEqualizerAPOButtonForRecording = ReadBool(root, nameof(ShowEqualizerAPOButtonForRecording),
                settings.ShowEqualizerAPOButtonForRecording);
            settings.ShowListenButtonForRecording = ReadBool(root, nameof(ShowListenButtonForRecording),
                settings.ShowListenButtonForRecording);
            settings.ShowDefaultDeviceButtonForRecording = ReadBool(root, nameof(ShowDefaultDeviceButtonForRecording),
                settings.ShowDefaultDeviceButtonForRecording);
            settings.ShowBatteryButtonForRecording = ReadBool(root, nameof(ShowBatteryButtonForRecording),
                settings.ShowBatteryButtonForRecording);
            settings.ShowDeviceFormatText = ReadBool(root, nameof(ShowDeviceFormatText), settings.ShowDeviceFormatText);
            settings.ShowDeviceCodecText = ReadBool(root, nameof(ShowDeviceCodecText), settings.ShowDeviceCodecText);
            settings.CaptureActivityIndicator =
                ReadEnum(root, nameof(CaptureActivityIndicator), settings.CaptureActivityIndicator);
            settings.RecordingAppDrawerDisplayType = ReadEnum(root, nameof(RecordingAppDrawerDisplayType),
                settings.RecordingAppDrawerDisplayType);
            settings.AppDrawerIconsCenterMode =
                ReadEnum(root, nameof(AppDrawerIconsCenterMode), settings.AppDrawerIconsCenterMode);
            settings.AppDrawerIconsCenterSoftMax = ReadInt(root, nameof(AppDrawerIconsCenterSoftMax),
                settings.AppDrawerIconsCenterSoftMax);
            settings.AppDrawerIconScalePercent = ReadInt(root, nameof(AppDrawerIconScalePercent),
                settings.AppDrawerIconScalePercent);
            settings.AppDrawerIconsPerRow = ReadInt(root, nameof(AppDrawerIconsPerRow), settings.AppDrawerIconsPerRow);
            settings.AppDrawerStackDirection =
                ReadEnum(root, nameof(AppDrawerStackDirection), settings.AppDrawerStackDirection);
            settings.PlaybackAppDrawerSlidersMaxApps = ReadInt(root, nameof(PlaybackAppDrawerSlidersMaxApps),
                settings.PlaybackAppDrawerSlidersMaxApps);
            settings.PlaybackAppDrawerIconsMaxRows = ReadInt(root, nameof(PlaybackAppDrawerIconsMaxRows),
                settings.PlaybackAppDrawerIconsMaxRows);
            settings.RecordingAppDrawerSlidersMaxApps = ReadInt(root, nameof(RecordingAppDrawerSlidersMaxApps),
                settings.RecordingAppDrawerSlidersMaxApps);
            settings.RecordingAppDrawerIconsMaxRows = ReadInt(root, nameof(RecordingAppDrawerIconsMaxRows),
                settings.RecordingAppDrawerIconsMaxRows);
            settings.CheckForUpdatesEnabled =
                ReadBool(root, nameof(CheckForUpdatesEnabled), settings.CheckForUpdatesEnabled);
            settings.ShowUpdateNotificationsEnabled = ReadBool(root, nameof(ShowUpdateNotificationsEnabled),
                settings.ShowUpdateNotificationsEnabled);
            settings.ShowUpdateButtonInFlyout =
                ReadBool(root, nameof(ShowUpdateButtonInFlyout), settings.ShowUpdateButtonInFlyout);
            settings.UpdateCheckIntervalMs =
                ReadInt(root, nameof(UpdateCheckIntervalMs), settings.UpdateCheckIntervalMs);
            settings.KeepFlyoutWarm = ReadBool(root, nameof(KeepFlyoutWarm), settings.KeepFlyoutWarm);
            settings.KeepTrayContextMenuWarm =
                ReadBool(root, nameof(KeepTrayContextMenuWarm), settings.KeepTrayContextMenuWarm);
            settings.Hotkeys = ReadHotkeys(root.Element("Hotkeys"));
        }
        finally
        {
            settings.SuppressChangeNotification = false;
        }

        settings.WireColorCallbacks();
        settings.InitializeSliderThumbCatalog();
        return settings;
    }
}
