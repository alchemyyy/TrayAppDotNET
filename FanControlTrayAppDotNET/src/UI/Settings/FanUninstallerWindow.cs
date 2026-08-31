using FanInstallScope = TrayAppDotNETCommon.Models.InstallScope;
using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;

namespace FanControlTrayAppDotNET.UI.Settings;

public sealed class FanUninstallerWindow(string installDir, FanInstallScope scope)
    : TrayAppDotNETUninstallerWindow(CreateOptions(installDir, scope))
{
    public FanUninstallerWindow()
        : this(string.Empty, FanInstallScope.LocalAppData)
    {
    }

    private static TrayAppDotNETUninstallerWindowOptions CreateOptions(string installDir, FanInstallScope scope)
    {
        AppSettings settings = AppServices.Settings ?? new AppSettings();
        SettingsPalette palette = FanSettingsWindow.CreatePalette(
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
