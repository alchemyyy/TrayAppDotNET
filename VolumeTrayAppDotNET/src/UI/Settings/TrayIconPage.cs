using Avalonia.Controls;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
{
    private StackPanel BuildTrayIconPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc(nameof(AppStrings.Settings_TrayIcon_SectionHeader)), p);

        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_TrayIcon_MouseWheel_Title)),
            Loc(nameof(AppStrings.Settings_TrayIcon_MouseWheel_Description)),
            _settings.TrayScrollEnabled,
            v => _settings.TrayScrollEnabled = v,
            p,
            afterSave: RefreshCurrentPage));
        stack.Children.Add(Maybe(_settings.TrayScrollEnabled, IntCard(
            Loc(nameof(AppStrings.Settings_General_WheelVolumeStepPercent_Title)),
            Loc(nameof(AppStrings.Settings_General_WheelVolumeStepPercent_Description)),
            _settings.WheelVolumeStepPercent,
            AppSettings.WheelVolumeStepPercentMin,
            AppSettings.WheelVolumeStepPercentMax,
            v => _settings.WheelVolumeStepPercent = v,
            p,
            Loc(nameof(AppStrings.Common_PercentSuffix)))));
        stack.Children.Add(Maybe(_settings.TrayScrollEnabled, IntCard(
            Loc(nameof(AppStrings.Settings_General_WheelVolumeStepFinePercent_Title)),
            Loc(nameof(AppStrings.Settings_General_WheelVolumeStepFinePercent_Description)),
            _settings.WheelVolumeStepFinePercent,
            AppSettings.WheelVolumeStepFinePercentMin,
            AppSettings.WheelVolumeStepFinePercentMax,
            v => _settings.WheelVolumeStepFinePercent = v,
            p,
            Loc(nameof(AppStrings.Common_PercentSuffix)))));
        stack.Children.Add(Maybe(_settings.TrayScrollEnabled, IntCard(
            Loc(nameof(AppStrings.Settings_General_WheelVolumeStepCoarsePercent_Title)),
            Loc(nameof(AppStrings.Settings_General_WheelVolumeStepCoarsePercent_Description)),
            _settings.WheelVolumeStepCoarsePercent,
            AppSettings.WheelVolumeStepCoarsePercentMin,
            AppSettings.WheelVolumeStepCoarsePercentMax,
            v => _settings.WheelVolumeStepCoarsePercent = v,
            p,
            Loc(nameof(AppStrings.Common_PercentSuffix)))));
        stack.Children.Add(Maybe(_settings.TrayScrollEnabled, BoolCard(
            Loc(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadScroll_Title)),
            Loc(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadScroll_Description)),
            _settings.PrecisionTouchpadScrollEnabled,
            v => _settings.PrecisionTouchpadScrollEnabled = v,
            p,
            afterSave: RefreshCurrentPage)));
        stack.Children.Add(Maybe(_settings is { TrayScrollEnabled: true, PrecisionTouchpadScrollEnabled: true }, IntCard(
            Loc(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadUnitsPerScrollStep_Title)),
            Loc(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadUnitsPerScrollStep_Description)),
            _settings.PrecisionTouchpadUnitsPerScrollStep,
            AppSettings.PrecisionTouchpadUnitsPerScrollStepMin,
            AppSettings.PrecisionTouchpadUnitsPerScrollStepMax,
            v => _settings.PrecisionTouchpadUnitsPerScrollStep = v,
            p)));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc(nameof(AppStrings.Common_ContextMenu_Header)), p));
        stack.Children.Add(StringComboCard(
            Loc(nameof(AppStrings.Settings_TrayIcon_MenuPosition_Title)),
            Loc(nameof(AppStrings.Settings_TrayIcon_MenuPosition_Description)),
            [
                (ContextMenuPosition.Classic, Loc(nameof(AppStrings.Settings_TrayIcon_MenuPosition_Classic))),
                (ContextMenuPosition.Modern, Loc(nameof(AppStrings.Settings_TrayIcon_MenuPosition_Modern)))
            ],
            _settings.ContextMenuPosition,
            v => _settings.ContextMenuPosition = v,
            p));
        AddDeviceNameStyleCard(stack, Loc(nameof(AppStrings.Settings_TrayIcon_PlaybackDeviceName_Title)),
            Loc(nameof(AppStrings.Settings_TrayIcon_PlaybackDeviceName_Description)),
            _settings.TrayMenuPlaybackDeviceNameStyle, v => _settings.TrayMenuPlaybackDeviceNameStyle = v, p);
        AddDeviceNameStyleCard(stack, Loc(nameof(AppStrings.Settings_TrayIcon_RecordingDeviceName_Title)),
            Loc(nameof(AppStrings.Settings_TrayIcon_RecordingDeviceName_Description)),
            _settings.TrayMenuRecordingDeviceNameStyle, v => _settings.TrayMenuRecordingDeviceNameStyle = v, p);
        stack.Children.Add(IntCard(
            Loc(nameof(AppStrings.Settings_TrayIcon_DeviceNameMaxLength_Title)),
            Loc(nameof(AppStrings.Settings_TrayIcon_DeviceNameMaxLength_Description)),
            _settings.TrayMenuDeviceNameMaxLength,
            AppSettings.TrayMenuDeviceNameMaxLengthMin,
            AppSettings.TrayMenuDeviceNameMaxLengthMax,
            v => _settings.TrayMenuDeviceNameMaxLength = v,
            p));
        stack.Children.Add(BoolCard(Loc(nameof(AppStrings.Settings_Devices_ShowTrayRecordingLink_Title)),
            Loc(nameof(AppStrings.Settings_Devices_ShowTrayRecordingLink_Description)),
            _settings.ShowTrayMenuRecordingLink, v => _settings.ShowTrayMenuRecordingLink = v, p));
        stack.Children.Add(BoolCard(Loc(nameof(AppStrings.Settings_Devices_ShowTraySoundsLink_Title)),
            Loc(nameof(AppStrings.Settings_Devices_ShowTraySoundsLink_Description)),
            _settings.ShowTrayMenuSoundsLink, v => _settings.ShowTrayMenuSoundsLink = v, p));
        stack.Children.Add(BoolCard(Loc(nameof(AppStrings.Settings_Devices_ShowTrayCommunicationsLink_Title)),
            Loc(nameof(AppStrings.Settings_Devices_ShowTrayCommunicationsLink_Description)),
            _settings.ShowTrayMenuCommunicationsLink, v => _settings.ShowTrayMenuCommunicationsLink = v, p));
        stack.Children.Add(BoolCard(Loc(nameof(AppStrings.Settings_Devices_ShowTrayDeviceLinks_Title)),
            Loc(nameof(AppStrings.Settings_Devices_ShowTrayDeviceLinks_Description)),
            _settings.ShowTrayMenuDeviceLinks, v => _settings.ShowTrayMenuDeviceLinks = v, p));

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(Loc(nameof(AppStrings.Settings_TrayIcon_ModifiedActions_Header)), p));
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            Loc(nameof(AppStrings.Settings_TrayIcon_ModifiedActions_Description)), p, new Avalonia.Thickness(0, 0, 0, 8)));
        AddTrayWheelActionCard(stack, Loc(nameof(AppStrings.Settings_TrayIcon_MouseWheel_Title)), _settings.TrayWheelAction,
            v => _settings.TrayWheelAction = v, p);
        AddTrayWheelActionCard(stack, Loc(nameof(AppStrings.Settings_TrayIcon_CtrlMouseWheel_Title)), _settings.TrayCtrlWheelAction,
            v => _settings.TrayCtrlWheelAction = v, p);
        AddTrayWheelActionCard(stack, Loc(nameof(AppStrings.Settings_TrayIcon_AltMouseWheel_Title)), _settings.TrayAltWheelAction,
            v => _settings.TrayAltWheelAction = v, p);
        AddTrayClickActionCard(stack, Loc(nameof(AppStrings.Settings_TrayIcon_CtrlLeftClick_Title)), _settings.TrayCtrlLeftClickAction,
            v => _settings.TrayCtrlLeftClickAction = v, p);
        AddTrayClickActionCard(stack, Loc(nameof(AppStrings.Settings_TrayIcon_AltLeftClick_Title)), _settings.TrayAltLeftClickAction,
            v => _settings.TrayAltLeftClickAction = v, p);
        AddTrayClickActionCard(stack, Loc(nameof(AppStrings.Settings_TrayIcon_CtrlRightClick_Title)), _settings.TrayCtrlRightClickAction,
            v => _settings.TrayCtrlRightClickAction = v, p);
        AddTrayClickActionCard(stack, Loc(nameof(AppStrings.Settings_TrayIcon_AltRightClick_Title)), _settings.TrayAltRightClickAction,
            v => _settings.TrayAltRightClickAction = v, p);
        AddTrayClickActionCard(stack, Loc(nameof(AppStrings.Settings_TrayIcon_DoubleLeftClick_Title)), _settings.TrayDoubleClickAction,
            v => _settings.TrayDoubleClickAction = v, p);
        AddTrayClickActionCard(stack, Loc(nameof(AppStrings.Settings_TrayIcon_CtrlDoubleLeftClick_Title)),
            _settings.TrayCtrlDoubleLeftClickAction, v => _settings.TrayCtrlDoubleLeftClickAction = v, p);
        AddTrayClickActionCard(stack, Loc(nameof(AppStrings.Settings_TrayIcon_AltDoubleLeftClick_Title)),
            _settings.TrayAltDoubleLeftClickAction, v => _settings.TrayAltDoubleLeftClickAction = v, p);

        return stack;
    }

    private void AddDeviceNameStyleCard(
        StackPanel stack,
        string title,
        string description,
        TrayMenuDeviceNameStyle selected,
        Action<TrayMenuDeviceNameStyle> set,
        SettingsPalette p) =>
        stack.Children.Add(StringComboCard(
            title,
            description,
            [
                (TrayMenuDeviceNameStyle.NameAndModel, Loc(nameof(AppStrings.Settings_TrayIcon_DeviceName_NameAndModel))),
                (TrayMenuDeviceNameStyle.Name, Loc(nameof(AppStrings.Settings_TrayIcon_DeviceName_Name))),
                (TrayMenuDeviceNameStyle.Model, Loc(nameof(AppStrings.Settings_TrayIcon_DeviceName_Model)))
            ],
            selected,
            set,
            p));

    private void AddTrayWheelActionCard(
        StackPanel stack,
        string title,
        TrayWheelVolumeStep selected,
        Action<TrayWheelVolumeStep> set,
        SettingsPalette p)
    {
        Border card = StringComboCard(
            title,
            string.Empty,
            [
                (TrayWheelVolumeStep.Nothing, Loc(nameof(AppStrings.Settings_TrayIcon_ClickAction_Nothing))),
                (TrayWheelVolumeStep.Default, Loc(nameof(AppStrings.Settings_General_WheelVolumeStepPercent_Title))),
                (TrayWheelVolumeStep.Fine, Loc(nameof(AppStrings.Settings_General_WheelVolumeStepFinePercent_Title))),
                (TrayWheelVolumeStep.Coarse, Loc(nameof(AppStrings.Settings_General_WheelVolumeStepCoarsePercent_Title)))
            ],
            selected,
            set,
            p);
        card.IsEnabled = _settings.TrayScrollEnabled;
        stack.Children.Add(card);
    }

    private void AddTrayClickActionCard(
        StackPanel stack,
        string title,
        TrayClickAction selected,
        Action<TrayClickAction> set,
        SettingsPalette p) =>
        stack.Children.Add(StringComboCard(
            title,
            string.Empty,
            [
                (TrayClickAction.Nothing, Loc(nameof(AppStrings.Settings_TrayIcon_ClickAction_Nothing)))
            ],
            selected,
            set,
            p));
}
