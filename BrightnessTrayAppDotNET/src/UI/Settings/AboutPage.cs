using Avalonia.Controls;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Settings;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private StackPanel BuildAboutPage() => BuildCommonAboutPage(new TrayAppDotNETAboutPageAppInfo
    {
        ApplicationName = Constants.ApplicationName,
        Tagline = L(nameof(AppStrings.Settings_About_Tagline)),
        BuildNumber = BuildInfo.BuildNumber,
        CommitHash = BuildInfo.CommitHash,
        SettingsFolderPath = AppSettings.GetDefaultDirectory(),
        UpdateSettings = _settings,
        UpdateService = static () => AppServices.UpdateCheckService,
        KnownIssues =
        [
            new TrayAppDotNETKnownIssue(
                L(nameof(AppStrings.Settings_About_NightLightCorruption_Title)),
                L(nameof(AppStrings.Settings_About_NightLightCorruption_Description))),
            new TrayAppDotNETKnownIssue(
                L(nameof(AppStrings.Settings_About_DDCCorruption_Title)),
                L(nameof(AppStrings.Settings_About_DDCCorruption_Description)))
        ]
    });
}
