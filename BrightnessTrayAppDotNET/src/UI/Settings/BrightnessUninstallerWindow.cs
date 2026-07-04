using TrayAppDotNETCommon.UI.Controls;
using BrightnessInstallScope = TrayAppDotNETCommon.InstallScope;
using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed class BrightnessUninstallerWindow : TrayAppDotNETUninstallerWindow
{
    public BrightnessUninstallerWindow()
        : this(string.Empty, BrightnessInstallScope.LocalAppData)
    {
    }

    public BrightnessUninstallerWindow(string installDir, BrightnessInstallScope scope)
        : base(CreateOptions(installDir, scope))
    {
    }

    private static TrayAppDotNETUninstallerWindowOptions CreateOptions(string installDir, BrightnessInstallScope scope)
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
