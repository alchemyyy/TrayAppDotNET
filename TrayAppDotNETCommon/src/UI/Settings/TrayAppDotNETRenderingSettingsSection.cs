using Avalonia;
using Avalonia.Controls;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Controls;

namespace TrayAppDotNETCommon.UI.Settings;

public sealed class TrayAppDotNETRenderingSettingsSectionOptions
{
    public required SettingsPalette Palette { get; init; }
    public required CornerRadius CardRadius { get; init; }
    public required Func<string, string> L { get; init; }
    public required Action Save { get; init; }
    public required Func<string, string, string, string?, Task<bool>> ConfirmAsync { get; init; }
    public required Func<string, string, Task> ShowMessage { get; init; }
    public required ITrayAppDotNETRenderingSettings RenderingSettings { get; init; }
    public ITrayAppDotNETWarmWindowSettings? WarmWindowSettings { get; init; }

    /// <summary>Submenu delay settings; only apps whose tray menus have submenus (Task Manager) set it.</summary>
    public ITrayAppDotNETTrayMenuSettings? SubmenuSettings { get; init; }

    public bool SupportsFlyoutWarmWindow { get; init; }
    public bool SupportsTrayContextMenuWarmWindow { get; init; }
}

public sealed class TrayAppDotNETRenderingSettingsSection(TrayAppDotNETRenderingSettingsSectionOptions options)
{
    private const double RenderingBackendComboWidth = 172;

    /// <summary>Adds the rendering backend card, the keep-warm cards and the submenu delay cards the app supports.</summary>
    public void AddCards(StackPanel stack)
    {
        SettingsPalette p = options.Palette;
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_General_Rendering_Header)), p));

        stack.Children.Add(BuildRenderingBackendCard());

        ITrayAppDotNETWarmWindowSettings? warmWindowSettings = options.WarmWindowSettings;
        if (warmWindowSettings != null &&
            options is not { SupportsFlyoutWarmWindow: false, SupportsTrayContextMenuWarmWindow: false })
        {
            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L(nameof(CommonStrings.Settings_General_Performance_Header)), p));

            if (options.SupportsFlyoutWarmWindow)
            {
                stack.Children.Add(BuildCard(
                    L(nameof(CommonStrings.Settings_General_KeepFlyoutWarm_Title)),
                    L(nameof(CommonStrings.Settings_General_KeepFlyoutWarm_Description)),
                    warmWindowSettings.KeepFlyoutWarm,
                    value => warmWindowSettings.KeepFlyoutWarm = value,
                    ControlMap.RenderingSection.Performance.KeepFlyoutWarm));
            }

            if (options.SupportsTrayContextMenuWarmWindow)
            {
                stack.Children.Add(BuildCard(
                    L(nameof(CommonStrings.Settings_General_KeepTrayContextMenuWarm_Title)),
                    L(nameof(CommonStrings.Settings_General_KeepTrayContextMenuWarm_Description)),
                    warmWindowSettings.KeepTrayContextMenuWarm,
                    value => warmWindowSettings.KeepTrayContextMenuWarm = value,
                    ControlMap.RenderingSection.Performance.KeepTrayContextMenuWarm));
            }
        }

        AddTrayMenuCards(stack);
    }

    private void AddTrayMenuCards(StackPanel stack)
    {
        ITrayAppDotNETTrayMenuSettings? trayMenuSettings = options.SubmenuSettings;
        if (trayMenuSettings == null) return;

        SettingsPalette palette = options.Palette;
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_General_ContextMenu_Header)),
            palette));

        Border submenuDelayCard = TrayAppDotNETSettingsCards.IntCard(
            L(nameof(CommonStrings.Settings_General_SubmenuOpenDelay_Title)),
            L(nameof(CommonStrings.Settings_General_SubmenuOpenDelay_Description)),
            trayMenuSettings.SubmenuShowDelayMs,
            TimeConstants.TrayMenuSubmenuShowDelayMinMs,
            TimeConstants.TrayMenuSubmenuShowDelayMaxMs,
            value => trayMenuSettings.SubmenuShowDelayMs = value,
            palette,
            options.CardRadius,
            options.Save,
            suffix: " ms",
            [L(nameof(CommonStrings.Settings_General_SubmenuOpenDelay_SearchKeywords))],
            ControlMap.RenderingSection.TrayMenu.SubmenuOpenDelay);
        submenuDelayCard.IsVisible = !trayMenuSettings.UseSystemSubmenuShowDelay;

        stack.Children.Add(TrayAppDotNETSettingsCards.BoolCard(
            L(nameof(CommonStrings.Settings_General_UseSystemSubmenuDelay_Title)),
            L(nameof(CommonStrings.Settings_General_UseSystemSubmenuDelay_Description)),
            trayMenuSettings.UseSystemSubmenuShowDelay,
            value =>
            {
                trayMenuSettings.UseSystemSubmenuShowDelay = value;
                submenuDelayCard.IsVisible = !value;
            },
            palette,
            options.CardRadius,
            options.Save,
            searchKeywords: [L(nameof(CommonStrings.Settings_General_UseSystemSubmenuDelay_SearchKeywords))],
            node: ControlMap.RenderingSection.TrayMenu.UseSystemSubmenuDelay));
        stack.Children.Add(submenuDelayCard);
    }

    /// <summary>Builds the startup-only rendering backend selector.</summary>
    private Border BuildRenderingBackendCard()
    {
        SettingsComboBox combo = TrayAppDotNETSettingsUI.ComboBox(
            options.Palette,
            RenderingBackendComboWidth,
            autoSizeToText: true,
            SettingsComboBoxAutoSizeMode.SelectedItem).MapTo(ControlMap.RenderingSection.RenderingBackend);
        foreach ((TrayAppDotNETRenderingBackend backend, string text) in RenderingBackendOptions())
            combo.Items.Add(new SettingsComboBoxItem(backend.ToString(), text, options.Palette));

        TrayAppDotNETSettingsUI.SelectComboByTag(combo, options.RenderingSettings.RenderingBackend.ToString());
        combo.SelectionChanged += async (_, _) =>
        {
            string? tag = TrayAppDotNETSettingsUI.SelectedTag(combo);
            if (string.IsNullOrEmpty(tag)) return;
            if (!Enum.TryParse(tag, out TrayAppDotNETRenderingBackend backend)) return;
            if (backend == options.RenderingSettings.RenderingBackend) return;

            options.RenderingSettings.RenderingBackend = backend;
            options.Save();
            await TrayAppDotNETRestartPrompt.PromptAsync(
                options.ConfirmAsync,
                options.ShowMessage,
                options.L,
                L(nameof(CommonStrings.Settings_General_RenderingRestart_Title)),
                L(nameof(CommonStrings.Settings_General_RenderingRestart_Message)));
        };

        return TrayAppDotNETSettingsCards.Card(
            L(nameof(CommonStrings.Settings_General_RenderingBackend_Title)),
            L(nameof(CommonStrings.Settings_General_RenderingBackend_Description)),
            combo,
            options.Palette,
            options.CardRadius,
            [L(nameof(CommonStrings.Settings_General_RenderingBackend_SearchKeywords))]);
    }

    /// <summary>Builds a boolean keep-warm setting card.</summary>
    private Border BuildCard(string title, string description, bool value, Action<bool> set, ControlMapNodeID node)
    {
        SettingsToggle toggle = TrayAppDotNETSettingsUI.Toggle(options.Palette, value, (_, enabled) =>
        {
            set(enabled);
            options.Save();
        }).MapTo(node);

        return TrayAppDotNETSettingsCards.MutableCard(
            title,
            description,
            toggle,
            options.Palette,
            options.CardRadius,
            out _,
            [L(nameof(CommonStrings.Settings_General_KeepWarm_SearchKeywords))]);
    }

    /// <summary>Returns user-facing rendering backend choices.</summary>
    private IReadOnlyList<(TrayAppDotNETRenderingBackend Backend, string Text)> RenderingBackendOptions() =>
    [
        (TrayAppDotNETRenderingBackend.GPUPreferred,
            L(nameof(CommonStrings.Settings_General_RenderingBackend_GPUPreferred))),
        (TrayAppDotNETRenderingBackend.Software,
            L(nameof(CommonStrings.Settings_General_RenderingBackend_Software)))
    ];

    /// <summary>Looks up a settings string.</summary>
    private string L(string key) => options.L(key);
}

/// <summary>The restart offer shared by settings that only apply at startup (rendering backend, language).</summary>
public static class TrayAppDotNETRestartPrompt
{
    /// <summary>
    /// Asks whether to restart now; on yes, this app's exit path runs and a new instance starts with the same settings
    /// folder override. A failed restart is shown as a message.
    /// </summary>
    public static async Task PromptAsync(
        Func<string, string, string, string?, Task<bool>> confirmAsync,
        Func<string, string, Task> showMessage,
        Func<string, string> L,
        string title,
        string message)
    {
        bool restart = await confirmAsync(
            title,
            message,
            L(nameof(CommonStrings.Settings_General_RenderingRestart_Button)),
            L(nameof(CommonStrings.Settings_General_NotNow_Button)));
        if (!restart) return;

        try
        {
            string? executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new FileNotFoundException(message: "Current executable was not found.", executablePath);

            TrayAppDotNETAppLifetime.Restart(executablePath, TrayAppDotNETProgram.SettingsDirectoryCommandLine());
        }
        catch (Exception ex)
        {
            await showMessage(L(nameof(CommonStrings.Settings_General_RestartFailed_Title)), ex.Message);
        }
    }
}
