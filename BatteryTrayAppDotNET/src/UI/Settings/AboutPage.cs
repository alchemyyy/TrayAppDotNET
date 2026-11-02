using Avalonia.Controls;

namespace BatteryTrayAppDotNET.UI.Settings;

public sealed partial class BatterySettingsWindow
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
        SupportsFlyoutUpdateButton = false
    });
}
