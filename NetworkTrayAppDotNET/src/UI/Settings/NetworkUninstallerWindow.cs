using NetworkTrayAppDotNET.Models;
using TrayAppDotNETCommon.UI.Controls;
using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;

namespace NetworkTrayAppDotNET.UI;

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
            Localize = Localize,
            RetargetStartupShortcut = static uninstallScope =>
                AppServices.Startup.RetargetShortcutIfPresent(exclude: uninstallScope),
            RunUninstall = static (uninstallScope, deleteSettings) =>
                AppServices.Installation.RunUninstall(uninstallScope, deleteSettings),
        };
    }

    private static string Localize(string key, string fallback)
    {
        try
        {
            string value = TrayLocalization.Instance[key];
            return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }
}
