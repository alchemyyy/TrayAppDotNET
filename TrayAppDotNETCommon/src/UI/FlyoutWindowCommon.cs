using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Tray;
using TrayAppDotNETCommon.UI.WarmWindows;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.UI;

/// <summary>
/// Base of every tray flyout. It owns the show and hide sequence, tray placement and docking, the chrome drag of an
/// undocked flyout, coalesced content rebuilds, modal dialogs shown over the flyout, and auto-hide on deactivation.
/// Derived flyouts build their content in <see cref="RebuildContent"/> and hook the lifecycle through
/// <see cref="OnShowing"/>, <see cref="OnShown"/>, <see cref="OnRevealing"/> and <see cref="OnHiding"/>.
/// </summary>
public abstract class FlyoutWindowCommon : Window, ITrayAppDotNETWarmWindow
{
    private static readonly PixelPoint HiddenPosition = new(
        TrayAppDotNETWarmWindowDefaults.OffscreenPosition,
        TrayAppDotNETWarmWindowDefaults.OffscreenPosition);

    private readonly UIResourceScope _windowResources;
    private readonly HashSet<FlyoutCompanionWindow> _companionWindows = [];
    private readonly HashSet<Window> _ownedDialogs = [];
    private readonly FlyoutWindowDragHelper _dragHelper = new();
    private readonly FlyoutRebuildScheduler _rebuilds;
    private FlyoutDockingController? _docking;
    private FlyoutChromeDragController? _chromeDrag;
    private UIContentGeneration? _activeContentGeneration;
    private double? _fixedLogicalWidth;
    private bool _focusGroupEvaluationQueued;
    private bool _scalingLayoutCorrectionQueued;
    private bool _hasRetainedFrame;
    private bool _isUndockButtonDragging;
    private bool _isClosed;
    private long _visibilityGeneration;
    private bool _suppressNextAutoHide;
    private bool _deactivatedSinceSuppressionArmed;
    private long _suppressionArmVersion;
    private Window? _watchedOwnedWindow;

    public bool KeepOpenForSettingsWindow { get; set; }
    public bool IsWarmPriming { get; set; }
    public bool IsManagedByWarmSlot { get; set; }

    public event EventHandler? WarmDismissed;

    /// <param name="dismissCommand">
    /// The app map's Escape command leaf on the flyout surface; every flyout hides on Escape through it.
    /// </param>
    protected FlyoutWindowCommon(ControlMapNodeID dismissCommand)
    {
        _windowResources = new UIResourceScope(GetType().Name);
        ControlNames = ControlNameScope.For(this);
        Placement = new FlyoutTrayPlacement(this);
        _rebuilds = new FlyoutRebuildScheduler(
            GetType().Name,
            RebuildContent,
            () => !_isClosed && (IsVisible || IsWarmPriming || _activeContentGeneration == null),
            () => IsWindowDragActive || IsContentInteractionActive,
            QueuePositionNearTray);
        _windowResources.Own(_rebuilds);
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

        this.MapCommand(dismissCommand, Hide);
        Deactivated += OnDeactivated;
        _windowResources.Add(() => Deactivated -= OnDeactivated);
        ScalingChanged += OnScalingChanged;
        _windowResources.Add(() => ScalingChanged -= OnScalingChanged);
    }

    /// <summary>Gets whether a window other than a companion, such as a menu, holds the flyout open.</summary>
    protected virtual bool HasOpenChildWindow => false;

    /// <summary>Gets whether deactivation hides the flyout; an undocked flyout stays open by default.</summary>
    protected virtual bool ShouldAutoHideWhenDeactivated => _docking?.IsUndocked != true;

    /// <summary>Gets whether an interaction such as a slider drag, open menu or inline editor holds the content.</summary>
    protected virtual bool IsContentInteractionActive => false;

    /// <summary>Gets whether a new chrome drag may start; the flyout must also be undocked.</summary>
    protected virtual bool CanStartWindowDrag => true;

    /// <summary>Gets the resources owned for the complete flyout-window lifetime.</summary>
    protected UIResourceScope WindowResources => _windowResources;

    /// <summary>Gets the currently active replaceable content generation.</summary>
    protected UIContentGeneration? ActiveContentGeneration => _activeContentGeneration;

    /// <summary>Gets the source-level control naming scope for this flyout instance.</summary>
    protected ControlNameScope ControlNames { get; }

    /// <summary>Gets the tray placement of this flyout and its companion windows.</summary>
    protected FlyoutTrayPlacement Placement { get; }

    /// <summary>Gets whether the window has closed; a closed flyout ignores every request.</summary>
    protected bool IsWindowClosed => _isClosed;

    /// <summary>Gets the docking controller created by <see cref="InitializeDocking"/>.</summary>
    protected FlyoutDockingController Docking =>
        _docking ?? throw new InvalidOperationException("Flyout docking has not been initialized.");

    /// <summary>Gets whether the flyout is undocked.</summary>
    public bool IsUndocked => _docking?.IsUndocked == true;

    /// <summary>Gets whether the chrome or the undock button is dragging the window.</summary>
    protected bool IsWindowDragActive => _chromeDrag?.IsDragging == true || _isUndockButtonDragging;

    /// <summary>Gets whether a content rebuild was asked for and has not run yet, such as one held by a drag.</summary>
    protected bool IsRebuildPending => _rebuilds.IsPending;

    /// <summary>Gets whether a content rebuild is running.</summary>
    protected bool IsRebuilding => _rebuilds.IsRebuilding;

    /// <summary>
    /// Builds a complete replacement generation and publishes it with <see cref="CommitContentGeneration"/>. Throw to
    /// keep the current content; the failure is logged.
    /// </summary>
    protected abstract void RebuildContent();

    /// <summary>Runs before the flyout is shown, while it is still hidden.</summary>
    protected virtual void OnShowing()
    {
    }

    /// <summary>Runs after the fresh content was laid out and positioned, before the first render.</summary>
    protected virtual void OnShown()
    {
    }

    /// <summary>Runs once the shown flyout settled, right before it is revealed and activated.</summary>
    protected virtual void OnRevealing()
    {
    }

    /// <summary>Runs first when the flyout hides; owned modal dialogs close right after.</summary>
    protected virtual void OnHiding()
    {
    }

    /// <summary>Runs after each flyout positioning, such as to move companion windows along.</summary>
    protected virtual void OnPositionedNearTray()
    {
    }

    /// <summary>Runs for every persisted dock transition, before the base re-anchors a redocked flyout.</summary>
    protected virtual void OnDockStateChanged(FlyoutDockStateChange change)
    {
    }

    /// <summary>Creates the docking controller and chrome drag from the flyout's persisted dock settings.</summary>
    protected void InitializeDocking(IFlyoutDockSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_docking != null) throw new InvalidOperationException("Flyout docking is already initialized.");

        _docking = new FlyoutDockingController(new FlyoutDockingOptions
        {
            Settings = settings,
            DragHelper = _dragHelper,
            CurrentPosition = () => Position,
            SetPosition = position => Position = position,
            ResolveDockedPosition = Placement.ResolveDockedPosition,
            ResolveSavedPosition = Placement.ResolveSavedPosition,
            ResolveSnapTolerance = Placement.ResolveSnapTolerance,
            StateChanged = OnDockStateChangedCore
        });
        _chromeDrag = _windowResources.Own(new FlyoutChromeDragController(
            this,
            _docking,
            () => !_isClosed && !_isUndockButtonDragging && CanStartWindowDrag,
            completed: FlushPendingRebuild));
    }

    /// <summary>Lets the chrome of a content generation drag the undocked flyout for the generation's lifetime.</summary>
    protected void AttachWindowDrag(Control surface, UIResourceScope generationResources)
    {
        ArgumentNullException.ThrowIfNull(generationResources);
        if (_chromeDrag == null) throw new InvalidOperationException("Flyout docking has not been initialized.");
        generationResources.Own(_chromeDrag.Attach(surface));
    }

    /// <summary>Tracks an undock button drag; pass it as <see cref="FlyoutUndockButtonOptions.DraggingChanged"/>.</summary>
    protected void SetUndockButtonDragging(bool isDragging)
    {
        _isUndockButtonDragging = isDragging;
        if (!isDragging) FlushPendingRebuild();
    }

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
    /// Shows the flyout over its tray icon, or at its saved position while undocked. The retained frame of the last
    /// reveal shows at once; a fresh generation is built, laid out and positioned before the next render, and the
    /// flyout is revealed and activated once the dispatcher settled.
    /// </summary>
    public void ShowAt(TrayAppDotNETShellTrayIcon trayIcon, bool activate = true)
    {
        ArgumentNullException.ThrowIfNull(trayIcon);
        if (_isClosed) return;

        long visibilityGeneration = ++_visibilityGeneration;
        Placement.TrayIcon = trayIcon;
        ClearNextAutoHideSuppression();
        ShowActivated = activate;
        _docking?.RedockIfUndockingDisabled();
        OnShowing();
        ApplyWorkAreaMaxHeight();

        // Present the retained frame at once. The fresh generation built below replaces it on the next render.
        ShowWithRetainedFrame(_docking?.ResolvePosition() ?? Placement.ResolveDockedPosition());
        _rebuilds.RebuildNow();

        // Settle size and position before the dispatcher can render the fresh generation
        ApplyWorkAreaMaxHeight();
        UpdateLayout();
        PositionNearTray();
        OnShown();

        CancellationToken cancellationToken = _windowResources.CancellationToken;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_isClosed
                    || cancellationToken.IsCancellationRequested
                    || visibilityGeneration != _visibilityGeneration
                    || !IsVisible) return;

                ApplyWorkAreaMaxHeight();
                UpdateLayout();
                PositionNearTray();
                OnRevealing();
                CompleteReveal();
                if (activate) Activate();
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Hides the flyout. Every hide, whether from Escape, a tray click, an auto-hide or the warm cache, runs
    /// <see cref="OnHiding"/> and closes the modal dialogs shown over the flyout.
    /// </summary>
    public sealed override void Hide() => HideCore(trimWarmResources: false);

    private void HideCore(bool trimWarmResources)
    {
        if (_isClosed) return;

        // Keep opacity and the current generation so the next show presents this frame immediately
        _visibilityGeneration++;
        _suppressNextAutoHide = false;
        _chromeDrag?.Cancel();
        try
        {
            OnHiding();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"{GetType().Name}.OnHiding: {exception}");
        }

        CloseOwnedDialogs();
        base.Hide();
        if (trimWarmResources && this is ITrayAppDotNETWarmResourceOwner resourceOwner)
            resourceOwner.TrimHiddenWarmResources();

        NotifyWarmDismissed();
    }

    /// <summary>Redocks an undocked flyout.</summary>
    public void Redock()
    {
        if (_isClosed) return;
        _docking?.Redock();
    }

    /// <summary>Moves the flyout to its docked or saved position.</summary>
    public void PositionNearTray()
    {
        if (_docking == null) return;
        Position = _docking.ResolvePosition();
        OnPositionedNearTray();
    }

    /// <summary>Re-anchors the visible flyout after the next layout pass, unless a drag moves it.</summary>
    protected void QueuePositionNearTray()
    {
        if (_isClosed || !IsVisible || IsWindowDragActive) return;

        long visibilityGeneration = _visibilityGeneration;
        CancellationToken cancellationToken = _windowResources.CancellationToken;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_isClosed
                    || cancellationToken.IsCancellationRequested
                    || visibilityGeneration != _visibilityGeneration
                    || !IsVisible
                    || IsWindowDragActive) return;

                ApplyWorkAreaMaxHeight();
                UpdateLayout();
                PositionNearTray();
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>Limits the flyout to the work area of its monitor.</summary>
    protected void ApplyWorkAreaMaxHeight() => MaxHeight = Placement.ResolveMaxHeight();

    /// <summary>Queues one coalesced content rebuild; a hidden flyout only marks its content stale.</summary>
    protected void QueueRebuild() => _rebuilds.Queue();

    /// <summary>Rebuilds the content now unless the flyout is hidden or an interaction holds it.</summary>
    protected bool RebuildNow() => _rebuilds.RebuildNow();

    /// <summary>Runs a rebuild that an interaction held, once nothing holds the content any more.</summary>
    protected void FlushPendingRebuild() => _rebuilds.Flush();

    /// <summary>
    /// Shows the flyout's modal dialog. Hiding the flyout closes it, and while it is open the flyout does not
    /// auto-hide.
    /// </summary>
    protected internal async Task<TResult?> ShowOwnedDialogAsync<TResult>(Window dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        if (_isClosed)
        {
            dialog.Close();
            return default;
        }

        bool dialogClosed = false;
        EventHandler onClosed = (_, _) => dialogClosed = true;
        dialog.Closed += onClosed;
        _ownedDialogs.Add(dialog);
        try
        {
            return await dialog.ShowDialog<TResult>(this);
        }
        finally
        {
            _ownedDialogs.Remove(dialog);
            dialog.Closed -= onClosed;

            // Avalonia only hides a modal child when its owner hides, which would leak the native window
            if (!dialogClosed)
            {
                try
                {
                    dialog.Close();
                }
                catch (Exception exception)
                {
                    TADNLog.Log($"{GetType().Name} dialog close failed: {exception}");
                }
            }

            if (!_isClosed) NotifyChildWindowClosedFromDeactivation();
        }
    }

    /// <summary>Shows the flyout's modal dialog; see <see cref="ShowOwnedDialogAsync{TResult}"/>.</summary>
    protected Task ShowOwnedDialogAsync(Window dialog) => ShowOwnedDialogAsync<object>(dialog);

    /// <summary>Gets whether a modal dialog is open over the flyout.</summary>
    protected bool HasOpenDialog => _ownedDialogs.Count > 0;

    private void CloseOwnedDialogs()
    {
        if (_ownedDialogs.Count == 0) return;

        foreach (Window dialog in _ownedDialogs.ToArray())
        {
            try
            {
                dialog.Close();
            }
            catch (Exception exception)
            {
                TADNLog.Log($"{GetType().Name} dialog close failed: {exception}");
            }
        }
    }

    /// <summary>
    /// Shows the install-update prompt over the flyout unless the flyout is closed or a prompt or download for it is
    /// already running. The presenter tracks both and logs failures.
    /// </summary>
    protected void ShowInstallUpdatePrompt(Func<TrayAppDotNETUpdatePromptOptions?> createOptions)
    {
        ArgumentNullException.ThrowIfNull(createOptions);
        if (_isClosed || TrayAppDotNETUpdatePromptPresenter.IsBusy(this)) return;

        TrayAppDotNETUpdatePromptOptions? options = createOptions();
        if (options == null) return;
        if (!ReferenceEquals(options.Owner, this))
            throw new ArgumentException("The update prompt must be owned by this flyout.", nameof(createOptions));

        TrayAppDotNETUpdatePromptPresenter.ShowInstallUpdateAsync(options)
            .ForgetLogged($"{GetType().Name}.ShowInstallUpdatePrompt");
    }

    /// <summary>
    /// Shows the retained frame at a position resolved for the current content size. After the first reveal the
    /// flyout keeps full opacity, so the compositor presents the retained frame at once and content published before
    /// the next render replaces it directly. A flyout that has never been revealed stays transparent until
    /// <see cref="CompleteReveal"/>.
    /// </summary>
    internal void ShowWithRetainedFrame(PixelPoint position)
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
    internal void CompleteReveal()
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
    protected virtual void ApplyRenderScalingLayoutConstraints() => ApplyWorkAreaMaxHeight();

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
    /// Wraps the activation of a control that opens another window, so the deactivation that window causes does not
    /// hide the flyout. The suppression is armed by the activation itself (a click released over the control, Enter,
    /// Space or an accelerator), never by a press, and is disarmed again when no deactivation followed by the time
    /// the dispatcher is idle.
    /// </summary>
    protected Action<ControlActivation> OpensWindow(Action<ControlActivation> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        return activation =>
        {
            ArmNextAutoHideSuppression();
            open(activation);
        };
    }

    /// <inheritdoc cref="OpensWindow(Action{ControlActivation})"/>
    protected Action OpensWindow(Action open)
    {
        ArgumentNullException.ThrowIfNull(open);
        return () =>
        {
            ArmNextAutoHideSuppression();
            open();
        };
    }

    private void ArmNextAutoHideSuppression()
    {
        if (_isClosed) return;

        long version = ++_suppressionArmVersion;
        _suppressNextAutoHide = true;
        _deactivatedSinceSuppressionArmed = false;
        CancellationToken cancellationToken = _windowResources.CancellationToken;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (cancellationToken.IsCancellationRequested || version != _suppressionArmVersion) return;
                if (!_deactivatedSinceSuppressionArmed) _suppressNextAutoHide = false;
            },
            DispatcherPriority.ContextIdle);
    }

    internal bool IsNextAutoHideSuppressed => _suppressNextAutoHide;

    internal bool CanHideFromCoordinator =>
        ShouldAutoHideWhenDeactivated
        && !HasOpenChildWindows
        && !HasActiveCompanionWindow;

    internal void ClearNextAutoHideSuppression()
    {
        _suppressNextAutoHide = false;
        _suppressionArmVersion++;
    }

    internal void HideFromCoordinator()
    {
        if (ConsumeNextAutoHideSuppression()) return;
        Hide();
    }

    /// <summary>Hides the flyout and trims its warm resources; the warm slot then decides on eviction.</summary>
    public void DismissForWarmCache() => HideCore(trimWarmResources: true);

    public virtual void CloseForWarmEviction()
    {
        if (this is ITrayAppDotNETWarmResourceOwner resourceOwner)
            resourceOwner.DisposeWarmResources();

        IsManagedByWarmSlot = false;
        Close();
    }

    private void NotifyWarmDismissed()
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

    private bool HasOpenChildWindows => _ownedDialogs.Count > 0 || HasOpenChildWindow;

    private bool HasActiveCompanionWindow =>
        _companionWindows.Any(window =>
            window is { IsVisible: true }
            && window.WindowState != WindowState.Minimized
            && (window.IsActive || FindActiveOwnedWindow(window) != null))
        || FindActiveOwnedWindow(this) != null;

    /// <summary>
    /// Finds a visible, active window a group member owns, such as a scroll bar's context menu over a companion.
    /// Such a window takes activation from its owner but still belongs to the flyout's focus group.
    /// </summary>
    private static Window? FindActiveOwnedWindow(Window owner)
    {
        foreach (Window owned in owner.OwnedWindows)
        {
            if (!owned.IsVisible || owned is FlyoutCompanionWindow) continue;
            if (owned.IsActive) return owned;
            if (FindActiveOwnedWindow(owned) is { } nested) return nested;
        }

        return null;
    }

    private Window? FindActiveGroupOwnedWindow()
    {
        foreach (FlyoutCompanionWindow window in _companionWindows)
        {
            if (window.IsVisible && FindActiveOwnedWindow(window) is { } owned) return owned;
        }

        return FindActiveOwnedWindow(this);
    }

    // The member an owned window took activation from raises nothing when that window goes away, so the owned window's
    // own deactivation or close re-evaluates the group
    private void WatchOwnedWindow(Window window)
    {
        if (ReferenceEquals(_watchedOwnedWindow, window)) return;

        UnwatchOwnedWindow();
        _watchedOwnedWindow = window;
        window.Deactivated += OnWatchedOwnedWindowChanged;
        window.Closed += OnWatchedOwnedWindowChanged;
    }

    private void UnwatchOwnedWindow()
    {
        Window? window = _watchedOwnedWindow;
        _watchedOwnedWindow = null;
        if (window == null) return;

        window.Deactivated -= OnWatchedOwnedWindowChanged;
        window.Closed -= OnWatchedOwnedWindowChanged;
    }

    private void OnWatchedOwnedWindowChanged(object? sender, EventArgs e)
    {
        UnwatchOwnedWindow();
        QueueFocusGroupEvaluation();
    }

    private bool CanHideInactiveFocusGroup =>
        !IsWarmPriming
        && ShouldAutoHideWhenDeactivated
        && !HasOpenChildWindows
        && !KeepOpenForSettingsWindow;

    private void OnDockStateChangedCore(FlyoutDockStateChange change)
    {
        OnDockStateChanged(change);
        if (change == FlyoutDockStateChange.Redocked) QueuePositionNearTray();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        _deactivatedSinceSuppressionArmed = true;
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
                if (!IsActive && FindActiveGroupOwnedWindow() is { } ownedWindow) WatchOwnedWindow(ownedWindow);
                if (!ShouldHideFocusGroup(
                        IsVisible,
                        IsActive,
                        HasActiveCompanionWindow,
                        CanHideInactiveFocusGroup))
                    return;
                if (ConsumeNextAutoHideSuppression()) return;

                Hide();
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

        ClearNextAutoHideSuppression();
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

    /// <summary>
    /// Runs once when the window closes, after <see cref="IsWindowClosed"/> turned true and before the base closes
    /// owned dialogs, retires the content generation and disposes <see cref="WindowResources"/>.
    /// </summary>
    protected virtual void OnFlyoutClosed()
    {
    }

    protected sealed override void OnClosed(EventArgs e)
    {
        if (_isClosed)
        {
            base.OnClosed(e);
            return;
        }

        _isClosed = true;
        try
        {
            OnFlyoutClosed();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"{GetType().Name}.OnFlyoutClosed: {exception}");
        }

        try
        {
            CloseOwnedDialogs();
            DisposeContentGeneration();
        }
        finally
        {
            UnwatchOwnedWindow();
            _companionWindows.Clear();
            _ownedDialogs.Clear();
            _focusGroupEvaluationQueued = false;
            _windowResources.Dispose();
            WarmDismissed = null;
            Placement.TrayIcon = null;
            base.OnClosed(e);
        }
    }
}
