using System.Globalization;
using Avalonia.Controls;
using TrayAppDotNETCommon.Services;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed record TrayAppDotNETUpdatePromptOptions
{
    public required Window Owner { get; init; }
    public required UpdateCheckService Service { get; init; }
    public required UpdateInfo UpdateInfo { get; init; }
    public required SettingsPalette Palette { get; init; }
    public required bool EnableRoundedCorners { get; init; }
    public required Func<string, string, string> Localize { get; init; }
    public required Action Shutdown { get; init; }
    public Action<string> Log { get; init; } = static _ => { };
    public Action FlushLog { get; init; } = static () => { };
    public Action<bool>? SetPromptOpen { get; init; }
    public Action<bool>? SetDownloadInFlight { get; init; }
    public Action? PromptClosed { get; init; }
    public bool ShowFailurePrompt { get; init; } = true;
}

/// <summary>
/// Presents one shared update-install confirmation and staging flow.
/// </summary>
public static class TrayAppDotNETUpdatePromptPresenter
{
    /// <summary>
    /// Confirms, stages, and shuts down after a successful update handoff.
    /// </summary>
    public static async Task<bool> ShowInstallUpdateAsync(TrayAppDotNETUpdatePromptOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        bool confirmed = await ShowConfirmationAsync(options);
        if (!confirmed) return false;

        options.SetDownloadInFlight?.Invoke(true);
        bool staged = false;
        try
        {
            staged = await options.Service.DownloadAndStageAsync(options.UpdateInfo);
        }
        catch (Exception ex)
        {
            options.Log($"TrayAppDotNETUpdatePromptPresenter.ShowInstallUpdateAsync: {ex.Message}");
        }

        if (staged)
        {
            options.FlushLog();
            options.Shutdown();
            return true;
        }

        options.SetDownloadInFlight?.Invoke(false);
        if (options.ShowFailurePrompt)
            await ShowFailureAsync(options);
        return false;
    }

    private static async Task<bool> ShowConfirmationAsync(TrayAppDotNETUpdatePromptOptions options)
    {
        string title = string.Format(
            CultureInfo.CurrentCulture,
            Localize(options, "UpdateDialog_TitleFormat", "Update available: {0}"),
            options.UpdateInfo.ReleaseName);
        string description = Localize(options, "UpdateDialog_DefaultDescription", "A newer release is available.");
        string changelog = string.IsNullOrWhiteSpace(options.UpdateInfo.Changelog)
            ? Localize(options, "UpdateDialog_NoChangelog", "No changelog provided.")
            : options.UpdateInfo.Changelog;
        string confirmText = Localize(options, "UpdateDialog_Install", "Install");
        string cancelText = Localize(options, "UpdateDialog_Cancel", "Cancel");

        TrayAppDotNETUpdateConfirmationWindow dialog = new(
            title,
            description,
            changelog,
            confirmText,
            cancelText,
            options.Palette,
            options.EnableRoundedCorners)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        return await ShowPromptAsync(options, dialog);
    }

    private static async Task ShowFailureAsync(TrayAppDotNETUpdatePromptOptions options)
    {
        string title = Localize(options, "Settings_About_InstallUpdate_CheckFailed", "Update failed");
        string message = Localize(
            options,
            "UpdateDialog_DownloadFailed",
            "The update could not be downloaded. Check the log for details.");
        string okText = Localize(options, "SettingsWindow_ConfirmOverlay_OK", "OK");

        TrayAppDotNETUpdateConfirmationWindow dialog = new(
            title,
            message,
            null,
            okText,
            null,
            options.Palette,
            options.EnableRoundedCorners)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        _ = await ShowPromptAsync(options, dialog);
    }

    private static async Task<bool> ShowPromptAsync(
        TrayAppDotNETUpdatePromptOptions options,
        TrayAppDotNETUpdateConfirmationWindow dialog)
    {
        options.SetPromptOpen?.Invoke(true);
        try
        {
            return await dialog.ShowDialog<bool>(options.Owner);
        }
        finally
        {
            options.SetPromptOpen?.Invoke(false);
            options.PromptClosed?.Invoke();
        }
    }

    private static string Localize(TrayAppDotNETUpdatePromptOptions options, string key, string fallback) =>
        options.Localize(key, fallback);
}
