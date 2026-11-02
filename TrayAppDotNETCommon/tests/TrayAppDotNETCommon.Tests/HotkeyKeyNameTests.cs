using Avalonia.Input;
using TrayAppDotNETCommon.UI.Hotkeys;
using TrayAppDotNETCommon.Localization;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class HotkeyKeyNameTests
{
    // Every key the capture box can record is named, never shown through the numeric fallback
    [Fact]
    public void EveryCapturableKeyHasAName()
    {
        static string fallbackOf(uint vk) =>
            TrayAppDotNETHotkeyKeys.KeyName(vk) == string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                LocalizationManager.Instance[nameof(CommonStrings.Settings_Hotkeys_Key_VK_Format)],
                vk)
                ? $"VK {vk:X2}"
                : string.Empty;

        List<string> unnamed = [];
        foreach (Key key in Enum.GetValues<Key>())
        {
            uint vk = TrayAppDotNETHotkeyKeys.VirtualKeyFromKey(key);
            if (vk == 0) continue;
            // Punctuation keys are named by the current keyboard layout, which may not have every OEM key
            if (vk is >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2) continue;

            string fallback = fallbackOf(vk);
            if (fallback.Length > 0) unnamed.Add($"{key} ({fallback})");
        }

        Assert.Empty(unnamed);
    }
}
