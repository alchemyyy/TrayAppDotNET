using Avalonia.Controls;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
{
    private StackPanel BuildDeviceAppDrawersPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(AppStrings.Settings_DeviceAppDrawers_SectionHeader)), p);

        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_DeviceAppDrawers_DefaultAppDrawerExpanded_Title)),
            L(nameof(AppStrings.Settings_DeviceAppDrawers_DefaultAppDrawerExpanded_Description)),
            _settings.DefaultAppDrawerExpanded,
            v => _settings.DefaultAppDrawerExpanded = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_DeviceAppDrawers_DefaultAppDrawerExpanded_SearchKeywords))
            ],
            node: ControlMap.Settings.DeviceAppDrawersPage.DefaultAppDrawerExpanded));
        stack.Children.Add(IntCard(
            L(nameof(AppStrings.Settings_General_IconRetryInterval_Title)),
            L(nameof(AppStrings.Settings_General_IconRetryInterval_Description)),
            _settings.IconRetryIntervalMs,
            TimeConstants.IconRetryIntervalMsMin,
            TimeConstants.IconRetryIntervalMsMax,
            v => _settings.IconRetryIntervalMs = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_General_IconRetryInterval_SearchKeywords))
            ],
            node: ControlMap.Settings.DeviceAppDrawersPage.IconRetryInterval));

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(L(nameof(AppStrings.Settings_Flyout_PlaybackDrawer_Header)), p));
        stack.Children.Add(IntCard(
            L(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Sliders_Title)),
            L(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Sliders_Description)),
            _settings.PlaybackAppDrawerSlidersMaxApps,
            AppSettings.AppDrawerSlidersMaxAppsMin,
            AppSettings.AppDrawerSlidersMaxAppsMax,
            v => _settings.PlaybackAppDrawerSlidersMaxApps = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Sliders_SearchKeywords))
            ],
            node: ControlMap.Settings.DeviceAppDrawersPage.PlaybackSlidersMaxApps));

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(L(nameof(AppStrings.Settings_Flyout_RecordingDrawer_Header)),
                p));
        stack.Children.Add(StringComboCard(
            L(nameof(AppStrings.Settings_Flyout_RecordingAppDrawerDisplayType_Title)),
            L(nameof(AppStrings.Settings_Flyout_RecordingAppDrawerDisplayType_Description)),
            [
                (AppDrawerDisplayType.Icons,
                    L(nameof(AppStrings.Settings_Flyout_RecordingAppDrawerDisplayType_Icons))),
                (AppDrawerDisplayType.Sliders,
                    L(nameof(AppStrings.Settings_Flyout_RecordingAppDrawerDisplayType_Sliders)))
            ],
            _settings.RecordingAppDrawerDisplayType,
            v => _settings.RecordingAppDrawerDisplayType = v,
            p,
            RefreshCurrentPage,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_RecordingAppDrawerDisplayType_SearchKeywords))
            ],
            node: ControlMap.Settings.DeviceAppDrawersPage.RecordingDisplayType));
        stack.Children.Add(Maybe(_settings.RecordingAppDrawerDisplayType == AppDrawerDisplayType.Sliders, IntCard(
            L(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Sliders_Title)),
            L(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Sliders_Description)),
            _settings.RecordingAppDrawerSlidersMaxApps,
            AppSettings.AppDrawerSlidersMaxAppsMin,
            AppSettings.AppDrawerSlidersMaxAppsMax,
            v => _settings.RecordingAppDrawerSlidersMaxApps = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Sliders_SearchKeywords))
            ],
            node: ControlMap.Settings.DeviceAppDrawersPage.RecordingSlidersMaxApps)));
        stack.Children.Add(Maybe(_settings.RecordingAppDrawerDisplayType == AppDrawerDisplayType.Icons, IntCard(
            L(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Icons_Title)),
            L(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Icons_Description)),
            _settings.RecordingAppDrawerIconsMaxRows,
            AppSettings.AppDrawerIconsMaxRowsMin,
            AppSettings.AppDrawerIconsMaxRowsMax,
            v => _settings.RecordingAppDrawerIconsMaxRows = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Icons_SearchKeywords))
            ],
            node: ControlMap.Settings.DeviceAppDrawersPage.RecordingIconsMaxRows)));
        stack.Children.Add(StringComboCard(
            L(nameof(AppStrings.Settings_General_CaptureActivityIndicator_Title)),
            L(nameof(AppStrings.Settings_General_CaptureActivityIndicator_Description)),
            [
                (CaptureActivityIndicator.DimInactive,
                    L(nameof(AppStrings.Settings_General_CaptureActivityIndicator_DimInactive))),
                (CaptureActivityIndicator.ActiveGlyph,
                    L(nameof(AppStrings.Settings_General_CaptureActivityIndicator_ActiveGlyph))),
                (CaptureActivityIndicator.HideInactive,
                    L(nameof(AppStrings.Settings_General_CaptureActivityIndicator_HideInactive))),
                (CaptureActivityIndicator.None, L(nameof(AppStrings.Settings_General_CaptureActivityIndicator_None)))
            ],
            _settings.CaptureActivityIndicator,
            v => _settings.CaptureActivityIndicator = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_General_CaptureActivityIndicator_SearchKeywords))
            ],
            node: ControlMap.Settings.DeviceAppDrawersPage.CaptureActivityIndicator));

        bool icons = _settings.RecordingAppDrawerDisplayType == AppDrawerDisplayType.Icons;
        stack.Children.Add(Maybe(icons, StringComboCard(
            L(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCentered_Title)),
            L(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCentered_Description)),
            [
                (AppDrawerIconsCenterMode.Off, L(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCenterMode_Off))),
                (AppDrawerIconsCenterMode.Centered,
                    L(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCenterMode_Centered))),
                (AppDrawerIconsCenterMode.CenteredSoftMax,
                    L(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCenterMode_SoftMax)))
            ],
            _settings.AppDrawerIconsCenterMode,
            v => _settings.AppDrawerIconsCenterMode = v,
            p,
            RefreshCurrentPage,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCentered_SearchKeywords))
            ],
            node: ControlMap.Settings.DeviceAppDrawersPage.IconsCenterMode)));
        stack.Children.Add(Maybe(_settings.AppDrawerIconsCenterMode == AppDrawerIconsCenterMode.CenteredSoftMax,
            IntCard(
                L(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCenterSoftMax_Title)),
                L(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCenterSoftMax_Description)),
                _settings.AppDrawerIconsCenterSoftMax,
                AppSettings.AppDrawerIconsCenterSoftMaxMin,
                AppSettings.AppDrawerIconsCenterSoftMaxMax,
                v => _settings.AppDrawerIconsCenterSoftMax = v,
                p,
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCenterSoftMax_SearchKeywords))
                ],
                node: ControlMap.Settings.DeviceAppDrawersPage.IconsCenterSoftMax)));
        stack.Children.Add(Maybe(icons, IntCard(
            L(nameof(AppStrings.Settings_Flyout_AppDrawerIconScale_Title)),
            L(nameof(AppStrings.Settings_Flyout_AppDrawerIconScale_Description)),
            _settings.AppDrawerIconScalePercent,
            AppSettings.AppDrawerIconScalePercentMin,
            AppSettings.AppDrawerIconScalePercentMax,
            v => _settings.AppDrawerIconScalePercent = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_AppDrawerIconScale_SearchKeywords))
            ],
            node: ControlMap.Settings.DeviceAppDrawersPage.IconScale)));
        stack.Children.Add(Maybe(icons, StringComboCard(
            L(nameof(AppStrings.Settings_Flyout_AppDrawerStackDirection_Title)),
            L(nameof(AppStrings.Settings_Flyout_AppDrawerStackDirection_Description)),
            [
                (AppDrawerStackDirection.Auto, L(nameof(AppStrings.Settings_Flyout_AppDrawerStackDirection_Auto))),
                (AppDrawerStackDirection.TopBottom,
                    L(nameof(AppStrings.Settings_Flyout_AppDrawerStackDirection_TopBottom))),
                (AppDrawerStackDirection.BottomTop,
                    L(nameof(AppStrings.Settings_Flyout_AppDrawerStackDirection_BottomTop))),
                (AppDrawerStackDirection.LeftRight,
                    L(nameof(AppStrings.Settings_Flyout_AppDrawerStackDirection_LeftRight))),
                (AppDrawerStackDirection.RightLeft,
                    L(nameof(AppStrings.Settings_Flyout_AppDrawerStackDirection_RightLeft)))
            ],
            _settings.AppDrawerStackDirection,
            v => _settings.AppDrawerStackDirection = v,
            p,
            RefreshCurrentPage,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_AppDrawerStackDirection_SearchKeywords))
            ],
            node: ControlMap.Settings.DeviceAppDrawersPage.IconStackDirection)));
        bool vertical =
            _settings.AppDrawerStackDirection is AppDrawerStackDirection.LeftRight or AppDrawerStackDirection.RightLeft;
        stack.Children.Add(Maybe(icons && !vertical, IntCard(
            L(nameof(AppStrings.Settings_Flyout_AppDrawerIconsPerRow_Title)),
            L(nameof(AppStrings.Settings_Flyout_AppDrawerIconsPerRow_Description)),
            _settings.AppDrawerIconsPerRow,
            AppSettings.AppDrawerIconsPerRowMin,
            AppSettings.AppDrawerIconsPerRowMax,
            v => _settings.AppDrawerIconsPerRow = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_AppDrawerIconsPerRow_SearchKeywords))
            ],
            node: ControlMap.Settings.DeviceAppDrawersPage.IconsPerRow)));
        stack.Children.Add(Maybe(icons && vertical, IntCard(
            L(nameof(AppStrings.Settings_Flyout_AppDrawerIconsPerColumn_Title)),
            L(nameof(AppStrings.Settings_Flyout_AppDrawerIconsPerColumn_Description)),
            _settings.AppDrawerIconsPerRow,
            AppSettings.AppDrawerIconsPerRowMin,
            AppSettings.AppDrawerIconsPerRowMax,
            v => _settings.AppDrawerIconsPerRow = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_AppDrawerIconsPerColumn_SearchKeywords))
            ],
            node: ControlMap.Settings.DeviceAppDrawersPage.IconsPerColumn)));

        return stack;
    }
}
