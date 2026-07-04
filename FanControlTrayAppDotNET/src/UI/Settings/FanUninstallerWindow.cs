using TrayAppDotNETCommon.UI.Controls;
using FanInstallScope = TrayAppDotNETCommon.InstallScope;
using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;

namespace FanControlTrayAppDotNET.UI;

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
