using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using TrayAppDotNETCommon.UI.Settings;
using VolumeInstallScope = TrayAppDotNETCommon.Models.InstallScope;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
{
    private StackPanel BuildGeneralPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc(nameof(AppStrings.Settings_General_SectionHeader)), p);

        TrayAppDotNETGeneralSettingsSection commonSection = CreateGeneralSettingsSection(p);
        stack.Children.Add(commonSection.BuildStartupCard());
        commonSection.AddInstallationSection(
            stack,
            [
                new TrayAppDotNETInstallCardOptions
                {
                    Scope = VolumeInstallScope.LocalAppData,
                    Title = Loc(nameof(AppStrings.Settings_General_LocalUser_Title)),
                    ExecutablePath = AppServices.InstallLayout.LocalAppDataInstallExecutable,
                    Elevated = false,
                    Install = static () => AppServices.Installation.InstallToLocalAppData(),
                    UninstallAsync = async _ =>
                    {
                        VolumeUninstallerWindow uninstallerDialog = new(
                            AppServices.InstallLayout.LocalAppDataInstallDirectory,
                            VolumeInstallScope.LocalAppData);
                        await uninstallerDialog.ShowDialog(this);
                        HookPostUninstallRefresh(uninstallerDialog);
                    }
                },
                new TrayAppDotNETInstallCardOptions
                {
                    Scope = VolumeInstallScope.ProgramFiles,
                    Title = Loc(nameof(AppStrings.Settings_General_SystemWide_Title)),
                    ExecutablePath = AppServices.InstallLayout.ProgramFilesInstallExecutable,
                    Elevated = true,
                    Install = static () => AppServices.Installation.InstallSystemWide(),
                    UninstallAsync = async _ =>
                    {
                        VolumeUninstallerWindow uninstallerDialog = new(
                            AppServices.InstallLayout.ProgramFilesInstallDirectory,
                            VolumeInstallScope.ProgramFiles);
                        await uninstallerDialog.ShowDialog(this);
                        HookPostUninstallRefresh(uninstallerDialog);
                    }
                }
            ],
            new TrayAppDotNETStoreInstallOptions(
                Loc(nameof(AppStrings.Settings_General_WindowsStore_Title)),
                StoreInstallDescription));
        CreateRenderingSettingsSection(p).AddCards(stack);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc(nameof(AppStrings.Settings_General_Notifications_Header)), p));
        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_General_PlayDeviceVolumeChangeSound_Title)),
            Loc(nameof(AppStrings.Settings_General_PlayDeviceVolumeChangeSound_Description)),
            _settings.PlayDeviceVolumeChangeSound,
            v => _settings.PlayDeviceVolumeChangeSound = v,
            p,
            afterSave: RefreshCurrentPage,
            searchKeywords:
            [
                L("Settings_General_PlayDeviceVolumeChangeSound_SearchKeywords",
                    "audio feedback speaker ding chime")
            ]));
        stack.Children.Add(Maybe(_settings.PlayDeviceVolumeChangeSound, BoolCard(
            Loc(nameof(AppStrings.Settings_General_PlayTrayScrollVolumeChangeSound_Title)),
            Loc(nameof(AppStrings.Settings_General_PlayTrayScrollVolumeChangeSound_Description)),
            _settings.PlayTrayScrollVolumeChangeSound,
            v => _settings.PlayTrayScrollVolumeChangeSound = v,
            p,
            searchKeywords:
            [
                L("Settings_General_PlayTrayScrollVolumeChangeSound_SearchKeywords",
                    "wheel audio feedback chime")
            ])));
        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_General_PlayAppVolumeChangeSound_Title)),
            Loc(nameof(AppStrings.Settings_General_PlayAppVolumeChangeSound_Description)),
            _settings.PlayAppVolumeChangeSound,
            v => _settings.PlayAppVolumeChangeSound = v,
            p,
            searchKeywords:
            [
                L("Settings_General_PlayAppVolumeChangeSound_SearchKeywords",
                    "mixer application audio feedback preview chime")
            ]));
        stack.Children.Add(Maybe(_settings.PlayDeviceVolumeChangeSound, BoolCard(
            Loc(nameof(AppStrings.Settings_General_SuppressDeviceVolumeChangeSoundWhenAudioPlaying_Title)),
            Loc(nameof(AppStrings.Settings_General_SuppressDeviceVolumeChangeSoundWhenAudioPlaying_Description)),
            _settings.SuppressDeviceVolumeChangeSoundWhenAudioPlaying,
            v => _settings.SuppressDeviceVolumeChangeSoundWhenAudioPlaying = v,
            p,
            afterSave: RefreshCurrentPage,
            searchKeywords:
            [
                L("Settings_General_SuppressDeviceVolumeChangeSoundWhenAudioPlaying_SearchKeywords",
                    "silence mute feedback while listening")
            ])));
        stack.Children.Add(Maybe(
            _settings is { PlayDeviceVolumeChangeSound: true, SuppressDeviceVolumeChangeSoundWhenAudioPlaying: true },
            IntCard(
                Loc(nameof(AppStrings.Settings_General_DingSuppressionPeakThreshold_Title)),
                Loc(nameof(AppStrings.Settings_General_DingSuppressionPeakThreshold_Description)),
                _settings.DingSuppressionPeakThresholdPercent,
                AppSettings.DingSuppressionPeakThresholdPercentMin,
                AppSettings.DingSuppressionPeakThresholdPercentMax,
                v => _settings.DingSuppressionPeakThresholdPercent = v,
                p,
                searchKeywords:
                [
                    L("Settings_General_DingSuppressionPeakThreshold_SearchKeywords",
                        "beep sensitivity cutoff level")
                ])));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc(nameof(AppStrings.Settings_General_Other_Header)), p));
        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_General_LogarithmicVolumeScale_Title)),
            Loc(nameof(AppStrings.Settings_General_LogarithmicVolumeScale_Description)),
            _settings.UseLogarithmicVolumeScale,
            v => _settings.UseLogarithmicVolumeScale = v,
            p,
            searchKeywords:
            [
                L("Settings_General_LogarithmicVolumeScale_SearchKeywords",
                    "audio taper natural slider curve")
            ]));
        stack.Children.Add(IntCard(
            Loc(nameof(AppStrings.Settings_General_WheelVolumeStepPercent_Title)),
            Loc(nameof(AppStrings.Settings_General_WheelVolumeStepPercent_Description)),
            _settings.WheelVolumeStepPercent,
            AppSettings.WheelVolumeStepPercentMin,
            AppSettings.WheelVolumeStepPercentMax,
            v => _settings.WheelVolumeStepPercent = v,
            p,
            Loc(nameof(AppStrings.Common_PercentSuffix)),
            searchKeywords:
            [
                L("Settings_General_WheelVolumeStepPercent_SearchKeywords",
                    "scroll sensitivity increment amount")
            ]));

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
            CurrentBuildNumber = BuildInfo.BuildNumber
        });

    private TrayAppDotNETRenderingSettingsSection CreateRenderingSettingsSection(SettingsPalette p) =>
        new(new TrayAppDotNETRenderingSettingsSectionOptions
        {
            Palette = p,
            CardRadius = RadiusLarge,
            Localize = L,
            Save = Save,
            ConfirmAsync = ConfirmAsync,
            ShowMessage = ShowMessage,
            RenderingSettings = _settings,
            WarmWindowSettings = _settings,
            SupportsFlyoutWarmWindow = true,
            SupportsTrayContextMenuWarmWindow = true
        });

    private static string StoreInstallDescription()
    {
        TrayAppDotNETInstallationInfo? info = AppServices.Installation.DetectAll()
            .FirstOrDefault(i => i.Scope == VolumeInstallScope.WindowsStore);
        return info?.Status == TrayAppDotNETInstallStatus.CurrentlyRunning
            ? Loc(nameof(AppStrings.Settings_General_StoreRunning))
            : Loc(nameof(AppStrings.Settings_General_StoreNotInstalled));
    }

    private void HookPostUninstallRefresh(VolumeUninstallerWindow uninstallerDialog)
    {
        if (!uninstallerDialog.ConfirmedUninstall) return;

        Process? uninstallProcess = uninstallerDialog.UninstallProcess;
        if (uninstallProcess == null) return;

        PostUninstallRefreshOwner owner = new(uninstallProcess, OnUninstallProcessCompleted);
        if (!TryOwnUninstallMonitor(owner))
        {
            owner.Dispose();
            return;
        }

        owner.Start();
    }

    private bool TryOwnUninstallMonitor(PostUninstallRefreshOwner owner)
    {
        lock (_uninstallMonitorGate)
        {
            return !_uninstallMonitoringDisposed && _uninstallMonitors.Add(owner);
        }
    }

    private void OnUninstallProcessCompleted(PostUninstallRefreshOwner owner, int exitCode)
    {
        lock (_uninstallMonitorGate)
        {
            if (!_uninstallMonitors.Remove(owner) || _uninstallMonitoringDisposed) return;
        }

        Dispatcher.UIThread.Post(
            () => _ = ApplyUninstallCompletionAsync(exitCode),
            DispatcherPriority.Background);
    }

    private async Task ApplyUninstallCompletionAsync(int exitCode)
    {
        lock (_uninstallMonitorGate)
        {
            if (_uninstallMonitoringDisposed) return;
        }

        if (IsClosing) return;

        try
        {
            AppServices.Startup.RetargetShortcutIfPresent();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Volume uninstall completion startup retarget failed: {exception.Message}");
        }

        if (CurrentPageKey == VolumeSettingsPage.General)
        {
            try
            {
                RefreshCurrentPage();
            }
            catch (Exception exception)
            {
                TADNLog.Log($"Volume uninstall completion refresh failed: {exception.Message}");
            }
        }

        if (exitCode == 0 || IsClosing) return;

        try
        {
            await ShowMessage(
                Loc(nameof(AppStrings.Settings_General_UninstallIncomplete_Title)),
                Loc(nameof(AppStrings.Settings_General_UninstallIncomplete_Message)));
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Volume uninstall completion message failed: {exception.Message}");
        }
    }

    private void DisposeUninstallMonitors()
    {
        List<PostUninstallRefreshOwner> owners = [];
        lock (_uninstallMonitorGate)
        {
            if (_uninstallMonitoringDisposed) return;
            _uninstallMonitoringDisposed = true;
            owners.AddRange(_uninstallMonitors);
            _uninstallMonitors.Clear();
        }

        for (int index = 0; index < owners.Count; index++)
        {
            try
            {
                owners[index].Dispose();
            }
            catch (Exception exception)
            {
                TADNLog.Log($"Volume uninstall monitor disposal failed: {exception.Message}");
            }
        }
    }

    /// <summary>Owns a transferred uninstall process until completion or settings-window close.</summary>
    private sealed class PostUninstallRefreshOwner(
        Process process,
        Action<PostUninstallRefreshOwner, int> completed) : IDisposable
    {
        private readonly Lock _gate = new();
        private Process? _process = process;
        private Action<PostUninstallRefreshOwner, int>? _completed = completed;
        private bool _started;
        private bool _finished;

        public void Start()
        {
            Process? process;
            lock (_gate)
            {
                if (_finished || _started) return;
                _started = true;
                process = _process;
            }

            if (process == null) return;

            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += OnProcessExited;
                if (process.HasExited) Complete(notify: true);
            }
            catch (Exception exception)
            {
                if (!IsFinished)
                    TADNLog.Log($"Volume uninstall process monitoring failed: {exception.Message}");
                Complete(notify: true);
            }
        }

        public void Dispose() => Complete(notify: false);

        private void OnProcessExited(object? sender, EventArgs e) => Complete(notify: true);

        private bool IsFinished
        {
            get
            {
                lock (_gate)
                    return _finished;
            }
        }

        private void Complete(bool notify)
        {
            Process? process;
            Action<PostUninstallRefreshOwner, int>? completed;
            lock (_gate)
            {
                if (_finished) return;
                _finished = true;
                process = _process;
                _process = null;
                completed = notify ? _completed : null;
                _completed = null;
            }

            int exitCode = -1;
            if (process != null)
            {
                try { process.Exited -= OnProcessExited; }
                catch (Exception exception)
                {
                    TADNLog.Log($"Volume uninstall process event detachment failed: {exception.Message}");
                }

                if (notify)
                {
                    try { exitCode = process.ExitCode; }
                    catch (Exception exception)
                    {
                        TADNLog.Log($"Volume uninstall process exit read failed: {exception.Message}");
                    }
                }

                try { process.Dispose(); }
                catch (Exception exception)
                {
                    TADNLog.Log($"Volume uninstall process disposal failed: {exception.Message}");
                }
            }

            if (completed == null) return;

            try
            {
                completed(this, exitCode);
            }
            catch (Exception exception)
            {
                TADNLog.Log($"Volume uninstall process completion failed: {exception.Message}");
            }
        }
    }
}
