using TrayAppDotNETCommon.Services.Install;
using TrayAppDotNETCommon.UI.Controls;
using BrightnessInstallScope = TrayAppDotNETCommon.Models.InstallScope;
using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed class BrightnessUninstallerWindow(
    string installDir,
    BrightnessInstallScope scope,
    IProgress<TrayAppDotNETInstallProgress>? progress = null)
    : TrayAppDotNETUninstallerWindow(CreateOptions(installDir, scope, progress))
{
    public BrightnessUninstallerWindow()
        : this(string.Empty, BrightnessInstallScope.LocalAppData)
    {
    }

    private static TrayAppDotNETUninstallerWindowOptions CreateOptions(
        string installDir,
        BrightnessInstallScope scope,
        IProgress<TrayAppDotNETInstallProgress>? progress)
    {
        AppSettings settings = AppServices.Settings ?? new AppSettings();
        SettingsPalette palette = BrightnessSettingsWindow.CreatePalette(
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
