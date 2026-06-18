using Avalonia.Controls;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
{
    private StackPanel BuildDeviceAppDrawersPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc("Settings_DeviceAppDrawers_SectionHeader"), p);

        stack.Children.Add(BoolCard(
            Loc("Settings_DeviceAppDrawers_DefaultAppDrawerExpanded_Title"),
            Loc("Settings_DeviceAppDrawers_DefaultAppDrawerExpanded_Description"),
            _settings.DefaultAppDrawerExpanded,
            v => _settings.DefaultAppDrawerExpanded = v,
            p));
        stack.Children.Add(IntCard(
            Loc("Settings_General_IconRetryInterval_Title"),
            Loc("Settings_General_IconRetryInterval_Description"),
            _settings.IconRetryIntervalMs,
            AppSettings.IconRetryIntervalMsMin,
            AppSettings.IconRetryIntervalMsMax,
            v => _settings.IconRetryIntervalMs = v,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc("Settings_Flyout_PlaybackDrawer_Header"), p));
        stack.Children.Add(IntCard(
            Loc("Settings_Flyout_AppDrawerMaxApps_Sliders_Title"),
            Loc("Settings_Flyout_AppDrawerMaxApps_Sliders_Description"),
            _settings.PlaybackAppDrawerSlidersMaxApps,
            AppSettings.AppDrawerSlidersMaxAppsMin,
            AppSettings.AppDrawerSlidersMaxAppsMax,
            v => _settings.PlaybackAppDrawerSlidersMaxApps = v,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc("Settings_Flyout_RecordingDrawer_Header"), p));
        stack.Children.Add(StringComboCard(
            Loc("Settings_Flyout_RecordingAppDrawerDisplayType_Title"),
            Loc("Settings_Flyout_RecordingAppDrawerDisplayType_Description"),
            [
                (AppDrawerDisplayType.Icons, Loc("Settings_Flyout_RecordingAppDrawerDisplayType_Icons")),
                (AppDrawerDisplayType.Sliders, Loc("Settings_Flyout_RecordingAppDrawerDisplayType_Sliders")),
            ],
            _settings.RecordingAppDrawerDisplayType,
            v => _settings.RecordingAppDrawerDisplayType = v,
            p,
            afterSave: RefreshCurrentPage));
        stack.Children.Add(Maybe(_settings.RecordingAppDrawerDisplayType == AppDrawerDisplayType.Sliders, IntCard(
            Loc("Settings_Flyout_AppDrawerMaxApps_Sliders_Title"),
            Loc("Settings_Flyout_AppDrawerMaxApps_Sliders_Description"),
            _settings.RecordingAppDrawerSlidersMaxApps,
            AppSettings.AppDrawerSlidersMaxAppsMin,
            AppSettings.AppDrawerSlidersMaxAppsMax,
            v => _settings.RecordingAppDrawerSlidersMaxApps = v,
            p)));
        stack.Children.Add(Maybe(_settings.RecordingAppDrawerDisplayType == AppDrawerDisplayType.Icons, IntCard(
            Loc("Settings_Flyout_AppDrawerMaxApps_Icons_Title"),
            Loc("Settings_Flyout_AppDrawerMaxApps_Icons_Description"),
            _settings.RecordingAppDrawerIconsMaxRows,
            AppSettings.AppDrawerIconsMaxRowsMin,
            AppSettings.AppDrawerIconsMaxRowsMax,
            v => _settings.RecordingAppDrawerIconsMaxRows = v,
            p)));
        stack.Children.Add(StringComboCard(
            Loc("Settings_General_CaptureActivityIndicator_Title"),
            Loc("Settings_General_CaptureActivityIndicator_Description"),
            [
                (CaptureActivityIndicator.DimInactive, Loc("Settings_General_CaptureActivityIndicator_DimInactive")),
                (CaptureActivityIndicator.ActiveGlyph, Loc("Settings_General_CaptureActivityIndicator_ActiveGlyph")),
                (CaptureActivityIndicator.HideInactive, Loc("Settings_General_CaptureActivityIndicator_HideInactive")),
                (CaptureActivityIndicator.None, Loc("Settings_General_CaptureActivityIndicator_None")),
            ],
            _settings.CaptureActivityIndicator,
            v => _settings.CaptureActivityIndicator = v,
            p));

        bool icons = _settings.RecordingAppDrawerDisplayType == AppDrawerDisplayType.Icons;
        stack.Children.Add(Maybe(icons, StringComboCard(
            Loc("Settings_Flyout_AppDrawerIconsCentered_Title"),
            Loc("Settings_Flyout_AppDrawerIconsCentered_Description"),
            [
                (AppDrawerIconsCenterMode.Off, Loc("Settings_Flyout_AppDrawerIconsCenterMode_Off")),
                (AppDrawerIconsCenterMode.Centered, Loc("Settings_Flyout_AppDrawerIconsCenterMode_Centered")),
                (AppDrawerIconsCenterMode.CenteredSoftMax, Loc("Settings_Flyout_AppDrawerIconsCenterMode_SoftMax")),
            ],
            _settings.AppDrawerIconsCenterMode,
            v => _settings.AppDrawerIconsCenterMode = v,
            p,
            afterSave: RefreshCurrentPage)));
        stack.Children.Add(Maybe(_settings.AppDrawerIconsCenterMode == AppDrawerIconsCenterMode.CenteredSoftMax,
            IntCard(
                Loc("Settings_Flyout_AppDrawerIconsCenterSoftMax_Title"),
                Loc("Settings_Flyout_AppDrawerIconsCenterSoftMax_Description"),
                _settings.AppDrawerIconsCenterSoftMax,
                AppSettings.AppDrawerIconsCenterSoftMaxMin,
                AppSettings.AppDrawerIconsCenterSoftMaxMax,
                v => _settings.AppDrawerIconsCenterSoftMax = v,
                p)));
        stack.Children.Add(Maybe(icons, IntCard(
            Loc("Settings_Flyout_AppDrawerIconScale_Title"),
            Loc("Settings_Flyout_AppDrawerIconScale_Description"),
            _settings.AppDrawerIconScalePercent,
            AppSettings.AppDrawerIconScalePercentMin,
            AppSettings.AppDrawerIconScalePercentMax,
            v => _settings.AppDrawerIconScalePercent = v,
            p)));
        stack.Children.Add(Maybe(icons, StringComboCard(
            Loc("Settings_Flyout_AppDrawerStackDirection_Title"),
            Loc("Settings_Flyout_AppDrawerStackDirection_Description"),
            [
                (AppDrawerStackDirection.Auto, Loc("Settings_Flyout_AppDrawerStackDirection_Auto")),
                (AppDrawerStackDirection.TopBottom, Loc("Settings_Flyout_AppDrawerStackDirection_TopBottom")),
                (AppDrawerStackDirection.BottomTop, Loc("Settings_Flyout_AppDrawerStackDirection_BottomTop")),
                (AppDrawerStackDirection.LeftRight, Loc("Settings_Flyout_AppDrawerStackDirection_LeftRight")),
                (AppDrawerStackDirection.RightLeft, Loc("Settings_Flyout_AppDrawerStackDirection_RightLeft")),
            ],
            _settings.AppDrawerStackDirection,
            v => _settings.AppDrawerStackDirection = v,
            p,
            afterSave: RefreshCurrentPage)));
        bool vertical =
            _settings.AppDrawerStackDirection is AppDrawerStackDirection.LeftRight or AppDrawerStackDirection.RightLeft;
        stack.Children.Add(Maybe(icons && !vertical, IntCard(
            Loc("Settings_Flyout_AppDrawerIconsPerRow_Title"),
            Loc("Settings_Flyout_AppDrawerIconsPerRow_Description"),
            _settings.AppDrawerIconsPerRow,
            AppSettings.AppDrawerIconsPerRowMin,
            AppSettings.AppDrawerIconsPerRowMax,
            v => _settings.AppDrawerIconsPerRow = v,
            p)));
        stack.Children.Add(Maybe(icons && vertical, IntCard(
            Loc("Settings_Flyout_AppDrawerIconsPerColumn_Title"),
            Loc("Settings_Flyout_AppDrawerIconsPerColumn_Description"),
            _settings.AppDrawerIconsPerRow,
            AppSettings.AppDrawerIconsPerRowMin,
            AppSettings.AppDrawerIconsPerRowMax,
            v => _settings.AppDrawerIconsPerRow = v,
            p)));

        return stack;
    }
}
