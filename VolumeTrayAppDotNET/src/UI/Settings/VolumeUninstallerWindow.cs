using VolumeInstallScope = TrayAppDotNETCommon.Models.InstallScope;
using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed class VolumeUninstallerWindow(string installDir, VolumeInstallScope scope)
    : TrayAppDotNETUninstallerWindow(CreateOptions(installDir, scope))
{
    public VolumeUninstallerWindow()
        : this(string.Empty, VolumeInstallScope.LocalAppData)
    {
    }

    private static TrayAppDotNETUninstallerWindowOptions CreateOptions(string installDir, VolumeInstallScope scope)
    {
        SettingsPalette palette =
            VolumeSettingsPalette.Create(AppServices.Theme, AppServices.Settings, ResolveEffectiveIsLight());
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
