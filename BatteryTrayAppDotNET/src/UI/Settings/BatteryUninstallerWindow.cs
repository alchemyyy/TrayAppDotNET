using BatteryTrayAppDotNET.Visuals;
using TrayAppDotNETCommon.UI.Controls;
using BatteryInstallScope = TrayAppDotNETCommon.Models.InstallScope;
using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;

namespace BatteryTrayAppDotNET.UI.Settings;

public sealed class BatteryUninstallerWindow(string installDir, BatteryInstallScope scope) : TrayAppDotNETUninstallerWindow(CreateOptions(installDir, scope))
{
    public BatteryUninstallerWindow()
        : this(string.Empty, BatteryInstallScope.LocalAppData)
    {
    }

    private static TrayAppDotNETUninstallerWindowOptions CreateOptions(string installDir, BatteryInstallScope scope)
    {
        SettingsPalette palette =
            BatterySettingsPalette.Create(AppServices.Theme, AppServices.Settings, ResolveEffectiveIsLight());
        return new TrayAppDotNETUninstallerWindowOptions
        {
            ApplicationName = Program.ApplicationName,
            InstallDirectory = installDir,
            SettingsDirectory = AppSettings.GetDefaultDirectory(),
            InstallScope = scope,
            Icon = AppTheme.LoadAppIcon(),
            Palette = palette,
            EnableRoundedCorners = AppServices.Settings?.EnableRoundedCorners == true,
            L = L,
            RunUninstall = static (uninstallScope, deleteSettings) =>
                AppServices.Installation.RunUninstall(uninstallScope, deleteSettings)
        };
    }

    private static bool ResolveEffectiveIsLight() => AppServices.Settings?.ThemeMode switch
    {
        ThemeMode.Light => true,
        ThemeMode.Dark => false,
        _ => AppServices.Theme?.IsLightTheme ?? AppTheme.Default.IsLightTheme
    };

    private static string L(string key) => TrayLocalization.Instance[key];
}
