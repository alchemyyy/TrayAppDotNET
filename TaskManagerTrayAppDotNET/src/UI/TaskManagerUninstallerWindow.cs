using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.UI;

internal sealed class TaskManagerUninstallerWindow(string installDirectory, InstallScope scope)
    : TrayAppDotNETUninstallerWindow(CreateOptions(installDirectory, scope))
{
    private static TrayAppDotNETUninstallerWindowOptions CreateOptions(
        string installDirectory,
        InstallScope scope)
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
            L = static key => LocalizationManager.Instance[key],
            RetargetStartupShortcut = static uninstallScope =>
                AppServices.Startup.RetargetShortcutIfPresent(exclude: uninstallScope),
            RunUninstall = static (uninstallScope, deleteSettings) =>
                AppServices.Installation.RunUninstall(uninstallScope, deleteSettings)
        };
    }
}
