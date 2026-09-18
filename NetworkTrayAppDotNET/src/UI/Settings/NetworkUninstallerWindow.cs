using NetworkTrayAppDotNET.Models;
using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;

namespace NetworkTrayAppDotNET.UI.Settings;

public sealed class NetworkUninstallerWindow(string installDir, InstallScope scope, IProgress<TrayAppDotNETInstallProgress>? progress = null)
    : TrayAppDotNETUninstallerWindow(CreateOptions(installDir, scope, progress))
{
    public NetworkUninstallerWindow()
        : this(string.Empty, InstallScope.LocalAppData)
    {
    }

    private static TrayAppDotNETUninstallerWindowOptions CreateOptions(string installDir, InstallScope scope, IProgress<TrayAppDotNETInstallProgress>? progress)
    {
        AppSettings settings = AppServices.Settings ?? new AppSettings();
        SettingsPalette palette = NetworkSettingsWindow.CreatePalette(
            AppServices.Theme,
            settings,
            AppTheme.ResolveEffectiveIsLightTheme(settings));
        return new TrayAppDotNETUninstallerWindowOptions
        {
            ApplicationName = Program.ApplicationName,
            InstallDirectory = installDir,
            SettingsDirectory = AppSettings.GetDefaultDirectory(),
            InstallScope = scope,
            Icon = AppTheme.LoadAppIcon(),
            Palette = palette,
            EnableRoundedCorners = settings.EnableRoundedCorners,
            L = L,
            Progress = progress,
            RunUninstall = static (uninstallScope, deleteSettings, uninstallProgress) =>
                AppServices.Installation.RunUninstall(uninstallScope, deleteSettings, progress: uninstallProgress)
        };
    }

    private static string L(string key) => TrayLocalization.Instance[key];
}
