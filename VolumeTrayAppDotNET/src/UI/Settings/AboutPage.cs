using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Settings;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
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
        UpdatePromptNode = ControlMap.UpdatePrompt.ID,
        KnownIssues =
        [
            new TrayAppDotNETKnownIssue(
                L(nameof(AppStrings.Settings_About_BluetoothCodecNotDisplaying_Title)),
                L(nameof(AppStrings.Settings_About_BluetoothCodecNotDisplaying_Description)))
        ]
    });
}
