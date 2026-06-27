using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using TrayAppDotNETCommon.UI.Settings;
using VolumeInstallScope = TrayAppDotNETCommon.InstallScope;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
{
    private StackPanel BuildGeneralPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc("Settings_General_SectionHeader"), p);

        TrayAppDotNETGeneralSettingsSection commonSection = CreateGeneralSettingsSection(p);
        stack.Children.Add(commonSection.BuildStartupCard());
        CreateKeepWarmSettingsSection(p).AddCards(stack);
        commonSection.AddInstallationSection(
            stack,
            [
                new TrayAppDotNETInstallCardOptions
                {
                    Scope = VolumeInstallScope.LocalAppData,
                    Title = Loc("Settings_General_LocalUser_Title"),
                    ExecutablePath = AppServices.InstallLayout.LocalAppDataInstallExecutable,
                    Elevated = false,
                    Install = static () => AppServices.Installation.InstallToLocalAppData(),
                    UninstallAsync = async refresh =>
                    {
                        VolumeUninstallerWindow uninstallerDialog = new(
                            AppServices.InstallLayout.LocalAppDataInstallDirectory,
                            VolumeInstallScope.LocalAppData);
                        await uninstallerDialog.ShowDialog(this);
                        HookPostUninstallRefresh(uninstallerDialog, refresh);
                    },
                },
                new TrayAppDotNETInstallCardOptions
                {
                    Scope = VolumeInstallScope.ProgramFiles,
                    Title = Loc("Settings_General_SystemWide_Title"),
                    ExecutablePath = AppServices.InstallLayout.ProgramFilesInstallExecutable,
                    Elevated = true,
                    Install = static () => AppServices.Installation.InstallSystemWide(),
                    UninstallAsync = async refresh =>
                    {
                        VolumeUninstallerWindow uninstallerDialog = new(
                            AppServices.InstallLayout.ProgramFilesInstallDirectory,
                            VolumeInstallScope.ProgramFiles);
                        await uninstallerDialog.ShowDialog(this);
                        HookPostUninstallRefresh(uninstallerDialog, refresh);
                    },
                },
            ],
            new TrayAppDotNETStoreInstallOptions(
                Loc("Settings_General_WindowsStore_Title"),
                StoreInstallDescription));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc("Settings_General_Notifications_Header"), p));
        stack.Children.Add(BoolCard(
            Loc("Settings_General_PlayDeviceVolumeChangeSound_Title"),
            Loc("Settings_General_PlayDeviceVolumeChangeSound_Description"),
            _settings.PlayDeviceVolumeChangeSound,
            v => _settings.PlayDeviceVolumeChangeSound = v,
            p,
            afterSave: RefreshCurrentPage));
        stack.Children.Add(Maybe(_settings.PlayDeviceVolumeChangeSound, BoolCard(
            Loc("Settings_General_PlayTrayScrollVolumeChangeSound_Title"),
            Loc("Settings_General_PlayTrayScrollVolumeChangeSound_Description"),
            _settings.PlayTrayScrollVolumeChangeSound,
            v => _settings.PlayTrayScrollVolumeChangeSound = v,
            p)));
        stack.Children.Add(BoolCard(
            Loc("Settings_General_PlayAppVolumeChangeSound_Title"),
            Loc("Settings_General_PlayAppVolumeChangeSound_Description"),
            _settings.PlayAppVolumeChangeSound,
            v => _settings.PlayAppVolumeChangeSound = v,
            p));
        stack.Children.Add(Maybe(_settings.PlayDeviceVolumeChangeSound, BoolCard(
            Loc("Settings_General_SuppressDeviceVolumeChangeSoundWhenAudioPlaying_Title"),
            Loc("Settings_General_SuppressDeviceVolumeChangeSoundWhenAudioPlaying_Description"),
            _settings.SuppressDeviceVolumeChangeSoundWhenAudioPlaying,
            v => _settings.SuppressDeviceVolumeChangeSoundWhenAudioPlaying = v,
            p,
            afterSave: RefreshCurrentPage)));
        stack.Children.Add(Maybe(
            _settings is { PlayDeviceVolumeChangeSound: true, SuppressDeviceVolumeChangeSoundWhenAudioPlaying: true },
            IntCard(
                Loc("Settings_General_DingSuppressionPeakThreshold_Title"),
                Loc("Settings_General_DingSuppressionPeakThreshold_Description"),
                _settings.DingSuppressionPeakThresholdPercent,
                AppSettings.DingSuppressionPeakThresholdPercentMin,
                AppSettings.DingSuppressionPeakThresholdPercentMax,
                v => _settings.DingSuppressionPeakThresholdPercent = v,
                p)));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc("Settings_General_Other_Header"), p));
        stack.Children.Add(BoolCard(
            Loc("Settings_General_LogarithmicVolumeScale_Title"),
            Loc("Settings_General_LogarithmicVolumeScale_Description"),
            _settings.UseLogarithmicVolumeScale,
            v => _settings.UseLogarithmicVolumeScale = v,
            p));
        stack.Children.Add(IntCard(
            Loc("Settings_General_WheelVolumeStepPercent_Title"),
            Loc("Settings_General_WheelVolumeStepPercent_Description"),
            _settings.WheelVolumeStepPercent,
            AppSettings.WheelVolumeStepPercentMin,
            AppSettings.WheelVolumeStepPercentMax,
            v => _settings.WheelVolumeStepPercent = v,
            p,
            Loc("Common_PercentSuffix")));

        return stack;
    }

    private TrayAppDotNETGeneralSettingsSection CreateGeneralSettingsSection(SettingsPalette p) =>
        new(new TrayAppDotNETGeneralSettingsSectionOptions
        {
            Palette = p,
            ButtonRadius = RadiusMedium,
            CardRadius = RadiusLarge,
            Localize = L,
            Save = Save,
            ConfirmAsync = ConfirmAsync,
            ShowMessage = ShowMessage,
            GetRunOnStartup = static () => AppServices.Startup.GetRunOnStartup(),
            SetRunOnStartup = enabled =>
            {
                AppServices.Startup.SetRunOnStartup(enabled);
                _settings.RunOnStartup = enabled;
            },
            GetCurrentStartupShortcutTarget = static () => AppServices.Startup.GetCurrentShortcutTarget(),
            RetargetStartupShortcut = static () => AppServices.Startup.RetargetShortcutIfPresent(),
            DetectInstallations = static () => AppServices.Installation.DetectAll(),
            CurrentBuildNumber = BuildInfo.BuildNumber,
        });

    private TrayAppDotNETKeepWarmSettingsSection CreateKeepWarmSettingsSection(SettingsPalette p) =>
        new(new TrayAppDotNETKeepWarmSettingsSectionOptions
        {
            Palette = p,
            CardRadius = RadiusLarge,
            Localize = L,
            Save = Save,
            Settings = _settings,
            SupportsFlyout = true,
            SupportsTrayContextMenu = true,
        });

    private static string StoreInstallDescription()
    {
        TrayAppDotNETInstallationInfo? info = AppServices.Installation.DetectAll()
            .FirstOrDefault(i => i.Scope == VolumeInstallScope.WindowsStore);
        return info?.Status == TrayAppDotNETInstallStatus.CurrentlyRunning
            ? Loc("Settings_General_StoreRunning")
            : Loc("Settings_General_StoreNotInstalled");
    }

    private void HookPostUninstallRefresh(VolumeUninstallerWindow uninstallerDialog, Action refreshAfterInstallChange)
    {
        if (!uninstallerDialog.ConfirmedUninstall) return;

        Process? uninstallProcess = uninstallerDialog.UninstallProcess;
        if (uninstallProcess == null) return;

        uninstallProcess.Exited += (_, _) => OnUninstallBatExited(uninstallProcess, refreshAfterInstallChange);
        if (uninstallProcess.HasExited)
            OnUninstallBatExited(uninstallProcess, refreshAfterInstallChange);
    }

    private void OnUninstallBatExited(Process bat, Action refreshAfterInstallChange)
    {
        int exitCode;
        try { exitCode = bat.ExitCode; }
        catch (Exception ex)
        {
            exitCode = -1;
            TADNLog.Log($"VolumeSettingsWindow.OnUninstallBatExited: {ex.Message}");
        }
        finally
        {
            bat.Dispose();
        }

        Dispatcher.UIThread.Post(async void () =>
        {
            refreshAfterInstallChange();
            if (exitCode != 0)
            {
                await ShowMessage(
                    Loc("Settings_General_UninstallIncomplete_Title"),
                    Loc("Settings_General_UninstallIncomplete_Message"));
            }
        });
    }
}
