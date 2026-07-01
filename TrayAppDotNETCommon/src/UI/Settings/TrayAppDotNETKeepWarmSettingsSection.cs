using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.UI.Controls;

namespace TrayAppDotNETCommon.UI.Settings;

public sealed class TrayAppDotNETKeepWarmSettingsSectionOptions
{
    public required SettingsPalette Palette { get; init; }
    public required CornerRadius CardRadius { get; init; }
    public required Func<string, string, string> Localize { get; init; }
    public required Action Save { get; init; }
    public required Func<string, string, string, string, Task<bool>> ConfirmAsync { get; init; }
    public required Func<string, string, Task> ShowMessage { get; init; }
    public required ITrayAppDotNETKeepWarmSettings Settings { get; init; }
    public bool SupportsFlyout { get; init; }
    public bool SupportsTrayContextMenu { get; init; }
}

public sealed class TrayAppDotNETKeepWarmSettingsSection(TrayAppDotNETKeepWarmSettingsSectionOptions options)
{
    private const double RenderingBackendComboWidth = 172;

    /// <summary>Adds rendering and keep-warm cards to the supplied settings page stack.</summary>
    public void AddCards(StackPanel stack)
    {
        SettingsPalette p = options.Palette;
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_General_Rendering_Header", "Rendering"), p));

        stack.Children.Add(BuildRenderingBackendCard());

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

        TrayAppDotNETSettingsUI.SelectComboByTag(combo, options.Settings.RenderingBackend.ToString());
        combo.SelectionChanged += async (_, _) =>
        {
            string? tag = TrayAppDotNETSettingsUI.SelectedTag(combo);
            if (string.IsNullOrEmpty(tag)) return;
            if (!Enum.TryParse(tag, out TrayAppDotNETRenderingBackend backend)) return;
            if (backend == options.Settings.RenderingBackend) return;

            options.Settings.RenderingBackend = backend;
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
                WindowStyle = ProcessWindowStyle.Hidden,
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
    private static IReadOnlyList<(TrayAppDotNETRenderingBackend Backend, string Text)> RenderingBackendOptions() =>
    [
        (TrayAppDotNETRenderingBackend.GPUPreferred, "GPU preferred"),
        (TrayAppDotNETRenderingBackend.Software, "Software"),
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
