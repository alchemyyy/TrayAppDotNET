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
    private UIContentGeneration? _activeContentGeneration;
    private double? _fixedLogicalWidth;
    private bool _scalingLayoutCorrectionQueued;

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

    /// <summary>Shows the transparent flyout on its target monitor before final measured positioning.</summary>
    protected void ShowHiddenForPositioning(PixelPoint stagingPosition)
    {
        if (IsVisible) return;

        Opacity = 0;
        Position = stagingPosition;
        ReapplyFixedFlyoutWidth();
        RestoreAutomaticHeightSizing();
        Show();
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
        UIContentGeneration? generation = Interlocked.Exchange(ref _activeContentGeneration, null);
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
    }

    internal bool CanHideFromCoordinator => ShouldAutoHideWhenDeactivated && !HasOpenChildWindow;

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
        CancellationToken cancellationToken = _windowResources.CancellationToken;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (cancellationToken.IsCancellationRequested) return;
                ClearNextAutoHideSuppression();
                if (!IsVisible || IsActive) return;
                if (!ShouldHideWhenInactive()) return;
                HideFlyout();
            },
            DispatcherPriority.Input);
    }

    private bool ShouldHideWhenInactive() =>
        !IsWarmPriming
        && CanHideFromCoordinator
        && !KeepOpenForSettingsWindow;

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!ShouldHideWhenInactive()) return;
        if (ConsumeNextAutoHideSuppression()) return;
        HideFlyout();
    }

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
            _windowResources.Dispose();
            WarmDismissed = null;
            base.OnClosed(e);
        }
    }
}
