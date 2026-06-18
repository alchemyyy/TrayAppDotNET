using Avalonia;
using Avalonia.Controls;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.UI.Controls;

namespace TrayAppDotNETCommon.UI.Settings;

public sealed class TrayAppDotNETMemorySettingsSectionOptions
{
    public required SettingsPalette Palette { get; init; }
    public required CornerRadius CardRadius { get; init; }
    public required Func<string, string, string> Localize { get; init; }
    public required Action Save { get; init; }
    public required ITrayAppDotNETStartupMemorySettings Settings { get; init; }
}

public sealed class TrayAppDotNETMemorySettingsSection(TrayAppDotNETMemorySettingsSectionOptions options)
{
    public void AddCards(StackPanel stack)
    {
        SettingsPalette p = options.Palette;
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_General_Memory_Header", "Memory"), p));

        SettingsToggle toggle = TrayAppDotNETSettingsUI.Toggle(options.Palette, options.Settings.PurgeMemoryOnStartup,
            (_, enabled) =>
            {
                options.Settings.PurgeMemoryOnStartup = enabled;
                options.Save();
            });

        stack.Children.Add(TrayAppDotNETSettingsCards.MutableCard(
            L("Settings_General_PurgeMemoryOnStartup_Title", "Purge memory on startup"),
            L("Settings_General_PurgeMemoryOnStartup_Description",
                "Release cold startup resources before any KeepWarm UI is primed."),
            toggle,
            options.Palette,
            options.CardRadius,
            out _));
    }

    private string L(string key, string fallback) => options.Localize(key, fallback);
}
