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
    Func<string, string, string> Localize,
    Action Save);

public sealed class TrayAppDotNETRenderingSettingsSectionOptions
{
    public required SettingsPalette Palette { get; init; }
    public required CornerRadius CardRadius { get; init; }
    public required Func<string, string, string> Localize { get; init; }
    public required Action Save { get; init; }
    public required Func<string, string, string, string, Task<bool>> ConfirmAsync { get; init; }
    public required Func<string, string, Task> ShowMessage { get; init; }
    public required ITrayAppDotNETRenderingSettings RenderingSettings { get; init; }
    public ITrayAppDotNETWarmWindowSettings? WarmWindowSettings { get; init; }
    public bool SupportsFlyoutWarmWindow { get; init; }
    public bool SupportsTrayContextMenuWarmWindow { get; init; }
    public IReadOnlyList<Func<TrayAppDotNETRenderingSettingsCardContext, Control>> AdditionalCards { get; init; } = [];
}

public sealed class TrayAppDotNETRenderingSettingsSection(TrayAppDotNETRenderingSettingsSectionOptions options)
{
    private const double RenderingBackendComboWidth = 172;
    private readonly TrayAppDotNETRenderingSettingsCardContext _cardContext =
        new(options.Palette, options.CardRadius, options.Localize, options.Save);

    /// <summary>Adds rendering cards and optional rendering-adjacent cards to the supplied settings page stack.</summary>
    public void AddCards(StackPanel stack)
    {
        SettingsPalette p = options.Palette;
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_General_Rendering_Header", "Rendering"), p));

        stack.Children.Add(BuildRenderingBackendCard());
        foreach (Func<TrayAppDotNETRenderingSettingsCardContext, Control> buildCard in options.AdditionalCards)
            stack.Children.Add(buildCard(_cardContext));

        ITrayAppDotNETWarmWindowSettings? warmWindowSettings = options.WarmWindowSettings;
        if (warmWindowSettings == null ||
            (!options.SupportsFlyoutWarmWindow && !options.SupportsTrayContextMenuWarmWindow))
            return;

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_General_Performance_Header", "Performance"), p));

        if (options.SupportsFlyoutWarmWindow)
        {
            stack.Children.Add(BuildCard(
                L("Settings_General_KeepFlyoutWarm_Title", "Keep flyout warm"),
                L("Settings_General_KeepFlyoutWarm_Description",
                    "Keep the flyout created in the background so it opens faster. When off, hidden UI resources are released after a short idle delay."),
                warmWindowSettings.KeepFlyoutWarm,
                value => warmWindowSettings.KeepFlyoutWarm = value));
        }

        if (options.SupportsTrayContextMenuWarmWindow)
        {
            stack.Children.Add(BuildCard(
                L("Settings_General_KeepTrayContextMenuWarm_Title", "Keep tray context menu warm"),
                L("Settings_General_KeepTrayContextMenuWarm_Description",
                    "Keep the tray context menu created in the background so it opens faster. When off, hidden UI resources are released after a short idle delay."),
                warmWindowSettings.KeepTrayContextMenuWarm,
                value => warmWindowSettings.KeepTrayContextMenuWarm = value));
        }
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
            L("Settings_General_RenderingBackend_Title", "Rendering backend"),
            L("Settings_General_RenderingBackend_Description",
                "\"GPU preferred\" uses Avalonia's Windows GPU path with software fallback. \"Software\" forces CPU rendering. GPU rendering is faster but has more RAM overhead. Restart required."),
            combo,
            options.Palette,
            options.CardRadius);
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
            out _);
    }

    /// <summary>Asks whether to restart now after a rendering backend change.</summary>
    private async Task PromptRestartAsync()
    {
        bool restart = await options.ConfirmAsync(
            L("Settings_General_RenderingRestart_Title", "Restart required"),
            L("Settings_General_RenderingRestart_Message",
                "Restart the app now to apply the selected rendering backend?"),
            L("Settings_General_RenderingRestart_Button", "Restart"),
            L("Settings_General_NotNow_Button", "Not now"));
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
                throw new FileNotFoundException("Current executable was not found.", executablePath);

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
            await options.ShowMessage(L("Settings_General_RestartFailed_Title", "Restart failed"), ex.Message);
        }
    }

    /// <summary>Returns user-facing rendering backend choices.</summary>
    private IReadOnlyList<(TrayAppDotNETRenderingBackend Backend, string Text)> RenderingBackendOptions() =>
    [
        (TrayAppDotNETRenderingBackend.GPUPreferred,
            L("Settings_General_RenderingBackend_GPUPreferred", "GPU preferred")),
        (TrayAppDotNETRenderingBackend.Software,
            L("Settings_General_RenderingBackend_Software", "Software"))
    ];

    /// <summary>Requests shutdown through the classic desktop lifetime.</summary>
    private static void ShutdownDesktopApp()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    /// <summary>Localizes a settings string with a fallback.</summary>
    private string L(string key, string fallback) => options.Localize(key, fallback);
}
