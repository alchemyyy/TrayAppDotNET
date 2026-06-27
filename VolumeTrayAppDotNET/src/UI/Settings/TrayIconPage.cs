using Avalonia.Controls;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
{
    private StackPanel BuildTrayIconPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc("Settings_TrayIcon_SectionHeader"), p);

        stack.Children.Add(BoolCard(
            Loc("Settings_TrayIcon_MouseWheel_Title"),
            Loc("Settings_TrayIcon_MouseWheel_Description"),
            _settings.TrayScrollEnabled,
            v => _settings.TrayScrollEnabled = v,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc("Common_ContextMenu_Header"), p));
        stack.Children.Add(StringComboCard(
            Loc("Settings_TrayIcon_MenuPosition_Title"),
            Loc("Settings_TrayIcon_MenuPosition_Description"),
            [
                (ContextMenuPosition.Classic, Loc("Settings_TrayIcon_MenuPosition_Classic")),
                (ContextMenuPosition.Modern, Loc("Settings_TrayIcon_MenuPosition_Modern")),
            ],
            _settings.ContextMenuPosition,
            v => _settings.ContextMenuPosition = v,
            p));
        AddDeviceNameStyleCard(stack, Loc("Settings_TrayIcon_PlaybackDeviceName_Title"),
            Loc("Settings_TrayIcon_PlaybackDeviceName_Description"),
            _settings.TrayMenuPlaybackDeviceNameStyle, v => _settings.TrayMenuPlaybackDeviceNameStyle = v, p);
        AddDeviceNameStyleCard(stack, Loc("Settings_TrayIcon_RecordingDeviceName_Title"),
            Loc("Settings_TrayIcon_RecordingDeviceName_Description"),
            _settings.TrayMenuRecordingDeviceNameStyle, v => _settings.TrayMenuRecordingDeviceNameStyle = v, p);
        stack.Children.Add(IntCard(
            Loc("Settings_TrayIcon_DeviceNameMaxLength_Title"),
            Loc("Settings_TrayIcon_DeviceNameMaxLength_Description"),
            _settings.TrayMenuDeviceNameMaxLength,
            AppSettings.TrayMenuDeviceNameMaxLengthMin,
            AppSettings.TrayMenuDeviceNameMaxLengthMax,
            v => _settings.TrayMenuDeviceNameMaxLength = v,
            p));
        stack.Children.Add(BoolCard(Loc("Settings_Devices_ShowTrayRecordingLink_Title"),
            Loc("Settings_Devices_ShowTrayRecordingLink_Description"),
            _settings.ShowTrayMenuRecordingLink, v => _settings.ShowTrayMenuRecordingLink = v, p));
        stack.Children.Add(BoolCard(Loc("Settings_Devices_ShowTraySoundsLink_Title"),
            Loc("Settings_Devices_ShowTraySoundsLink_Description"),
            _settings.ShowTrayMenuSoundsLink, v => _settings.ShowTrayMenuSoundsLink = v, p));
        stack.Children.Add(BoolCard(Loc("Settings_Devices_ShowTrayCommunicationsLink_Title"),
            Loc("Settings_Devices_ShowTrayCommunicationsLink_Description"),
            _settings.ShowTrayMenuCommunicationsLink, v => _settings.ShowTrayMenuCommunicationsLink = v, p));
        stack.Children.Add(BoolCard(Loc("Settings_Devices_ShowTrayDeviceLinks_Title"),
            Loc("Settings_Devices_ShowTrayDeviceLinks_Description"),
            _settings.ShowTrayMenuDeviceLinks, v => _settings.ShowTrayMenuDeviceLinks = v, p));

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(Loc("Settings_TrayIcon_ModifiedActions_Header"), p));
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            Loc("Settings_TrayIcon_ModifiedActions_Description"), p, new Avalonia.Thickness(0, 0, 0, 8)));
        AddTrayClickActionCard(stack, Loc("Settings_TrayIcon_CtrlLeftClick_Title"), _settings.TrayCtrlLeftClickAction,
            v => _settings.TrayCtrlLeftClickAction = v, p);
        AddTrayClickActionCard(stack, Loc("Settings_TrayIcon_AltLeftClick_Title"), _settings.TrayAltLeftClickAction,
            v => _settings.TrayAltLeftClickAction = v, p);
        AddTrayClickActionCard(stack, Loc("Settings_TrayIcon_CtrlRightClick_Title"), _settings.TrayCtrlRightClickAction,
            v => _settings.TrayCtrlRightClickAction = v, p);
        AddTrayClickActionCard(stack, Loc("Settings_TrayIcon_AltRightClick_Title"), _settings.TrayAltRightClickAction,
            v => _settings.TrayAltRightClickAction = v, p);
        AddTrayClickActionCard(stack, Loc("Settings_TrayIcon_DoubleLeftClick_Title"), _settings.TrayDoubleClickAction,
            v => _settings.TrayDoubleClickAction = v, p);
        AddTrayClickActionCard(stack, Loc("Settings_TrayIcon_CtrlDoubleLeftClick_Title"),
            _settings.TrayCtrlDoubleLeftClickAction, v => _settings.TrayCtrlDoubleLeftClickAction = v, p);
        AddTrayClickActionCard(stack, Loc("Settings_TrayIcon_AltDoubleLeftClick_Title"),
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
                (TrayMenuDeviceNameStyle.NameAndModel, Loc("Settings_TrayIcon_DeviceName_NameAndModel")),
                (TrayMenuDeviceNameStyle.Name, Loc("Settings_TrayIcon_DeviceName_Name")),
                (TrayMenuDeviceNameStyle.Model, Loc("Settings_TrayIcon_DeviceName_Model")),
            ],
            selected,
            set,
            p));

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
                (TrayClickAction.Nothing, Loc("Settings_TrayIcon_ClickAction_Nothing")),
            ],
            selected,
            set,
            p));
}
