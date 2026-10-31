using Avalonia.Controls;
using TrayAppDotNETCommon.UI.ControlMapping;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
{
    private StackPanel BuildTrayIconPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_TrayIcon_SectionHeader)), p);

        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_TrayIcon_MouseWheel_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_MouseWheel_Description)),
            _settings.TrayScrollEnabled,
            v => _settings.TrayScrollEnabled = v,
            p,
            RefreshCurrentPage,
            [
                L(nameof(AppStrings.Settings_TrayIcon_MouseWheel_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.MouseWheel));
        stack.Children.Add(Maybe(_settings.TrayScrollEnabled, IntCard(
            L(nameof(AppStrings.Settings_General_WheelVolumeStepPercent_Title)),
            L(nameof(AppStrings.Settings_General_WheelVolumeStepPercent_Description)),
            _settings.WheelVolumeStepPercent,
            AppSettings.WheelVolumeStepPercentMin,
            AppSettings.WheelVolumeStepPercentMax,
            v => _settings.WheelVolumeStepPercent = v,
            p,
            L(nameof(CommonStrings.Common_PercentSuffix)),
            [
                L(nameof(AppStrings.Settings_General_WheelVolumeStepPercent_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.WheelVolumeStepPercent)));
        stack.Children.Add(Maybe(_settings.TrayScrollEnabled, IntCard(
            L(nameof(AppStrings.Settings_General_WheelVolumeStepFinePercent_Title)),
            L(nameof(AppStrings.Settings_General_WheelVolumeStepFinePercent_Description)),
            _settings.WheelVolumeStepFinePercent,
            AppSettings.WheelVolumeStepFinePercentMin,
            AppSettings.WheelVolumeStepFinePercentMax,
            v => _settings.WheelVolumeStepFinePercent = v,
            p,
            L(nameof(CommonStrings.Common_PercentSuffix)),
            [
                L(nameof(AppStrings.Settings_General_WheelVolumeStepFinePercent_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.WheelVolumeStepFinePercent)));
        stack.Children.Add(Maybe(_settings.TrayScrollEnabled, IntCard(
            L(nameof(AppStrings.Settings_General_WheelVolumeStepCoarsePercent_Title)),
            L(nameof(AppStrings.Settings_General_WheelVolumeStepCoarsePercent_Description)),
            _settings.WheelVolumeStepCoarsePercent,
            AppSettings.WheelVolumeStepCoarsePercentMin,
            AppSettings.WheelVolumeStepCoarsePercentMax,
            v => _settings.WheelVolumeStepCoarsePercent = v,
            p,
            L(nameof(CommonStrings.Common_PercentSuffix)),
            [
                L(nameof(AppStrings.Settings_General_WheelVolumeStepCoarsePercent_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.WheelVolumeStepCoarsePercent)));
        stack.Children.Add(Maybe(_settings.TrayScrollEnabled, BoolCard(
            L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadScroll_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadScroll_Description)),
            _settings.PrecisionTouchpadScrollEnabled,
            v => _settings.PrecisionTouchpadScrollEnabled = v,
            p,
            RefreshCurrentPage,
            [
                L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadScroll_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.PrecisionTouchpadScroll)));
        stack.Children.Add(Maybe(_settings is { TrayScrollEnabled: true, PrecisionTouchpadScrollEnabled: true },
            IntCard(
                L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadUnitsPerScrollStep_Title)),
                L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadUnitsPerScrollStep_Description)),
                _settings.PrecisionTouchpadUnitsPerScrollStep,
                AppSettings.PrecisionTouchpadUnitsPerScrollStepMin,
                AppSettings.PrecisionTouchpadUnitsPerScrollStepMax,
                v => _settings.PrecisionTouchpadUnitsPerScrollStep = v,
                p,
                L(nameof(CommonStrings.Common_PercentSuffix)),
                [
                    L(nameof(AppStrings.Settings_TrayIcon_PrecisionTouchpadUnitsPerScrollStep_SearchKeywords))
                ],
                node: ControlMap.Settings.TrayIconPage.PrecisionTouchpadUnitsPerScrollStep)));

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(L(nameof(AppStrings.Common_ContextMenu_Header)), p));
        stack.Children.Add(StringComboCard(
            L(nameof(CommonStrings.Settings_TrayIcon_MenuPosition_Title)),
            L(nameof(CommonStrings.Settings_TrayIcon_MenuPosition_Description)),
            [
                (TrayAppDotNETContextMenuPosition.Classic, L(nameof(CommonStrings.Settings_TrayIcon_MenuPosition_Classic))),
                (TrayAppDotNETContextMenuPosition.Modern, L(nameof(CommonStrings.Settings_TrayIcon_MenuPosition_Modern)))
            ],
            _settings.ContextMenuPosition,
            v => _settings.ContextMenuPosition = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_TrayIcon_MenuPosition_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.ContextMenuPosition));
        AddDeviceNameStyleCard(stack, L(nameof(AppStrings.Settings_TrayIcon_PlaybackDeviceName_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_PlaybackDeviceName_Description)),
            _settings.TrayMenuPlaybackDeviceNameStyle, v => _settings.TrayMenuPlaybackDeviceNameStyle = v, p,
            [
                L(nameof(AppStrings.Settings_TrayIcon_PlaybackDeviceName_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.PlaybackDeviceName);
        AddDeviceNameStyleCard(stack, L(nameof(AppStrings.Settings_TrayIcon_RecordingDeviceName_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_RecordingDeviceName_Description)),
            _settings.TrayMenuRecordingDeviceNameStyle, v => _settings.TrayMenuRecordingDeviceNameStyle = v, p,
            [
                L(nameof(AppStrings.Settings_TrayIcon_RecordingDeviceName_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.RecordingDeviceName);
        stack.Children.Add(IntCard(
            L(nameof(AppStrings.Settings_TrayIcon_DeviceNameMaxLength_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_DeviceNameMaxLength_Description)),
            _settings.TrayMenuDeviceNameMaxLength,
            AppSettings.TrayMenuDeviceNameMaxLengthMin,
            AppSettings.TrayMenuDeviceNameMaxLengthMax,
            v => _settings.TrayMenuDeviceNameMaxLength = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_TrayIcon_DeviceNameMaxLength_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.DeviceNameMaxLength));
        stack.Children.Add(BoolCard(L(nameof(AppStrings.Settings_Devices_ShowTrayRecordingLink_Title)),
            L(nameof(AppStrings.Settings_Devices_ShowTrayRecordingLink_Description)),
            _settings.ShowTrayMenuRecordingLink, v => _settings.ShowTrayMenuRecordingLink = v, p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Devices_ShowTrayRecordingLink_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.ShowRecordingLink));
        stack.Children.Add(BoolCard(L(nameof(AppStrings.Settings_Devices_ShowTraySoundsLink_Title)),
            L(nameof(AppStrings.Settings_Devices_ShowTraySoundsLink_Description)),
            _settings.ShowTrayMenuSoundsLink, v => _settings.ShowTrayMenuSoundsLink = v, p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Devices_ShowTraySoundsLink_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.ShowSoundsLink));
        stack.Children.Add(BoolCard(L(nameof(AppStrings.Settings_Devices_ShowTrayCommunicationsLink_Title)),
            L(nameof(AppStrings.Settings_Devices_ShowTrayCommunicationsLink_Description)),
            _settings.ShowTrayMenuCommunicationsLink, v => _settings.ShowTrayMenuCommunicationsLink = v, p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Devices_ShowTrayCommunicationsLink_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.ShowCommunicationsLink));
        stack.Children.Add(BoolCard(L(nameof(AppStrings.Settings_Devices_ShowTrayDeviceLinks_Title)),
            L(nameof(AppStrings.Settings_Devices_ShowTrayDeviceLinks_Description)),
            _settings.ShowTrayMenuDeviceLinks, v => _settings.ShowTrayMenuDeviceLinks = v, p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Devices_ShowTrayDeviceLinks_SearchKeywords))
            ],
            node: ControlMap.Settings.TrayIconPage.ShowDeviceLinks));

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(L(nameof(CommonStrings.Settings_TrayIcon_ModifiedActions_Header)),
                p));
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            L(nameof(AppStrings.Settings_TrayIcon_ModifiedActions_Description)), p,
            new Avalonia.Thickness(left: 0, top: 0, right: 0, bottom: 8)));
        AddTrayWheelActionCard(stack, L(nameof(AppStrings.Settings_TrayIcon_MouseWheel_Title)),
            _settings.TrayWheelAction,
            v => _settings.TrayWheelAction = v, p,
            [L(nameof(AppStrings.Settings_TrayIcon_ModifiedWheelActions_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.WheelAction);
        AddTrayWheelActionCard(stack, L(nameof(AppStrings.Settings_TrayIcon_CtrlMouseWheel_Title)),
            _settings.TrayCtrlWheelAction,
            v => _settings.TrayCtrlWheelAction = v, p,
            [L(nameof(AppStrings.Settings_TrayIcon_ModifiedWheelActions_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.CtrlWheelAction);
        AddTrayWheelActionCard(stack, L(nameof(AppStrings.Settings_TrayIcon_AltMouseWheel_Title)),
            _settings.TrayAltWheelAction,
            v => _settings.TrayAltWheelAction = v, p,
            [L(nameof(AppStrings.Settings_TrayIcon_ModifiedWheelActions_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.AltWheelAction);
        AddTrayClickActionCard(stack, L(nameof(CommonStrings.Settings_TrayIcon_CtrlLeftClick_Title)),
            _settings.TrayCtrlLeftClickAction,
            v => _settings.TrayCtrlLeftClickAction = v, p,
            [L(nameof(AppStrings.Settings_TrayIcon_ModifiedClickActions_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.CtrlLeftClickAction);
        AddTrayClickActionCard(stack, L(nameof(CommonStrings.Settings_TrayIcon_AltLeftClick_Title)),
            _settings.TrayAltLeftClickAction,
            v => _settings.TrayAltLeftClickAction = v, p,
            [L(nameof(AppStrings.Settings_TrayIcon_ModifiedClickActions_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.AltLeftClickAction);
        AddTrayClickActionCard(stack, L(nameof(CommonStrings.Settings_TrayIcon_CtrlRightClick_Title)),
            _settings.TrayCtrlRightClickAction,
            v => _settings.TrayCtrlRightClickAction = v, p,
            [L(nameof(AppStrings.Settings_TrayIcon_ModifiedClickActions_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.CtrlRightClickAction);
        AddTrayClickActionCard(stack, L(nameof(CommonStrings.Settings_TrayIcon_AltRightClick_Title)),
            _settings.TrayAltRightClickAction,
            v => _settings.TrayAltRightClickAction = v, p,
            [L(nameof(AppStrings.Settings_TrayIcon_ModifiedClickActions_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.AltRightClickAction);
        AddTrayClickActionCard(stack, L(nameof(CommonStrings.Settings_TrayIcon_DoubleLeftClick_Title)),
            _settings.TrayDoubleClickAction,
            v => _settings.TrayDoubleClickAction = v, p,
            [L(nameof(AppStrings.Settings_TrayIcon_ModifiedClickActions_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.DoubleLeftClickAction);
        AddTrayClickActionCard(stack, L(nameof(CommonStrings.Settings_TrayIcon_CtrlDoubleLeftClick_Title)),
            _settings.TrayCtrlDoubleLeftClickAction, v => _settings.TrayCtrlDoubleLeftClickAction = v, p,
            [L(nameof(AppStrings.Settings_TrayIcon_ModifiedClickActions_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.CtrlDoubleLeftClickAction);
        AddTrayClickActionCard(stack, L(nameof(CommonStrings.Settings_TrayIcon_AltDoubleLeftClick_Title)),
            _settings.TrayAltDoubleLeftClickAction, v => _settings.TrayAltDoubleLeftClickAction = v, p,
            [L(nameof(AppStrings.Settings_TrayIcon_ModifiedClickActions_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.AltDoubleLeftClickAction);

        return stack;
    }

    private void AddDeviceNameStyleCard(
        StackPanel stack,
        string title,
        string description,
        TrayMenuDeviceNameStyle selected,
        Action<TrayMenuDeviceNameStyle> set,
        SettingsPalette p,
        IReadOnlyList<string> searchKeywords,
        ControlMapNodeID node) =>
        stack.Children.Add(StringComboCard(
            title,
            description,
            [
                (TrayMenuDeviceNameStyle.NameAndModel,
                    L(nameof(AppStrings.Settings_TrayIcon_DeviceName_NameAndModel))),
                (TrayMenuDeviceNameStyle.Name, L(nameof(AppStrings.Settings_TrayIcon_DeviceName_Name))),
                (TrayMenuDeviceNameStyle.Model, L(nameof(AppStrings.Settings_TrayIcon_DeviceName_Model)))
            ],
            selected,
            set,
            p,
            searchKeywords: searchKeywords,
            node: node));

    private void AddTrayWheelActionCard(
        StackPanel stack,
        string title,
        TrayWheelVolumeStep selected,
        Action<TrayWheelVolumeStep> set,
        SettingsPalette p,
        IReadOnlyList<string> searchKeywords,
        ControlMapNodeID node)
    {
        Border card = StringComboCard(
            title,
            string.Empty,
            [
                (TrayWheelVolumeStep.Nothing, L(nameof(CommonStrings.Settings_TrayIcon_ClickAction_Nothing))),
                (TrayWheelVolumeStep.Default, L(nameof(AppStrings.Settings_General_WheelVolumeStepPercent_Title))),
                (TrayWheelVolumeStep.Fine, L(nameof(AppStrings.Settings_General_WheelVolumeStepFinePercent_Title))),
                (TrayWheelVolumeStep.Coarse,
                    L(nameof(AppStrings.Settings_General_WheelVolumeStepCoarsePercent_Title)))
            ],
            selected,
            set,
            p,
            searchKeywords: searchKeywords,
            node: node);
        card.IsEnabled = _settings.TrayScrollEnabled;
        stack.Children.Add(card);
    }

    private void AddTrayClickActionCard(
        StackPanel stack,
        string title,
        TrayClickAction selected,
        Action<TrayClickAction> set,
        SettingsPalette p,
        IReadOnlyList<string> searchKeywords,
        ControlMapNodeID node) =>
        stack.Children.Add(StringComboCard(
            title,
            string.Empty,
            [
                (TrayClickAction.Nothing, L(nameof(CommonStrings.Settings_TrayIcon_ClickAction_Nothing))),
                (TrayClickAction.OpenSettings, L(nameof(CommonStrings.Settings_TrayIcon_ClickAction_OpenSettings)))
            ],
            selected,
            set,
            p,
            searchKeywords: searchKeywords,
            node: node));
}
