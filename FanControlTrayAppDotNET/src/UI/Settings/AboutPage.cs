using Avalonia.Controls;
using TrayAppDotNETCommon.UI;

namespace FanControlTrayAppDotNET.UI.Settings;

public sealed partial class FanSettingsWindow
{
    private StackPanel BuildAboutPage() => BuildCommonAboutPage(new TrayAppDotNETAboutPageAppInfo
    {
        ApplicationName = Constants.ApplicationName,
        Tagline = L(nameof(AppStrings.Settings_About_Tagline)),
        BuildNumber = BuildInfo.BuildNumber,
        CommitHash = BuildInfo.CommitHash,
        SettingsFolderPath = AppSettings.GetDefaultDirectory(),
        UpdateSettings = _settings,
        UpdateService = static () => AppServices.UpdateCheckService
    });
}
