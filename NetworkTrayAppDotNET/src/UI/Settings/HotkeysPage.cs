using Avalonia.Controls;
using NetworkTrayAppDotNET.Models;
using TrayAppDotNETCommon.UI.Settings;

namespace NetworkTrayAppDotNET.UI.Settings;

public sealed partial class NetworkSettingsWindow
{
    // OpenNetworkSettings and OpenAdapterSettings are bindable only in settings.xml (documented in the control map)
    private StackPanel BuildHotkeysPage() => BuildHotkeysPage(
        _settings,
        static () => AppServices.HotkeyService,
        [
            new TrayAppDotNETHotkeyRow<NetworkHotkeyAction>
            {
                Action = NetworkHotkeyAction.OpenFlyout,
                Title = L(nameof(CommonStrings.Settings_Hotkeys_OpenFlyout_Title)),
                Description = L(nameof(AppStrings.Settings_Hotkeys_OpenFlyout_Description)),
                SearchKeywords = [L(nameof(AppStrings.Settings_Hotkeys_OpenFlyout_SearchKeywords))]
            },
            new TrayAppDotNETHotkeyRow<NetworkHotkeyAction>
            {
                Action = NetworkHotkeyAction.OpenSettings,
                Title = L(nameof(CommonStrings.Settings_Hotkeys_OpenSettings_Title)),
                Description = L(nameof(CommonStrings.Settings_Hotkeys_OpenSettings_Description)),
                SearchKeywords = [L(nameof(AppStrings.Settings_Hotkeys_OpenSettings_SearchKeywords))]
            }
        ]);
}
