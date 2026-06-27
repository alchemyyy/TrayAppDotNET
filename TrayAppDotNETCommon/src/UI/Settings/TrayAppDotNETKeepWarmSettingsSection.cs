using Avalonia;
using Avalonia.Controls;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.UI.Controls;

namespace TrayAppDotNETCommon.UI.Settings;

public sealed class TrayAppDotNETKeepWarmSettingsSectionOptions
{
    public required SettingsPalette Palette { get; init; }
    public required CornerRadius CardRadius { get; init; }
    public required Func<string, string, string> Localize { get; init; }
    public required Action Save { get; init; }
    public required ITrayAppDotNETKeepWarmSettings Settings { get; init; }
    public bool SupportsFlyout { get; init; }
    public bool SupportsTrayContextMenu { get; init; }
}

public sealed class TrayAppDotNETKeepWarmSettingsSection(TrayAppDotNETKeepWarmSettingsSectionOptions options)
{
    public void AddCards(StackPanel stack)
    {
        if (!options.SupportsFlyout && !options.SupportsTrayContextMenu) return;

        SettingsPalette p = options.Palette;
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_General_KeepWarm_Header", "Keep warm"), p));

        if (options.SupportsFlyout)
        {
            stack.Children.Add(BuildCard(
                L("Settings_General_KeepFlyoutWarm_Title", "Keep flyout warm"),
                L("Settings_General_KeepFlyoutWarm_Description",
                    "Keep the flyout created in the background so it opens faster. When off, hidden UI resources are released after a short idle delay."),
                options.Settings.KeepFlyoutWarm,
                value => options.Settings.KeepFlyoutWarm = value));
        }

        if (options.SupportsTrayContextMenu)
        {
            stack.Children.Add(BuildCard(
                L("Settings_General_KeepTrayContextMenuWarm_Title", "Keep tray context menu warm"),
                L("Settings_General_KeepTrayContextMenuWarm_Description",
                    "Keep the tray context menu created in the background so it opens faster. When off, hidden UI resources are released after a short idle delay."),
                options.Settings.KeepTrayContextMenuWarm,
                value => options.Settings.KeepTrayContextMenuWarm = value));
        }
    }

    private Border BuildCard(string title, string description, bool value, Action<bool> set)
    {
        SettingsToggle toggle = TrayAppDotNETSettingsUI.Toggle(options.Palette, value, (_, enabled) =>
        {
            set(enabled);
            options.Save();
        });

        return TrayAppDotNETSettingsCards.MutableCard(
            title,
            description,
            toggle,
            options.Palette,
            options.CardRadius,
            out _);
    }

    private string L(string key, string fallback) => options.Localize(key, fallback);
}
