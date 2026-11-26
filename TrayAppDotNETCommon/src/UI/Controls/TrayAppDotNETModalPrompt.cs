using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TrayAppDotNETCommon.UI.Controls;

/// <summary>
/// Runs prompt flows over an owner window, one flow per owner at a time, and shows each prompt the way the update
/// prompt shows: modal over the owner, in a flyout's upper third, with the owner dimmed behind it.
/// </summary>
public static class TrayAppDotNETModalPrompt
{
    // UI-thread only: owners with a prompt flow running, such as an open prompt or a confirmed update download
    private static readonly HashSet<Window> BusyOwners = [];

    /// <summary>Gets whether a prompt flow for the owner is running.</summary>
    public static bool IsBusy(Window owner) => BusyOwners.Contains(owner);

    /// <summary>
    /// Runs <paramref name="flow"/> as the owner's only prompt flow and returns its result, or returns false at once
    /// while another flow for the owner runs. <paramref name="busyChanged"/> is raised as the flow starts and ends.
    /// </summary>
    public static async Task<bool> RunExclusiveAsync(
        Window owner,
        Func<Task<bool>> flow,
        Action<bool>? busyChanged = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(flow);
        IDisposable? busy = TryBeginBusy(owner);
        if (busy == null) return false;

        busyChanged?.Invoke(true);
        try
        {
            return await flow();
        }
        finally
        {
            // The owner refreshes from IsBusy, so the state clears before the callback
            busy.Dispose();
            busyChanged?.Invoke(false);
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

    /// <summary>
    /// Shows <paramref name="dialog"/> modally over <paramref name="owner"/>, which <paramref name="ownerBackdrop"/>
    /// dims meanwhile. Over a flyout the dialog sits in the flyout's upper third and closes when the flyout hides. An
    /// owner hidden or closed meanwhile, such as while a confirmed download ran, cannot host the dialog, so it then
    /// opens on its own, centered and topmost.
    /// </summary>
    public static async Task<TrayAppDotNETConfirmationResult> ShowAsync(
        Window owner,
        TrayAppDotNETConfirmationWindow dialog,
        Color ownerBackdrop,
        Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(log);

        if (!owner.IsVisible)
        {
            log("TrayAppDotNETModalPrompt: the owner window is gone; the prompt opens on its own.");
            return await ShowUnownedAsync(dialog);
        }

        if (owner is FlyoutWindowCommon)
            dialog.PlaceOverOwnerUpperThird(owner);

        ModalPromptOwnerBackdrop? backdrop = null;
        try
        {
            try
            {
                backdrop = ModalPromptOwnerBackdrop.Attach(owner, ownerBackdrop);
            }
            catch (Exception exception)
            {
                log($"TrayAppDotNETModalPrompt.OwnerBackdrop: {exception.Message}");
            }

            // A flyout closes the dialog when it hides and keeps itself open while the dialog is up
            if (owner is FlyoutWindowCommon flyout)
                return await flyout.ShowOwnedDialogAsync<TrayAppDotNETConfirmationResult>(dialog);

            return await ShowAndCloseAsync(owner, dialog);
        }
        finally
        {
            backdrop?.Dispose();
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
}

internal sealed class ModalPromptOwnerBackdrop : IDisposable
{
    private readonly Window _owner;
    private readonly Control _originalContent;
    private readonly Grid _host;
    private readonly Border _backdrop;
    private int _disposeState;

    private ModalPromptOwnerBackdrop(
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
    public static ModalPromptOwnerBackdrop? Attach(Window owner, Color color)
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

        return new ModalPromptOwnerBackdrop(owner, originalContent, host, backdrop);
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
