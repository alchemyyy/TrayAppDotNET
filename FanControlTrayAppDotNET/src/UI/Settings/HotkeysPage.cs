using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Settings;

namespace FanControlTrayAppDotNET.UI.Settings;

public sealed partial class FanSettingsWindow
{
    private StackPanel BuildHotkeysPage() => BuildHotkeysPage(
        _settings,
        static () => AppServices.HotkeyService,
        [
            new TrayAppDotNETHotkeyRow<HotkeyAction>
            {
                Action = HotkeyAction.OpenFlyout,
                Title = L(nameof(CommonStrings.Settings_Hotkeys_OpenFlyout_Title)),
                Description = L(nameof(AppStrings.Settings_Hotkeys_OpenFlyout_Description)),
                SearchKeywords = [L(nameof(AppStrings.Settings_Hotkeys_OpenFlyout_SearchKeywords))]
            },
            new TrayAppDotNETHotkeyRow<HotkeyAction>
            {
                Action = HotkeyAction.OpenSettings,
                Title = L(nameof(CommonStrings.Settings_Hotkeys_OpenSettings_Title)),
                Description = L(nameof(CommonStrings.Settings_Hotkeys_OpenSettings_Description)),
                SearchKeywords = [L(nameof(AppStrings.Settings_Hotkeys_OpenSettings_SearchKeywords))]
            }
        ]);
}
