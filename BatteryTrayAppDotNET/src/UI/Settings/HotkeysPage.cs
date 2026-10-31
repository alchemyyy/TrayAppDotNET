using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Settings;

namespace BatteryTrayAppDotNET.UI.Settings;

public sealed partial class BatterySettingsWindow
{
    private StackPanel BuildHotkeysPage() => BuildHotkeysPage(
        _settings,
        static () => AppServices.HotkeyService,
        [
            new TrayAppDotNETHotkeyRow<HotkeyAction>
            {
                Action = HotkeyAction.OpenFlyout,
                Title = L(nameof(CommonStrings.Settings_Hotkeys_OpenFlyout_Title)),
                Description = L(nameof(AppStrings.Settings_Hotkeys_OpenFlyout_Description))
            },
            new TrayAppDotNETHotkeyRow<HotkeyAction>
            {
                Action = HotkeyAction.OpenSettings,
                Title = L(nameof(CommonStrings.Settings_Hotkeys_OpenSettings_Title)),
                Description = L(nameof(CommonStrings.Settings_Hotkeys_OpenSettings_Description))
            }
        ]);
}
