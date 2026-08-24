using NetworkTrayAppDotNET.Models;
using TrayAppDotNETCommon.UI.Controls;
using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;

namespace NetworkTrayAppDotNET.UI.Settings;

public sealed class NetworkUninstallerWindow(string installDir, InstallScope scope)
    : TrayAppDotNETUninstallerWindow(CreateOptions(installDir, scope))
{
    public NetworkUninstallerWindow()
        : this(string.Empty, InstallScope.LocalAppData)
    {
    }

    private static TrayAppDotNETUninstallerWindowOptions CreateOptions(string installDir, InstallScope scope)
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
            RunUninstall = static (uninstallScope, deleteSettings) =>
                AppServices.Installation.RunUninstall(uninstallScope, deleteSettings)
        };
    }

    private static string L(string key) => TrayLocalization.Instance[key];
}
