using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.UI;

internal sealed class TaskManagerUninstallerWindow(
    string installDirectory, InstallScope scope, IProgress<TrayAppDotNETInstallProgress>? progress = null)
    : TrayAppDotNETUninstallerWindow(CreateOptions(installDirectory, scope, progress))
{
    private static TrayAppDotNETUninstallerWindowOptions CreateOptions(
        string installDirectory,
        InstallScope scope,
        IProgress<TrayAppDotNETInstallProgress>? progress)
    {
        AppTheme theme = AppServices.Theme ?? AppTheme.Default;
        AppSettings? settings = AppServices.Settings;
        bool isLight = settings?.ThemeMode switch
        {
            TrayAppDotNETThemeMode.Light => true,
            TrayAppDotNETThemeMode.Dark => false,
            _ => theme.IsLightTheme
        };
        SettingsPalette palette = VolumeSettingsPalette.Create(theme, settings, isLight);
        return new TrayAppDotNETUninstallerWindowOptions
        {
            ApplicationName = Program.ApplicationName,
            InstallDirectory = installDirectory,
            SettingsDirectory = AppSettings.GetDefaultDirectory(),
            InstallScope = scope,
            Icon = null,
            Palette = palette,
            EnableRoundedCorners = settings?.EnableRoundedCorners ?? true,
            Progress = progress,
            L = static key => LocalizationManager.Instance[key],
            RunUninstall = static (uninstallScope, deleteSettings, uninstallProgress) =>
                AppServices.Installation.RunUninstall(uninstallScope, deleteSettings, progress: uninstallProgress)
        };
    }
}
