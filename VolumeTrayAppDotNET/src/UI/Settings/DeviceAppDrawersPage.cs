using Avalonia.Controls;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
{
    private StackPanel BuildDeviceAppDrawersPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc(nameof(AppStrings.Settings_DeviceAppDrawers_SectionHeader)), p);

        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_DeviceAppDrawers_DefaultAppDrawerExpanded_Title)),
            Loc(nameof(AppStrings.Settings_DeviceAppDrawers_DefaultAppDrawerExpanded_Description)),
            _settings.DefaultAppDrawerExpanded,
            v => _settings.DefaultAppDrawerExpanded = v,
            p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_DeviceAppDrawers_DefaultAppDrawerExpanded_SearchKeywords))
            ]));
        stack.Children.Add(IntCard(
            Loc(nameof(AppStrings.Settings_General_IconRetryInterval_Title)),
            Loc(nameof(AppStrings.Settings_General_IconRetryInterval_Description)),
            _settings.IconRetryIntervalMs,
            TimeConstants.IconRetryIntervalMsMin,
            TimeConstants.IconRetryIntervalMsMax,
            v => _settings.IconRetryIntervalMs = v,
            p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_General_IconRetryInterval_SearchKeywords))
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc(nameof(AppStrings.Settings_Flyout_PlaybackDrawer_Header)), p));
        stack.Children.Add(IntCard(
            Loc(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Sliders_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Sliders_Description)),
            _settings.PlaybackAppDrawerSlidersMaxApps,
            AppSettings.AppDrawerSlidersMaxAppsMin,
            AppSettings.AppDrawerSlidersMaxAppsMax,
            v => _settings.PlaybackAppDrawerSlidersMaxApps = v,
            p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Sliders_SearchKeywords))
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc(nameof(AppStrings.Settings_Flyout_RecordingDrawer_Header)), p));
        stack.Children.Add(StringComboCard(
            Loc(nameof(AppStrings.Settings_Flyout_RecordingAppDrawerDisplayType_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_RecordingAppDrawerDisplayType_Description)),
            [
                (AppDrawerDisplayType.Icons, Loc(nameof(AppStrings.Settings_Flyout_RecordingAppDrawerDisplayType_Icons))),
                (AppDrawerDisplayType.Sliders, Loc(nameof(AppStrings.Settings_Flyout_RecordingAppDrawerDisplayType_Sliders)))
            ],
            _settings.RecordingAppDrawerDisplayType,
            v => _settings.RecordingAppDrawerDisplayType = v,
            p,
            afterSave: RefreshCurrentPage,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Flyout_RecordingAppDrawerDisplayType_SearchKeywords))
            ]));
        stack.Children.Add(Maybe(_settings.RecordingAppDrawerDisplayType == AppDrawerDisplayType.Sliders, IntCard(
            Loc(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Sliders_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Sliders_Description)),
            _settings.RecordingAppDrawerSlidersMaxApps,
            AppSettings.AppDrawerSlidersMaxAppsMin,
            AppSettings.AppDrawerSlidersMaxAppsMax,
            v => _settings.RecordingAppDrawerSlidersMaxApps = v,
            p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Sliders_SearchKeywords))
            ])));
        stack.Children.Add(Maybe(_settings.RecordingAppDrawerDisplayType == AppDrawerDisplayType.Icons, IntCard(
            Loc(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Icons_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Icons_Description)),
            _settings.RecordingAppDrawerIconsMaxRows,
            AppSettings.AppDrawerIconsMaxRowsMin,
            AppSettings.AppDrawerIconsMaxRowsMax,
            v => _settings.RecordingAppDrawerIconsMaxRows = v,
            p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Flyout_AppDrawerMaxApps_Icons_SearchKeywords))
            ])));
        stack.Children.Add(StringComboCard(
            Loc(nameof(AppStrings.Settings_General_CaptureActivityIndicator_Title)),
            Loc(nameof(AppStrings.Settings_General_CaptureActivityIndicator_Description)),
            [
                (CaptureActivityIndicator.DimInactive, Loc(nameof(AppStrings.Settings_General_CaptureActivityIndicator_DimInactive))),
                (CaptureActivityIndicator.ActiveGlyph, Loc(nameof(AppStrings.Settings_General_CaptureActivityIndicator_ActiveGlyph))),
                (CaptureActivityIndicator.HideInactive, Loc(nameof(AppStrings.Settings_General_CaptureActivityIndicator_HideInactive))),
                (CaptureActivityIndicator.None, Loc(nameof(AppStrings.Settings_General_CaptureActivityIndicator_None)))
            ],
            _settings.CaptureActivityIndicator,
            v => _settings.CaptureActivityIndicator = v,
            p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_General_CaptureActivityIndicator_SearchKeywords))
            ]));

        bool icons = _settings.RecordingAppDrawerDisplayType == AppDrawerDisplayType.Icons;
        stack.Children.Add(Maybe(icons, StringComboCard(
            Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCentered_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCentered_Description)),
            [
                (AppDrawerIconsCenterMode.Off, Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCenterMode_Off))),
                (AppDrawerIconsCenterMode.Centered, Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCenterMode_Centered))),
                (AppDrawerIconsCenterMode.CenteredSoftMax, Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCenterMode_SoftMax)))
            ],
            _settings.AppDrawerIconsCenterMode,
            v => _settings.AppDrawerIconsCenterMode = v,
            p,
            afterSave: RefreshCurrentPage,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCentered_SearchKeywords))
            ])));
        stack.Children.Add(Maybe(_settings.AppDrawerIconsCenterMode == AppDrawerIconsCenterMode.CenteredSoftMax,
            IntCard(
                Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCenterSoftMax_Title)),
                Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCenterSoftMax_Description)),
                _settings.AppDrawerIconsCenterSoftMax,
                AppSettings.AppDrawerIconsCenterSoftMaxMin,
                AppSettings.AppDrawerIconsCenterSoftMaxMax,
                v => _settings.AppDrawerIconsCenterSoftMax = v,
                p,
                searchKeywords:
                [
                    Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconsCenterSoftMax_SearchKeywords))
                ])));
        stack.Children.Add(Maybe(icons, IntCard(
            Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconScale_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconScale_Description)),
            _settings.AppDrawerIconScalePercent,
            AppSettings.AppDrawerIconScalePercentMin,
            AppSettings.AppDrawerIconScalePercentMax,
            v => _settings.AppDrawerIconScalePercent = v,
            p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconScale_SearchKeywords))
            ])));
        stack.Children.Add(Maybe(icons, StringComboCard(
            Loc(nameof(AppStrings.Settings_Flyout_AppDrawerStackDirection_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_AppDrawerStackDirection_Description)),
            [
                (AppDrawerStackDirection.Auto, Loc(nameof(AppStrings.Settings_Flyout_AppDrawerStackDirection_Auto))),
                (AppDrawerStackDirection.TopBottom, Loc(nameof(AppStrings.Settings_Flyout_AppDrawerStackDirection_TopBottom))),
                (AppDrawerStackDirection.BottomTop, Loc(nameof(AppStrings.Settings_Flyout_AppDrawerStackDirection_BottomTop))),
                (AppDrawerStackDirection.LeftRight, Loc(nameof(AppStrings.Settings_Flyout_AppDrawerStackDirection_LeftRight))),
                (AppDrawerStackDirection.RightLeft, Loc(nameof(AppStrings.Settings_Flyout_AppDrawerStackDirection_RightLeft)))
            ],
            _settings.AppDrawerStackDirection,
            v => _settings.AppDrawerStackDirection = v,
            p,
            afterSave: RefreshCurrentPage,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Flyout_AppDrawerStackDirection_SearchKeywords))
            ])));
        bool vertical =
            _settings.AppDrawerStackDirection is AppDrawerStackDirection.LeftRight or AppDrawerStackDirection.RightLeft;
        stack.Children.Add(Maybe(icons && !vertical, IntCard(
            Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconsPerRow_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconsPerRow_Description)),
            _settings.AppDrawerIconsPerRow,
            AppSettings.AppDrawerIconsPerRowMin,
            AppSettings.AppDrawerIconsPerRowMax,
            v => _settings.AppDrawerIconsPerRow = v,
            p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconsPerRow_SearchKeywords))
            ])));
        stack.Children.Add(Maybe(icons && vertical, IntCard(
            Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconsPerColumn_Title)),
            Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconsPerColumn_Description)),
            _settings.AppDrawerIconsPerRow,
            AppSettings.AppDrawerIconsPerRowMin,
            AppSettings.AppDrawerIconsPerRowMax,
            v => _settings.AppDrawerIconsPerRow = v,
            p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Flyout_AppDrawerIconsPerColumn_SearchKeywords))
            ])));

        return stack;
    }
}
