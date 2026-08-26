using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Settings;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private readonly List<TrayAppDotNETAboutPage> _aboutPageGenerations = [];

    private StackPanel BuildAboutPage()
    {
        TrayAppDotNETAboutPage aboutPage = OwnPageResource(new TrayAppDotNETAboutPage(new TrayAppDotNETAboutPageOptions
        {
            Palette = Palette,
            ButtonRadius = RadiusMedium,
            CardRadius = RadiusLarge,
            L = L,
            Save = Save,
            ApplicationName = Constants.ApplicationName,
            Tagline = L(nameof(AppStrings.Settings_About_Tagline)),
            BuildNumber = BuildInfo.BuildNumber,
            CommitHash = BuildInfo.CommitHash,
            Publisher = Constants.Publisher,
            HelpLink = Constants.HelpLink,
            OpenSettingsFolderText = OpenSettingsFolderText,
            SettingsFolderPath = SettingsFolderPath,
            UpdateSettings = _settings,
            UpdateService = static () => AppServices.UpdateCheckService,
            ConfirmAsync = ConfirmAsync,
            PromptOwner = () => this,
            FlushLog = static () => TADNLog.Flush(),
            Log = static message => TADNLog.Log(message),
            RebuildAboutPage = () => RebuildShell(BrightnessSettingsPage.About),
            StaleCheckTimerIntervalMs = TimeConstants.AboutStaleCheckTimerIntervalMs,
            UpdateStaleGraceMs = TimeConstants.UpdateStaleGraceMs,
            KnownIssues =
            [
                new TrayAppDotNETKnownIssue(
                    L(nameof(AppStrings.Settings_About_NightLightCorruption_Title)),
                    L(nameof(AppStrings.Settings_About_NightLightCorruption_Description))),
                new TrayAppDotNETKnownIssue(
                    L(nameof(AppStrings.Settings_About_DDCCorruption_Title)),
                    L(nameof(AppStrings.Settings_About_DDCCorruption_Description)))
            ]
        }));
        _aboutPageGenerations.Add(aboutPage);
        AddPageCleanup(() => _aboutPageGenerations.Remove(aboutPage));
        return aboutPage.Build();
    }
}
