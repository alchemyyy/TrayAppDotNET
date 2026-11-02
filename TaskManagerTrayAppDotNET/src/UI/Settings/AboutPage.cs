using Avalonia.Controls;

namespace TaskManagerTrayAppDotNET.UI;

public sealed partial class TaskManagerSettingsWindow
{
    private StackPanel BuildAboutPage() => BuildCommonAboutPage(new TrayAppDotNETAboutPageAppInfo
    {
        ApplicationName = Constants.DisplayName,
        Tagline = L(nameof(AppStrings.Settings_About_Tagline)),
        BuildNumber = BuildInfo.BuildNumber,
        CommitHash = BuildInfo.CommitHash,
        SettingsFolderPath = AppSettings.GetDefaultDirectory(),
        UpdateSettings = _settings,
        UpdateService = static () => AppServices.UpdateCheckService,
        SupportsFlyoutUpdateButton = false,
        UpdatePromptNode = ControlMap.SettingsUpdateConfirmation.ID
    });
}
