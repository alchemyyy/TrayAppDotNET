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

        TrayAppDotNETUpdatePromptResult promptResult = await ShowConfirmationAsync(options);
        switch (promptResult)
        {
            case TrayAppDotNETUpdatePromptResult.Cancelled:
                return false;
            case TrayAppDotNETUpdatePromptResult.Alternate:
                try
                {
                    await options.Service.SkipReleaseAsync(options.UpdateInfo);
                }
                catch (Exception ex)
                {
                    options.Log($"TrayAppDotNETUpdatePromptPresenter.SkipReleaseAsync: {ex.Message}");
                }
                return false;
            case TrayAppDotNETUpdatePromptResult.Confirmed:
                break;
            default:
                return false;
        }

        return await StageAndShutdownAsync(
            options,
            "TrayAppDotNETUpdatePromptPresenter.ShowInstallUpdateAsync",
            Localize(options, "Settings_About_InstallUpdate_CheckFailed", "Update failed"),
            Localize(
                options,
                "UpdateDialog_DownloadFailed",
                "The update could not be downloaded. Check the log for details."));
    }

    /// <summary>Confirms and installs an older release, including the current-version skip choice.</summary>
    public static async Task<bool> ShowBackdateAsync(TrayAppDotNETUpdatePromptOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        TrayAppDotNETUpdatePromptResult confirmationResult = await ShowBackdateConfirmationAsync(options);
        if (confirmationResult != TrayAppDotNETUpdatePromptResult.Confirmed) return false;

        TrayAppDotNETUpdatePromptResult skipResult = await ShowBackdateSkipPromptAsync(options);
        bool isCurrentVersionSkipped;
        switch (skipResult)
        {
            case TrayAppDotNETUpdatePromptResult.Confirmed:
                isCurrentVersionSkipped = true;
                break;
            case TrayAppDotNETUpdatePromptResult.Alternate:
                isCurrentVersionSkipped = false;
                break;
            case TrayAppDotNETUpdatePromptResult.Cancelled:
            default:
                return false;
        }

        try
        {
            await options.Service.SetCurrentVersionSkippedAsync(isCurrentVersionSkipped);
        }
        catch (Exception exception)
        {
            options.Log($"TrayAppDotNETUpdatePromptPresenter.SetCurrentVersionSkippedAsync: {exception.Message}");
            return false;
        }

        return await StageAndShutdownAsync(
            options,
            "TrayAppDotNETUpdatePromptPresenter.ShowBackdateAsync",
            Localize(options, "BackdateDialog_FailedTitle", "Backdate failed"),
            Localize(
                options,
                "BackdateDialog_DownloadFailed",
                "The previous version could not be downloaded. Check the log for details."));
    }

    private static async Task<bool> StageAndShutdownAsync(
        TrayAppDotNETUpdatePromptOptions options,
        string operationName,
        string failureTitle,
        string failureMessage)
    {
        options.SetDownloadInFlight?.Invoke(true);
        bool staged = false;
        try
        {
            staged = await options.Service.DownloadAndStageAsync(options.UpdateInfo);
        }
        catch (Exception ex)
        {
            options.Log($"{operationName}: {ex.Message}");
        }

        if (staged)
        {
            options.FlushLog();
            options.Shutdown();
            return true;
        }

        options.SetDownloadInFlight?.Invoke(false);
        if (options.ShowFailurePrompt)
            await ShowFailureAsync(options, failureTitle, failureMessage);
        return false;
    }

    private static async Task<TrayAppDotNETUpdatePromptResult> ShowConfirmationAsync(
        TrayAppDotNETUpdatePromptOptions options)
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
        string skipText = Localize(options, "UpdateDialog_SkipRelease", "Skip this release");

        TrayAppDotNETUpdateConfirmationWindow dialog = new(
            title,
            description,
            changelog,
            confirmText,
            cancelText,
            options.Palette,
            options.EnableRoundedCorners,
            skipText)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        return await ShowPromptAsync(options, dialog);
    }

    private static async Task<TrayAppDotNETUpdatePromptResult> ShowBackdateConfirmationAsync(
        TrayAppDotNETUpdatePromptOptions options)
    {
        string title = string.Format(
            CultureInfo.CurrentCulture,
            Localize(options, "BackdateDialog_TitleFormat", "Backdate to {0}?"),
            options.UpdateInfo.Version);
        string description = string.Format(
            CultureInfo.CurrentCulture,
            Localize(
                options,
                "BackdateDialog_DescriptionFormat",
                "This will replace the current version with {0} and restart the app."),
            options.UpdateInfo.Version);
        string changelog = string.IsNullOrWhiteSpace(options.UpdateInfo.Changelog)
            ? Localize(options, "UpdateDialog_NoChangelog", "No changelog provided.")
            : options.UpdateInfo.Changelog;
        TrayAppDotNETUpdateConfirmationWindow dialog = new(
            title,
            description,
            changelog,
            Localize(options, "BackdateDialog_Confirm", "Backdate"),
            Localize(options, "UpdateDialog_Cancel", "Cancel"),
            options.Palette,
            options.EnableRoundedCorners)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        return await ShowPromptAsync(options, dialog);
    }

    private static async Task<TrayAppDotNETUpdatePromptResult> ShowBackdateSkipPromptAsync(
        TrayAppDotNETUpdatePromptOptions options)
    {
        string description = string.Format(
            CultureInfo.CurrentCulture,
            Localize(
                options,
                "BackdateDialog_SkipCurrentDescriptionFormat",
                "After backdating, version {0} will be offered as an update. Do you want to skip it?"),
            options.Service.CurrentBuild);
        TrayAppDotNETUpdateConfirmationWindow dialog = new(
            Localize(options, "BackdateDialog_SkipCurrentTitle", "Skip the current version?"),
            description,
            null,
            Localize(options, "BackdateDialog_Yes", "Yes"),
            null,
            options.Palette,
            options.EnableRoundedCorners,
            Localize(options, "BackdateDialog_No", "No"))
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        return await ShowPromptAsync(options, dialog);
    }

    private static async Task ShowFailureAsync(
        TrayAppDotNETUpdatePromptOptions options,
        string title,
        string message)
    {
        string okText = Localize(
            options,
            "SettingsWindow_ConfirmOverlay_OK",
            "OK");

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

    private static async Task<TrayAppDotNETUpdatePromptResult> ShowPromptAsync(
        TrayAppDotNETUpdatePromptOptions options,
        TrayAppDotNETUpdateConfirmationWindow dialog)
    {
        options.SetPromptOpen?.Invoke(true);
        try
        {
            return await dialog.ShowDialog<TrayAppDotNETUpdatePromptResult>(options.Owner);
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
