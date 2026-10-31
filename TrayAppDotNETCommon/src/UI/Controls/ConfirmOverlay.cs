using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.ControlMapping;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed record TrayAppDotNETConfirmOverlayOptions
{
    public required SettingsPalette Palette { get; init; }

    /// <summary>Gets the colour laid over the window content behind the dialog.</summary>
    public required Color Backdrop { get; init; }

    public required bool EnableRoundedCorners { get; init; }

    /// <summary>Gets the corner radius of the backdrop, which should follow the window frame.</summary>
    public CornerRadius BackdropCornerRadius { get; init; }

    /// <summary>
    /// Gets the app's scope that instantiates the FlyoutConfirmOverlay template. An app whose map instantiates it
    /// once can leave it unset; the template root id then resolves.
    /// </summary>
    public ControlMapNodeID? Node { get; init; }
}

/// <summary>
/// In-window confirmation over a window's content. While shown it holds keyboard focus on Confirm, so Enter or Space
/// confirms and Escape cancels, and it returns focus to the previously focused control when it closes.
/// </summary>
public sealed class TrayAppDotNETConfirmOverlay : Border, IDisposable
{
    private readonly TextBlock _title;
    private readonly TextBlock _message;
    private readonly SettingsButton _confirm;
    private readonly SettingsButton _cancel;
    private TaskCompletionSource<bool>? _completion;
    private Control? _previousFocus;
    private bool _disposed;

    public TrayAppDotNETConfirmOverlay(TrayAppDotNETConfirmOverlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        SettingsPalette p = options.Palette;
        FlyoutCardsResources.FlyoutCardsAxamlProperties layout = FlyoutCardsResources.Current.AxamlFlyoutCards;

        _title = TrayAppDotNETSettingsUI.Text(string.Empty, p, layout.ConfirmTitleFontSize, FontWeight.SemiBold);
        _title.TextWrapping = TextWrapping.Wrap;
        _title.Margin = layout.ConfirmTitleMargin;
        _message = TrayAppDotNETSettingsUI.DescriptionText(string.Empty, p, layout.ConfirmMessageMargin);
        _message.TextWrapping = TextWrapping.Wrap;
        _confirm = TrayAppDotNETSettingsUI.Button(string.Empty, p).MapTo(ControlMap.FlyoutConfirmOverlay.Confirm);
        _cancel = TrayAppDotNETSettingsUI.Button(string.Empty, p).MapTo(ControlMap.FlyoutConfirmOverlay.Cancel);
        _cancel.Margin = layout.ConfirmCancelMargin;
        _confirm.Click += OnConfirmClick;
        _cancel.Click += OnCancelClick;

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { _cancel, _confirm }
        };
        Border dialog = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(p.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(p.Border),
            BorderThickness = layout.ConfirmBorderThickness,
            CornerRadius = options.EnableRoundedCorners ? layout.ConfirmCornerRadius : new CornerRadius(0),
            Padding = layout.ConfirmPadding,
            Margin = layout.ConfirmMargin,
            MinWidth = layout.ConfirmMinWidth,
            MaxWidth = layout.ConfirmMaxWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Children = { _title, _message, buttons } }
        };

        Background = TrayAppDotNETSettingsUI.Brush(options.Backdrop);
        CornerRadius = options.BackdropCornerRadius;
        Child = dialog;
        IsVisible = false;
        if (options.Node is { } node) this.MapTo(node);
        else this.MapTo(ControlMap.FlyoutConfirmOverlay.ID);

        // Keys that nothing inside the dialog used must not reach the content behind the backdrop
        KeyDown += OnKeyDown;
    }

    /// <summary>Gets whether a confirmation is showing.</summary>
    public bool IsOpen => _completion != null;

    /// <summary>
    /// Shows a confirmation and completes with whether it was confirmed. A null <paramref name="cancelText"/> shows a
    /// single-button message. Showing another confirmation cancels the current one.
    /// </summary>
    public Task<bool> ShowAsync(string title, string message, string confirmText, string? cancelText)
    {
        if (_disposed || VisualRoot == null) return Task.FromResult(false);

        Cancel();
        _previousFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        _title.Text = title;
        _message.Text = message;
        _confirm.Text = confirmText;
        _cancel.Text = cancelText ?? string.Empty;
        _cancel.IsVisible = cancelText != null;
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _completion = completion;
        IsVisible = true;
        _confirm.Focus();
        return completion.Task;
    }

    /// <summary>Closes an open confirmation as declined.</summary>
    public void Cancel() => Complete(result: false);

    public void Dispose()
    {
        if (_disposed) return;
        Cancel();
        _disposed = true;
        _confirm.Click -= OnConfirmClick;
        _cancel.Click -= OnCancelClick;
        KeyDown -= OnKeyDown;
    }

    private void OnConfirmClick(object? sender, EventArgs e) => Complete(result: true);

    private void OnCancelClick(object? sender, EventArgs e) => Complete(result: false);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsOpen) e.Handled = true;
    }

    private void Complete(bool result)
    {
        TaskCompletionSource<bool>? completion = _completion;
        if (completion == null) return;

        _completion = null;
        IsVisible = false;
        Control? previousFocus = _previousFocus;
        _previousFocus = null;
        if (previousFocus is { IsEffectivelyVisible: true }) previousFocus.Focus();
        completion.TrySetResult(result);
    }
}
