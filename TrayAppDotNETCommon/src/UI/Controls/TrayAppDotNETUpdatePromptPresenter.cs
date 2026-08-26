using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed record TrayAppDotNETUpdatePromptOptions
{
    public required Window Owner { get; init; }
    public required UpdateCheckService Service { get; init; }
    public required UpdateInfo UpdateInfo { get; init; }
    public required SettingsPalette Palette { get; init; }
    public Color OwnerBackdrop { get; init; } =
        AppTheme.Default.FlyoutOverlayBackdrop.For(AppTheme.Default.IsLightTheme);
    public required bool EnableRoundedCorners { get; init; }
    public required Func<string, string> L { get; init; }
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
            L(options, nameof(CommonStrings.UpdateDialog_FailedTitle)),
            L(options, nameof(CommonStrings.UpdateDialog_DownloadFailed)));
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
            L(options, nameof(CommonStrings.BackdateDialog_FailedTitle)),
            L(options, nameof(CommonStrings.BackdateDialog_DownloadFailed)));
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
        string title = L(options, nameof(CommonStrings.UpdateDialog_Title));
        string description = string.Format(
            CultureInfo.CurrentCulture,
            L(options, nameof(CommonStrings.UpdateDialog_AppFormat)),
            options.Service.ApplicationName);
        string newVersionText = string.Format(
            CultureInfo.CurrentCulture,
            L(options, nameof(CommonStrings.UpdateDialog_NewVersionFormat)),
            options.UpdateInfo.Version);
        string currentVersionText = string.Format(
            CultureInfo.CurrentCulture,
            L(options, nameof(CommonStrings.UpdateDialog_CurrentVersionFormat)),
            options.Service.CurrentBuild);
        string confirmText = L(options, nameof(CommonStrings.UpdateDialog_Install));
        string skipText = L(options, nameof(CommonStrings.UpdateDialog_SkipRelease));
        string closeText = L(options, nameof(CommonStrings.UpdateDialog_Close));
        string releasesLinkText = L(options, nameof(CommonStrings.UpdateDialog_ViewReleases));
        string websiteLinkText = L(options, nameof(CommonStrings.UpdateDialog_VisitWebsite));
        string restartNotice = L(options, nameof(CommonStrings.UpdateDialog_RestartNotice));

        TrayAppDotNETUpdateConfirmationWindow dialog = new(
            title,
            description,
            confirmText,
            options.Palette,
            options.EnableRoundedCorners,
            skipText,
            closeText,
            modalDetails: new TrayAppDotNETUpdateModalDetails(
                newVersionText,
                currentVersionText,
                releasesLinkText,
                options.Service.ReleasesPageUrl,
                websiteLinkText),
            modalFooterText: restartNotice,
            useModalContentLayout: true)
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
            L(options, nameof(CommonStrings.BackdateDialog_TitleFormat)),
            options.UpdateInfo.Version);
        string description = string.Format(
            CultureInfo.CurrentCulture,
            L(options, nameof(CommonStrings.BackdateDialog_DescriptionFormat)),
            options.UpdateInfo.Version);
        TrayAppDotNETUpdateConfirmationWindow dialog = new(
            title,
            description,
            L(options, nameof(CommonStrings.BackdateDialog_Confirm)),
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
            L(options, nameof(CommonStrings.BackdateDialog_SkipCurrentDescriptionFormat)),
            options.Service.CurrentBuild);
        TrayAppDotNETUpdateConfirmationWindow dialog = new(
            L(options, nameof(CommonStrings.BackdateDialog_SkipCurrentTitle)),
            description,
            L(options, nameof(CommonStrings.BackdateDialog_Yes)),
            options.Palette,
            options.EnableRoundedCorners,
            L(options, nameof(CommonStrings.BackdateDialog_No)))
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
        string okText = L(options, nameof(CommonStrings.SettingsWindow_ConfirmOverlay_OK));

        TrayAppDotNETUpdateConfirmationWindow dialog = new(
            title,
            message,
            okText,
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
        UpdatePromptOwnerBackdrop? ownerBackdrop = null;
        try
        {
            try
            {
                ownerBackdrop = UpdatePromptOwnerBackdrop.Attach(options.Owner, options.OwnerBackdrop);
            }
            catch (Exception exception)
            {
                options.Log($"TrayAppDotNETUpdatePromptPresenter.OwnerBackdrop: {exception.Message}");
            }

            return await dialog.ShowDialog<TrayAppDotNETUpdatePromptResult>(options.Owner);
        }
        finally
        {
            try
            {
                ownerBackdrop?.Dispose();
            }
            finally
            {
                options.SetPromptOpen?.Invoke(false);
                options.PromptClosed?.Invoke();
            }
        }
    }

    private static string L(TrayAppDotNETUpdatePromptOptions options, string key) =>
        options.L(key);
}

internal sealed class UpdatePromptOwnerBackdrop : IDisposable
{
    private readonly Window _owner;
    private readonly Control _originalContent;
    private readonly Grid _host;
    private readonly Border _backdrop;
    private int _disposeState;

    private UpdatePromptOwnerBackdrop(
        Window owner,
        Control originalContent,
        Grid host,
        Border backdrop)
    {
        _owner = owner;
        _originalContent = originalContent;
        _host = host;
        _backdrop = backdrop;
    }

    /// <summary>Adds a non-interactive backdrop over the complete owner client area.</summary>
    public static UpdatePromptOwnerBackdrop? Attach(Window owner, Color color)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (owner.Content is not Control originalContent) return null;

        Border backdrop = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(color),
            CornerRadius = originalContent is Border border ? border.CornerRadius : default,
            Focusable = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ZIndex = int.MaxValue
        };
        Grid host = new();
        owner.Content = null;
        try
        {
            host.Children.Add(originalContent);
            host.Children.Add(backdrop);
            owner.Content = host;
        }
        catch
        {
            host.Children.Remove(backdrop);
            host.Children.Remove(originalContent);
            owner.Content = originalContent;
            throw;
        }

        return new UpdatePromptOwnerBackdrop(owner, originalContent, host, backdrop);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;

        _host.Children.Remove(_backdrop);
        if (!ReferenceEquals(_owner.Content, _host))
        {
            _host.Children.Remove(_originalContent);
            return;
        }

        _owner.Content = null;
        _host.Children.Remove(_originalContent);
        _owner.Content = _originalContent;
    }
}
