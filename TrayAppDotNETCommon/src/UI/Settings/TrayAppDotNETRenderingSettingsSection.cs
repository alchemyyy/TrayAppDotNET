using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.UI.Controls;

namespace TrayAppDotNETCommon.UI.Settings;

public sealed record TrayAppDotNETRenderingSettingsCardContext(
    SettingsPalette Palette,
    CornerRadius CardRadius,
    Func<string, string> L,
    Action Save);

public sealed class TrayAppDotNETRenderingSettingsSectionOptions
{
    public required SettingsPalette Palette { get; init; }
    public required CornerRadius CardRadius { get; init; }
    public required Func<string, string> L { get; init; }
    public required Action Save { get; init; }
    public required Func<string, string, string, string, Task<bool>> ConfirmAsync { get; init; }
    public required Func<string, string, Task> ShowMessage { get; init; }
    public required ITrayAppDotNETRenderingSettings RenderingSettings { get; init; }
    public ITrayAppDotNETWarmWindowSettings? WarmWindowSettings { get; init; }
    public ITrayAppDotNETTrayMenuSettings? TrayMenuSettings { get; init; }
    public bool SupportsFlyoutWarmWindow { get; init; }
    public bool SupportsTrayContextMenuWarmWindow { get; init; }
    public IReadOnlyList<Func<TrayAppDotNETRenderingSettingsCardContext, Control>> AdditionalCards { get; init; } = [];
}

public sealed class TrayAppDotNETRenderingSettingsSection(TrayAppDotNETRenderingSettingsSectionOptions options)
{
    private const double RenderingBackendComboWidth = 172;

    private readonly TrayAppDotNETRenderingSettingsCardContext _cardContext =
        new(options.Palette, options.CardRadius, options.L, options.Save);

    /// <summary>Adds rendering cards and optional rendering-adjacent cards to the supplied settings page stack.</summary>
    public void AddCards(StackPanel stack)
    {
        SettingsPalette p = options.Palette;
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_General_Rendering_Header)), p));

        stack.Children.Add(BuildRenderingBackendCard());
        foreach (Func<TrayAppDotNETRenderingSettingsCardContext, Control> buildCard in options.AdditionalCards)
            stack.Children.Add(buildCard(_cardContext));

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
                    value => warmWindowSettings.KeepFlyoutWarm = value));
            }

            if (options.SupportsTrayContextMenuWarmWindow)
            {
                stack.Children.Add(BuildCard(
                    L(nameof(CommonStrings.Settings_General_KeepTrayContextMenuWarm_Title)),
                    L(nameof(CommonStrings.Settings_General_KeepTrayContextMenuWarm_Description)),
                    warmWindowSettings.KeepTrayContextMenuWarm,
                    value => warmWindowSettings.KeepTrayContextMenuWarm = value));
            }
        }

        AddTrayMenuCards(stack);
    }

    private void AddTrayMenuCards(StackPanel stack)
    {
        ITrayAppDotNETTrayMenuSettings? trayMenuSettings = options.TrayMenuSettings;
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
            [L(nameof(CommonStrings.Settings_General_SubmenuOpenDelay_SearchKeywords))]);
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
            searchKeywords: [L(nameof(CommonStrings.Settings_General_UseSystemSubmenuDelay_SearchKeywords))]));
        stack.Children.Add(submenuDelayCard);
    }

    /// <summary>Builds the startup-only rendering backend selector.</summary>
    private Border BuildRenderingBackendCard()
    {
        SettingsComboBox combo = TrayAppDotNETSettingsUI.ComboBox(
            options.Palette,
            RenderingBackendComboWidth,
            autoSizeToText: true,
            SettingsComboBoxAutoSizeMode.SelectedItem);
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
            await PromptRestartAsync();
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
            out _,
            [L(nameof(CommonStrings.Settings_General_KeepWarm_SearchKeywords))]);
    }

    /// <summary>Asks whether to restart now after a rendering backend change.</summary>
    private async Task PromptRestartAsync()
    {
        bool restart = await options.ConfirmAsync(
            L(nameof(CommonStrings.Settings_General_RenderingRestart_Title)),
            L(nameof(CommonStrings.Settings_General_RenderingRestart_Message)),
            L(nameof(CommonStrings.Settings_General_RenderingRestart_Button)),
            L(nameof(CommonStrings.Settings_General_NotNow_Button)));
        if (!restart) return;

        await RestartCurrentProcessAsync();
    }

    /// <summary>Starts a new process instance and shuts down the current desktop lifetime.</summary>
    private async Task RestartCurrentProcessAsync()
    {
        try
        {
            string? executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                throw new FileNotFoundException(message: "Current executable was not found.", executablePath);

            ProcessStartInfo startInfo = new()
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            string? workingDirectory = Path.GetDirectoryName(executablePath);
            if (!string.IsNullOrWhiteSpace(workingDirectory))
                startInfo.WorkingDirectory = workingDirectory;

            using Process? process = Process.Start(startInfo);
            if (process == null)
                throw new InvalidOperationException("Process.Start returned null.");

            ShutdownDesktopApp();
        }
        catch (Exception ex)
        {
            await options.ShowMessage(L(nameof(CommonStrings.Settings_General_RestartFailed_Title)), ex.Message);
        }
    }

    /// <summary>Returns user-facing rendering backend choices.</summary>
    private IReadOnlyList<(TrayAppDotNETRenderingBackend Backend, string Text)> RenderingBackendOptions() =>
    [
        (TrayAppDotNETRenderingBackend.GPUPreferred,
            L(nameof(CommonStrings.Settings_General_RenderingBackend_GPUPreferred))),
        (TrayAppDotNETRenderingBackend.Software,
            L(nameof(CommonStrings.Settings_General_RenderingBackend_Software)))
    ];

    /// <summary>Requests shutdown through the classic desktop lifetime.</summary>
    private static void ShutdownDesktopApp()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    /// <summary>Looks up a settings string.</summary>
    private string L(string key) => options.L(key);
}
