using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.UI.ControlMapping;
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

    /// <summary>
    /// Gets a callback raised when a prompt or download for the owner starts or ends, so the owner can refresh the
    /// controls that start one; <see cref="TrayAppDotNETUpdatePromptPresenter.IsBusy"/> holds the state.
    /// </summary>
    public Action<bool>? BusyChanged { get; init; }

    public Action<bool>? SetDownloadInFlight { get; init; }
    public bool ShowFailurePrompt { get; init; } = true;

    /// <summary>
    /// Gets the app's UpdateConfirmation surface that every prompt of this flow is tagged with. An app whose map
    /// instantiates UpdateConfirmation more than once sets it; otherwise the template root id resolves.
    /// </summary>
    public ControlMapNodeID? Node { get; init; }
}

/// <summary>
/// Presents one shared update-install confirmation and staging flow.
/// </summary>
public static class TrayAppDotNETUpdatePromptPresenter
{
    // UI-thread only: owners with a prompt open or a download in flight
    private static readonly HashSet<Window> BusyOwners = [];

    /// <summary>Gets whether a prompt for the owner is open or its download is in flight.</summary>
    public static bool IsBusy(Window owner) => BusyOwners.Contains(owner);

    /// <summary>
    /// Confirms, stages, and shuts down after a successful update handoff. Returns false at once while another flow
    /// for the same owner is open or downloading.
    /// </summary>
    public static Task<bool> ShowInstallUpdateAsync(TrayAppDotNETUpdatePromptOptions options) =>
        RunExclusiveAsync(options, ShowInstallUpdateCoreAsync);

    private static async Task<bool> ShowInstallUpdateCoreAsync(TrayAppDotNETUpdatePromptOptions options)
    {
        while (true)
        {
            TrayAppDotNETConfirmationResult promptResult = await ShowConfirmationAsync(options);
            switch (promptResult)
            {
                case TrayAppDotNETConfirmationResult.Confirmed:
                    return await StageAndShutdownAsync(
                        options,
                        operationName: "TrayAppDotNETUpdatePromptPresenter.ShowInstallUpdateAsync",
                        L(options, nameof(CommonStrings.UpdateDialog_FailedTitle)),
                        L(options, nameof(CommonStrings.UpdateDialog_DownloadFailed)));
                case TrayAppDotNETConfirmationResult.Alternate:
                    if (await ShowSkipConfirmationAsync(options) == TrayAppDotNETConfirmationResult.Confirmed)
                    {
                        await SkipReleaseAsync(options);
                        return false;
                    }

                    // Declining the skip returns to the update prompt while its release is still the one offered
                    if (options.Service.AvailableUpdate != options.UpdateInfo) return false;
                    continue;
                case TrayAppDotNETConfirmationResult.Cancelled:
                default:
                    return false;
            }
        }
    }

    /// <summary>Confirms and persists skipping a release; returns whether the release is now skipped.</summary>
    public static Task<bool> ShowSkipReleaseAsync(TrayAppDotNETUpdatePromptOptions options) =>
        RunExclusiveAsync(options, static async options =>
            await ShowSkipConfirmationAsync(options) == TrayAppDotNETConfirmationResult.Confirmed
            && await SkipReleaseAsync(options));

    private static async Task<bool> RunExclusiveAsync(
        TrayAppDotNETUpdatePromptOptions options,
        Func<TrayAppDotNETUpdatePromptOptions, Task<bool>> flow)
    {
        ArgumentNullException.ThrowIfNull(options);
        IDisposable? busy = TryBeginBusy(options.Owner);
        if (busy == null) return false;

        options.BusyChanged?.Invoke(true);
        try
        {
            return await flow(options);
        }
        finally
        {
            // The owner refreshes from IsBusy, so the state clears before the callback
            busy.Dispose();
            options.BusyChanged?.Invoke(false);
        }
    }

    /// <summary>
    /// Marks the owner busy until the returned scope is disposed, or returns null while another flow holds it.
    /// </summary>
    internal static IDisposable? TryBeginBusy(Window owner) =>
        BusyOwners.Add(owner) ? new BusyScope(owner) : null;

    private sealed class BusyScope(Window owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            BusyOwners.Remove(owner);
        }
    }

    private static async Task<bool> SkipReleaseAsync(TrayAppDotNETUpdatePromptOptions options)
    {
        try
        {
            await options.Service.SkipReleaseAsync(options.UpdateInfo);
            return true;
        }
        catch (Exception exception)
        {
            options.Log($"TrayAppDotNETUpdatePromptPresenter.SkipReleaseAsync: {exception.Message}");
            return false;
        }
    }

    /// <summary>Confirms and installs an older release, including the current-version skip choice.</summary>
    public static Task<bool> ShowBackdateAsync(TrayAppDotNETUpdatePromptOptions options) =>
        RunExclusiveAsync(options, ShowBackdateCoreAsync);

    private static async Task<bool> ShowBackdateCoreAsync(TrayAppDotNETUpdatePromptOptions options)
    {
        TrayAppDotNETConfirmationResult confirmationResult = await ShowBackdateConfirmationAsync(options);
        if (confirmationResult != TrayAppDotNETConfirmationResult.Confirmed) return false;

        TrayAppDotNETConfirmationResult skipResult = await ShowBackdateSkipPromptAsync(options);
        bool isCurrentVersionSkipped;
        switch (skipResult)
        {
            case TrayAppDotNETConfirmationResult.Confirmed:
                isCurrentVersionSkipped = true;
                break;
            case TrayAppDotNETConfirmationResult.Alternate:
                isCurrentVersionSkipped = false;
                break;
            case TrayAppDotNETConfirmationResult.Cancelled:
            default:
                return false;
        }

        // The skip choice is saved only once the installer has taken over, so a failed backdate keeps an earlier skip
        return await StageAndShutdownAsync(
            options,
            operationName: "TrayAppDotNETUpdatePromptPresenter.ShowBackdateAsync",
            L(options, nameof(CommonStrings.BackdateDialog_FailedTitle)),
            L(options, nameof(CommonStrings.BackdateDialog_DownloadFailed)),
            beforeShutdown: () => PersistBackdateSkipAsync(options, isCurrentVersionSkipped));
    }

    private static async Task PersistBackdateSkipAsync(TrayAppDotNETUpdatePromptOptions options, bool isSkipped)
    {
        try
        {
            await options.Service.SetCurrentVersionSkippedAsync(isSkipped);
        }
        catch (Exception exception)
        {
            // The installer already owns the update; the app shuts down either way
            options.Log($"TrayAppDotNETUpdatePromptPresenter.SetCurrentVersionSkippedAsync: {exception.Message}");
        }
    }

    private static async Task<bool> StageAndShutdownAsync(
        TrayAppDotNETUpdatePromptOptions options,
        string operationName,
        string failureTitle,
        string failureMessage,
        Func<Task>? beforeShutdown = null)
    {
        options.SetDownloadInFlight?.Invoke(true);
        UpdateStageResult result = UpdateStageResult.Failed;

        // A confirmed update runs to the end even when its owner window hides or closes: no cancel control exists,
        // and the service's inactivity timeout ends a stalled download
        try
        {
            result = await options.Service.DownloadAndStageAsync(options.UpdateInfo);
        }
        catch (Exception ex)
        {
            options.Log($"{operationName}: {ex.Message}");
        }

        if (result == UpdateStageResult.HandedOff)
        {
            if (beforeShutdown != null) await beforeShutdown();
            options.FlushLog();
            options.Shutdown();
            return true;
        }

        options.SetDownloadInFlight?.Invoke(false);
        if (options.ShowFailurePrompt)
        {
            string message = result switch
            {
                UpdateStageResult.AnotherUpdateRunning => L(options, nameof(CommonStrings.UpdateDialog_AnotherUpdateRunning)),
                UpdateStageResult.AdministratorAccountRequired =>
                    L(options, nameof(CommonStrings.Elevation_Error_DifferentAdministratorAccount)),
                _ => failureMessage
            };
            await ShowFailureAsync(options, failureTitle, message);
        }

        return false;
    }

    private static async Task<TrayAppDotNETConfirmationResult> ShowConfirmationAsync(
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

        TrayAppDotNETConfirmationWindow dialog = new(
            title,
            description,
            confirmText,
            options.Palette,
            options.EnableRoundedCorners,
            skipText,
            closeText,
            new TrayAppDotNETUpdateModalDetails(
                newVersionText,
                currentVersionText,
                releasesLinkText,
                options.Service.ReleasesPageUrl,
                websiteLinkText),
            restartNotice,
            useModalContentLayout: true) { WindowStartupLocation = WindowStartupLocation.CenterOwner };
        return await ShowPromptAsync(options, dialog);
    }

    private static async Task<TrayAppDotNETConfirmationResult> ShowSkipConfirmationAsync(
        TrayAppDotNETUpdatePromptOptions options)
    {
        string title = string.Format(
            CultureInfo.CurrentCulture,
            L(options, nameof(CommonStrings.SkipDialog_TitleFormat)),
            options.UpdateInfo.Version);
        string description = string.Format(
            CultureInfo.CurrentCulture,
            L(options, nameof(CommonStrings.SkipDialog_DescriptionFormat)),
            options.UpdateInfo.Version);
        TrayAppDotNETConfirmationWindow dialog = new(
            title,
            description,
            L(options, nameof(CommonStrings.SkipDialog_Confirm)),
            options.Palette,
            options.EnableRoundedCorners,
            cancelText: L(options, nameof(CommonStrings.SettingsWindow_ConfirmOverlay_Cancel)))
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        return await ShowPromptAsync(options, dialog);
    }

    private static async Task<TrayAppDotNETConfirmationResult> ShowBackdateConfirmationAsync(
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
        TrayAppDotNETConfirmationWindow dialog = new(
            title,
            description,
            L(options, nameof(CommonStrings.BackdateDialog_Confirm)),
            options.Palette,
            options.EnableRoundedCorners) { WindowStartupLocation = WindowStartupLocation.CenterOwner };
        return await ShowPromptAsync(options, dialog);
    }

    private static async Task<TrayAppDotNETConfirmationResult> ShowBackdateSkipPromptAsync(
        TrayAppDotNETUpdatePromptOptions options)
    {
        string description = string.Format(
            CultureInfo.CurrentCulture,
            L(options, nameof(CommonStrings.BackdateDialog_SkipCurrentDescriptionFormat)),
            options.Service.CurrentBuild);
        TrayAppDotNETConfirmationWindow dialog = new(
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

        TrayAppDotNETConfirmationWindow dialog = new(
            title,
            message,
            okText,
            options.Palette,
            options.EnableRoundedCorners) { WindowStartupLocation = WindowStartupLocation.CenterOwner };
        _ = await ShowPromptAsync(options, dialog);
    }

    private static async Task<TrayAppDotNETConfirmationResult> ShowPromptAsync(
        TrayAppDotNETUpdatePromptOptions options,
        TrayAppDotNETConfirmationWindow dialog)
    {
        dialog.MapTo(options.Node);

        // An owner that was hidden or closed while a confirmed download ran cannot host the result; it still shows
        if (!options.Owner.IsVisible)
        {
            options.Log("TrayAppDotNETUpdatePromptPresenter: the owner window is gone; the prompt opens on its own.");
            return await ShowUnownedAsync(dialog);
        }

        if (options.Owner is FlyoutWindowCommon)
            dialog.PlaceOverOwnerUpperThird(options.Owner);

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

            // A flyout closes the dialog when it hides and keeps itself open while the dialog is up
            if (options.Owner is FlyoutWindowCommon flyout)
                return await flyout.ShowOwnedDialogAsync<TrayAppDotNETConfirmationResult>(dialog);

            return await ShowAndCloseAsync(options.Owner, dialog);
        }
        finally
        {
            ownerBackdrop?.Dispose();
        }
    }

    private static async Task<TrayAppDotNETConfirmationResult> ShowAndCloseAsync(
        Window owner,
        TrayAppDotNETConfirmationWindow dialog)
    {
        bool closed = false;
        EventHandler onClosed = (_, _) => closed = true;
        dialog.Closed += onClosed;
        try
        {
            return await dialog.ShowDialog<TrayAppDotNETConfirmationResult>(owner);
        }
        finally
        {
            dialog.Closed -= onClosed;

            // Hiding the owner completes the modal task with the dialog only hidden, not closed
            if (!closed) dialog.Close();
        }
    }

    private static Task<TrayAppDotNETConfirmationResult> ShowUnownedAsync(TrayAppDotNETConfirmationWindow dialog)
    {
        TaskCompletionSource<TrayAppDotNETConfirmationResult> completion = new();
        EventHandler? onClosed = null;
        onClosed = (_, _) =>
        {
            dialog.Closed -= onClosed;
            completion.TrySetResult(dialog.Result);
        };
        dialog.Closed += onClosed;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.Topmost = true;
        dialog.Show();
        return completion.Task;
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
        if (Interlocked.Exchange(ref _disposeState, value: 1) != 0) return;

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
