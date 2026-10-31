using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Settings;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
{
    private StackPanel BuildGeneralPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_General_SectionHeader)), p);

        TrayAppDotNETGeneralSettingsSection commonSection =
            CreateGeneralSettingsSection(AppServices.Install, AppLocalizationCultures.Names);
        stack.Children.Add(commonSection.BuildLanguageCard());
        stack.Children.Add(commonSection.BuildStartupCard());
        AddStandardInstallationSection(stack, commonSection, AppServices.Install, _showUninstaller, includeStoreCard: true);
        AddRenderingSection(stack, supportsFlyoutWarmWindow: true, supportsTrayContextMenuWarmWindow: true);

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(L(nameof(AppStrings.Settings_General_Notifications_Header)), p));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_General_PlayDeviceVolumeChangeSound_Title)),
            L(nameof(AppStrings.Settings_General_PlayDeviceVolumeChangeSound_Description)),
            _settings.PlayDeviceVolumeChangeSound,
            v => _settings.PlayDeviceVolumeChangeSound = v,
            p,
            RefreshCurrentPage,
            [
                L(nameof(AppStrings.Settings_General_PlayDeviceVolumeChangeSound_SearchKeywords))
            ],
            node: ControlMap.Settings.GeneralPage.PlayDeviceVolumeChangeSound));
        stack.Children.Add(Maybe(_settings.PlayDeviceVolumeChangeSound, BoolCard(
            L(nameof(AppStrings.Settings_General_PlayTrayScrollVolumeChangeSound_Title)),
            L(nameof(AppStrings.Settings_General_PlayTrayScrollVolumeChangeSound_Description)),
            _settings.PlayTrayScrollVolumeChangeSound,
            v => _settings.PlayTrayScrollVolumeChangeSound = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_General_PlayTrayScrollVolumeChangeSound_SearchKeywords))
            ],
            node: ControlMap.Settings.GeneralPage.PlayTrayScrollVolumeChangeSound)));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_General_PlayAppVolumeChangeSound_Title)),
            L(nameof(AppStrings.Settings_General_PlayAppVolumeChangeSound_Description)),
            _settings.PlayAppVolumeChangeSound,
            v => _settings.PlayAppVolumeChangeSound = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_General_PlayAppVolumeChangeSound_SearchKeywords))
            ],
            node: ControlMap.Settings.GeneralPage.PlayAppVolumeChangeSound));
        stack.Children.Add(Maybe(_settings.PlayDeviceVolumeChangeSound, BoolCard(
            L(nameof(AppStrings.Settings_General_SuppressDeviceVolumeChangeSoundWhenAudioPlaying_Title)),
            L(nameof(AppStrings.Settings_General_SuppressDeviceVolumeChangeSoundWhenAudioPlaying_Description)),
            _settings.SuppressDeviceVolumeChangeSoundWhenAudioPlaying,
            v => _settings.SuppressDeviceVolumeChangeSoundWhenAudioPlaying = v,
            p,
            RefreshCurrentPage,
            [
                L(nameof(AppStrings.Settings_General_SuppressDeviceVolumeChangeSoundWhenAudioPlaying_SearchKeywords))
            ],
            node: ControlMap.Settings.GeneralPage.SuppressDeviceVolumeChangeSoundWhenAudioPlaying)));
        stack.Children.Add(Maybe(
            _settings is { PlayDeviceVolumeChangeSound: true, SuppressDeviceVolumeChangeSoundWhenAudioPlaying: true },
            IntCard(
                L(nameof(AppStrings.Settings_General_DingSuppressionPeakThreshold_Title)),
                L(nameof(AppStrings.Settings_General_DingSuppressionPeakThreshold_Description)),
                _settings.DingSuppressionPeakThresholdPercent,
                AppSettings.DingSuppressionPeakThresholdPercentMin,
                AppSettings.DingSuppressionPeakThresholdPercentMax,
                v => _settings.DingSuppressionPeakThresholdPercent = v,
                p,
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_General_DingSuppressionPeakThreshold_SearchKeywords))
                ],
                node: ControlMap.Settings.GeneralPage.DingSuppressionPeakThreshold)));

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(L(nameof(AppStrings.Settings_General_Other_Header)), p));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_General_LogarithmicVolumeScale_Title)),
            L(nameof(AppStrings.Settings_General_LogarithmicVolumeScale_Description)),
            _settings.UseLogarithmicVolumeScale,
            v => _settings.UseLogarithmicVolumeScale = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_General_LogarithmicVolumeScale_SearchKeywords))
            ],
            node: ControlMap.Settings.GeneralPage.LogarithmicVolumeScale));
        return stack;
    }
}
