using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using TrayAppDotNETCommon.UI.WarmWindows;

namespace TrayAppDotNETCommon.UI;

public abstract class FlyoutWindowCommon : Window, ITrayAppDotNETWarmWindow
{
    private static readonly PixelPoint HiddenPosition = new(
        TrayAppDotNETWarmWindowDefaults.OffscreenPosition,
        TrayAppDotNETWarmWindowDefaults.OffscreenPosition);

    private readonly UIResourceScope _windowResources;
    private readonly HashSet<FlyoutCompanionWindow> _companionWindows = [];
    private UIContentGeneration? _activeContentGeneration;
    private double? _fixedLogicalWidth;
    private bool _focusGroupEvaluationQueued;
    private bool _scalingLayoutCorrectionQueued;
    private bool _hasRetainedFrame;

    public bool KeepOpenForSettingsWindow { get; set; }
    public bool IsWarmPriming { get; set; }
    public bool IsManagedByWarmSlot { get; set; }

    public event EventHandler? WarmDismissed;

    private bool _suppressNextAutoHide;

    protected FlyoutWindowCommon()
    {
        _windowResources = new UIResourceScope(GetType().Name);
        ControlNames = ControlNameScope.For(this);
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        CanResize = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Opacity = 0;
        Position = HiddenPosition;
        Deactivated += OnDeactivated;
        _windowResources.Add(() => Deactivated -= OnDeactivated);
        ScalingChanged += OnScalingChanged;
        _windowResources.Add(() => ScalingChanged -= OnScalingChanged);
    }

    protected virtual bool HasOpenChildWindow => false;

    protected virtual bool ShouldAutoHideWhenDeactivated => true;

    protected virtual void HideFlyout() => Hide();

    /// <summary>Gets the resources owned for the complete flyout-window lifetime.</summary>
    protected UIResourceScope WindowResources => _windowResources;

    /// <summary>Gets the currently active replaceable content generation.</summary>
    protected UIContentGeneration? ActiveContentGeneration => _activeContentGeneration;

    /// <summary>Gets the source-level control naming scope for this flyout instance.</summary>
    protected ControlNameScope ControlNames { get; }

    /// <summary>Fixes the logical flyout width across native per-monitor DPI resize notifications.</summary>
    protected void SetFixedFlyoutWidth(double logicalWidth)
    {
        if (!double.IsFinite(logicalWidth) || logicalWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(logicalWidth));

        _fixedLogicalWidth = logicalWidth;
        ReapplyFixedFlyoutWidth();
    }

    /// <summary>Gets whether the native surface still holds the frame of an earlier reveal.</summary>
    protected bool HasRetainedFrame => _hasRetainedFrame;

    /// <summary>
    /// Shows the flyout at a position resolved for its current content size.
    /// After the first reveal the flyout keeps full opacity, so the compositor presents the retained frame at once and
    /// content published before the next render replaces it directly.
    /// A flyout that has never been revealed stays transparent until <see cref="CompleteReveal"/>.
    /// </summary>
    protected void ShowWithRetainedFrame(PixelPoint position)
    {
        if (IsVisible) return;

        // NOTE: Opacity only takes effect in a rendered frame, so hiding a revealed flyout must never zero it.
        // The first frame after the next show would then paint transparent over the retained frame.
        Opacity = _hasRetainedFrame ? 1 : 0;
        Position = position;
        ReapplyFixedFlyoutWidth();
        RestoreAutomaticHeightSizing();
        Show();
    }

    /// <summary>Reveals a settled flyout and lets later shows present its retained frame immediately.</summary>
    protected void CompleteReveal()
    {
        Opacity = 1;
        _hasRetainedFrame = true;
    }

    /// <summary>Clears a realized height so height-to-content layout can measure replacement content.</summary>
    protected void RestoreAutomaticHeightSizing()
    {
        if ((SizeToContent & SizeToContent.Height) == 0) return;

        // Avalonia writes the native client height back after showing the window
        Height = double.NaN;
        InvalidateMeasure();
    }

    /// <summary>Reapplies monitor-dependent flyout constraints before a DPI correction layout pass.</summary>
    protected virtual void ApplyRenderScalingLayoutConstraints()
    {
    }

    /// <summary>
    /// Publishes a completely built generation, then retires the previous generation.
    /// </summary>
    protected void CommitContentGeneration(UIContentGeneration replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.IsDisposed)
            throw new ObjectDisposedException(replacement.OwnerName);

        UIContentGeneration? previous = _activeContentGeneration;
        try
        {
            ControlNames.AssignLogicalSubtree(replacement.Root, this);
            Content = replacement.Root;
            _activeContentGeneration = replacement;
        }
        catch
        {
            replacement.Dispose();
            throw;
        }

        previous?.Dispose();
    }

    /// <summary>Detaches and retires the active content generation.</summary>
    protected void DisposeContentGeneration()
    {
        UIContentGeneration? generation = Interlocked.Exchange(ref _activeContentGeneration, value: null);
        if (generation == null) return;

        try
        {
            if (!generation.IsDisposed && ReferenceEquals(Content, generation.Root))
                Content = null;
        }
        finally
        {
            generation.Dispose();
        }
    }

    /// <summary>
    /// Keeps the deactivation caused by a control that opens another window from hiding the flyout. A left press
    /// arms the suppression, and so do Enter and Space while the control has focus, which activate it through its
    /// own key handler or the control map.
    /// </summary>
    protected void SuppressNextAutoHideWhenPressed(Control control)
    {
        control.AddHandler(
            PointerPressedEvent,
            (_, e) =>
            {
                if (!control.IsEnabled) return;
                if (e.GetCurrentPoint(control).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
                    return;

                _suppressNextAutoHide = true;
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        // Tunneling sees the key before the control's own handler and the map's window handler can open the window
        control.AddHandler(
            KeyDownEvent,
            (_, e) =>
            {
                if (!control.IsEnabled || !ReferenceEquals(e.Source, control)) return;
                if (e.Key is not (Key.Enter or Key.Space)) return;

                _suppressNextAutoHide = true;
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    internal bool CanHideFromCoordinator =>
        ShouldAutoHideWhenDeactivated
        && !HasOpenChildWindow
        && !HasActiveCompanionWindow;

    internal void ClearNextAutoHideSuppression() => _suppressNextAutoHide = false;

    internal void HideFromCoordinator()
    {
        if (ConsumeNextAutoHideSuppression()) return;
        HideFlyout();
    }

    public virtual void DismissForWarmCache()
    {
        Hide();
        if (this is ITrayAppDotNETWarmResourceOwner resourceOwner)
            resourceOwner.TrimHiddenWarmResources();

        NotifyWarmDismissed();
    }

    public virtual void CloseForWarmEviction()
    {
        if (this is ITrayAppDotNETWarmResourceOwner resourceOwner)
            resourceOwner.DisposeWarmResources();

        IsManagedByWarmSlot = false;
        Close();
    }

    protected void NotifyWarmDismissed()
    {
        if (IsWarmPriming) return;
        if (!IsManagedByWarmSlot) return;
        WarmDismissed?.Invoke(this, EventArgs.Empty);
    }

    protected void NotifyChildWindowClosedFromDeactivation()
    {
        ClearNextAutoHideSuppression();
        QueueFocusGroupEvaluation();
    }

    internal void AttachCompanionWindow(FlyoutCompanionWindow companionWindow)
    {
        if (_windowResources.IsDisposed) return;

        _companionWindows.Add(companionWindow);
    }

    internal void DetachCompanionWindow(FlyoutCompanionWindow companionWindow)
    {
        if (!_companionWindows.Remove(companionWindow)) return;

        QueueFocusGroupEvaluation();
    }

    internal void NotifyCompanionWindowActivated(FlyoutCompanionWindow companionWindow)
    {
        if (_windowResources.IsDisposed) return;

        _companionWindows.Add(companionWindow);
        ClearNextAutoHideSuppression();
    }

    internal void NotifyCompanionWindowDeactivated(FlyoutCompanionWindow companionWindow)
    {
        if (!_companionWindows.Contains(companionWindow)) return;

        QueueFocusGroupEvaluation();
    }

    internal void NotifyCompanionWindowStateChanged(FlyoutCompanionWindow companionWindow)
    {
        if (!_companionWindows.Contains(companionWindow)) return;

        QueueFocusGroupEvaluation();
    }

    private bool HasActiveCompanionWindow =>
        _companionWindows.Any(window =>
            window is { IsVisible: true, IsActive: true }
            && window.WindowState != WindowState.Minimized);

    private bool CanHideInactiveFocusGroup =>
        !IsWarmPriming
        && ShouldAutoHideWhenDeactivated
        && !HasOpenChildWindow
        && !KeepOpenForSettingsWindow;

    private void OnDeactivated(object? sender, EventArgs e)
    {
        QueueFocusGroupEvaluation();
    }

    private void QueueFocusGroupEvaluation()
    {
        if (_focusGroupEvaluationQueued || _windowResources.IsDisposed) return;

        _focusGroupEvaluationQueued = true;
        CancellationToken cancellationToken = _windowResources.CancellationToken;
        Dispatcher.UIThread.Post(
            () =>
            {
                _focusGroupEvaluationQueued = false;
                if (cancellationToken.IsCancellationRequested || _windowResources.IsDisposed) return;
                if (!ShouldHideFocusGroup(
                        IsVisible,
                        IsActive,
                        HasActiveCompanionWindow,
                        CanHideInactiveFocusGroup))
                    return;
                if (ConsumeNextAutoHideSuppression()) return;

                HideFlyout();
            },
            DispatcherPriority.Input);
    }

    internal static bool ShouldHideFocusGroup(
        bool isFlyoutVisible,
        bool isFlyoutActive,
        bool hasActiveCompanionWindow,
        bool canHideInactiveFocusGroup) =>
        isFlyoutVisible
        && !isFlyoutActive
        && !hasActiveCompanionWindow
        && canHideInactiveFocusGroup;

    private bool ConsumeNextAutoHideSuppression()
    {
        if (!_suppressNextAutoHide) return false;

        _suppressNextAutoHide = false;
        return true;
    }

    private void OnScalingChanged(object? sender, EventArgs e)
    {
        // Avalonia raises this before Win32 applies the WM_DPICHANGED suggested bounds. Correct again after resize.
        ReapplyFixedFlyoutWidth();
        RestoreAutomaticHeightSizing();
        QueueScalingLayoutCorrection();
    }

    private void QueueScalingLayoutCorrection()
    {
        if (_scalingLayoutCorrectionQueued || _windowResources.IsDisposed) return;

        _scalingLayoutCorrectionQueued = true;
        CancellationToken cancellationToken = _windowResources.CancellationToken;
        Dispatcher.UIThread.Post(
            () =>
            {
                _scalingLayoutCorrectionQueued = false;
                if (cancellationToken.IsCancellationRequested || _windowResources.IsDisposed || !IsVisible) return;

                ReapplyFixedFlyoutWidth();
                RestoreAutomaticHeightSizing();
                ApplyRenderScalingLayoutConstraints();
                UpdateLayout();
            },
            DispatcherPriority.Loaded);
    }

    private void ReapplyFixedFlyoutWidth()
    {
        if (_fixedLogicalWidth is not { } logicalWidth) return;

        MinWidth = logicalWidth;
        MaxWidth = logicalWidth;
        Width = logicalWidth;
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            DisposeContentGeneration();
        }
        finally
        {
            _companionWindows.Clear();
            _focusGroupEvaluationQueued = false;
            _windowResources.Dispose();
            WarmDismissed = null;
            base.OnClosed(e);
        }
    }
}
