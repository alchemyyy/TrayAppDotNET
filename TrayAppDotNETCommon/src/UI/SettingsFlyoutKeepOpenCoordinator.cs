using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.UI.Controls;

namespace TrayAppDotNETCommon.UI;

public sealed class SettingsFlyoutKeepOpenCoordinator : IDisposable
{
    private Func<Window?>? _windowProvider;
    private Func<FlyoutWindowCommon?>? _flyoutWindowProvider;
    private Action? _showFlyoutWithoutActivation;
    private Window? _attachedSettingsWindow;
    private UIResourceScope? _settingsWindowResources;
    private FlyoutWindowCommon? _attachedFlyoutWindow;
    private UIResourceScope? _flyoutResources;
    private readonly Dictionary<Window, UIResourceScope> _attachedSettingsChildWindows = [];
    private Window? _immediateSettingsCloseWindow;
    private UIResourceScope? _immediateSettingsCloseResources;
    private int _immediateSettingsCloseRestoreVersion;
    private FlyoutWindowCommon? _pendingHideFlyout;
    private UIResourceScope? _pendingHideResources;
    private long _pendingHideVersion;
    private long _attachmentVersion;
    private long _queuedFocusGroupVersion;
    private bool _focusGroupEvaluationQueued;
    private bool _isHandlingSettingsActivation;
    private bool _hideRestoredFlyoutOnImmediateSettingsClose;
    private bool _disposed;

    public SettingsFlyoutKeepOpenCoordinator(
        Func<Window?> window,
        Func<FlyoutWindowCommon?> flyoutWindow,
        Action? showFlyoutWithoutActivation = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(flyoutWindow);
        _windowProvider = window;
        _flyoutWindowProvider = flyoutWindow;
        _showFlyoutWithoutActivation = showFlyoutWithoutActivation;
    }

    public void Attach(Window settingsWindow)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settingsWindow);
        if (ReferenceEquals(_attachedSettingsWindow, settingsWindow)) return;

        if (_attachedSettingsWindow != null)
            DetachCore();

        CancelPendingHide();
        InvalidateQueuedFocusGroupEvaluation();

        UIResourceScope resources = new(nameof(SettingsFlyoutKeepOpenCoordinator) + ".SettingsWindow");
        _attachedSettingsWindow = settingsWindow;
        try
        {
            settingsWindow.Activated += OnSettingsWindowActivated;
            resources.Add(() => settingsWindow.Activated -= OnSettingsWindowActivated);
            settingsWindow.Deactivated += OnSettingsWindowDeactivated;
            resources.Add(() => settingsWindow.Deactivated -= OnSettingsWindowDeactivated);
            settingsWindow.PropertyChanged += OnSettingsWindowPropertyChanged;
            resources.Add(() => settingsWindow.PropertyChanged -= OnSettingsWindowPropertyChanged);
            settingsWindow.AddHandler(
                InputElement.PointerPressedEvent,
                OnSettingsWindowPointerPressed,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            resources.Add(() => settingsWindow.RemoveHandler(
                InputElement.PointerPressedEvent,
                OnSettingsWindowPointerPressed));
            settingsWindow.Closed += OnSettingsWindowClosed;
            resources.Add(() => settingsWindow.Closed -= OnSettingsWindowClosed);
            _settingsWindowResources = resources;
            AttachSettingsChildWindows(settingsWindow);
        }
        catch
        {
            _attachedSettingsWindow = null;
            _settingsWindowResources = null;
            DetachSettingsChildWindows();
            resources.Dispose();
            InvalidateQueuedFocusGroupEvaluation();
            throw;
        }
    }

    public void HoldOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelPendingHide();

        Window? settingsWindow = _windowProvider?.Invoke();
        if (settingsWindow == null || settingsWindow.WindowState == WindowState.Minimized) return;

        AttachSettingsChildWindows(settingsWindow);

        FlyoutWindowCommon? flyout = _flyoutWindowProvider?.Invoke();
        if (flyout is not { IsVisible: true })
        {
            // Restore only a flyout that was already paired with settings
            if (_attachedFlyoutWindow != null && _showFlyoutWithoutActivation != null)
            {
                _showFlyoutWithoutActivation();
                flyout = _flyoutWindowProvider?.Invoke();
                if (flyout is { IsVisible: true })
                {
                    AttachFlyout(flyout);
                    flyout.KeepOpenForSettingsWindow = true;
                    if (_isHandlingSettingsActivation)
                        MarkRestoredFlyoutForImmediateSettingsClose();
                    return;
                }
            }

            _attachedFlyoutWindow?.KeepOpenForSettingsWindow = false;
            DetachFlyout(cancelPendingHide: true);
            return;
        }

        AttachFlyout(flyout);
        flyout.KeepOpenForSettingsWindow = true;
    }

    public void Release()
    {
        if (_disposed) return;
        ReleaseCore(hideFlyout: true, activateFlyout: false);
    }

    private void ReleaseCore(bool hideFlyout, bool activateFlyout)
    {
        FlyoutWindowCommon? flyout = _attachedFlyoutWindow;
        if (flyout == null && !_disposed)
            flyout = _flyoutWindowProvider?.Invoke();

        bool preservePendingHide = false;
        try
        {
            if (flyout != null)
            {
                flyout.KeepOpenForSettingsWindow = false;
                if (hideFlyout)
                    preservePendingHide = HideFlyoutNowOrWhenOpened(flyout);
                else if (activateFlyout && flyout is { IsVisible: true, CanHideFromCoordinator: true })
                    flyout.Activate();
            }
        }
        finally
        {
            DetachFlyout(cancelPendingHide: !preservePendingHide);
            DetachSettingsChildWindows();
        }
    }

    public void Detach()
    {
        if (_disposed) return;
        DetachCore();
    }

    private void DetachCore()
    {
        InvalidateQueuedFocusGroupEvaluation();
        CancelPendingHide();
        try
        {
            ReleaseCore(hideFlyout: false, activateFlyout: false);
        }
        finally
        {
            ClearImmediateSettingsCloseTracking();
            DetachSettingsWindow();
        }
    }

    private void DetachSettingsWindow()
    {
        _attachedSettingsWindow = null;
        UIResourceScope? resources = Interlocked.Exchange(ref _settingsWindowResources, null);
        resources?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        InvalidateQueuedFocusGroupEvaluation();

        try
        {
            CancelPendingHide();
            ReleaseCore(hideFlyout: false, activateFlyout: false);
        }
        finally
        {
            ClearImmediateSettingsCloseTracking();
            DetachSettingsChildWindows();
            DetachFlyout(cancelPendingHide: true);
            DetachSettingsWindow();
            _windowProvider = null;
            _flyoutWindowProvider = null;
            _showFlyoutWithoutActivation = null;
        }
    }

    private void OnSettingsWindowActivated(object? sender, EventArgs e)
    {
        Window? settingsWindow = _attachedSettingsWindow;
        if (_disposed || settingsWindow == null || !ReferenceEquals(sender, settingsWindow)) return;

        FlyoutWindowCommon? flyout = _attachedFlyoutWindow ?? _flyoutWindowProvider?.Invoke();
        bool suppressHiddenFlyoutRestore = flyout is not { IsVisible: true }
                                           && IsLeftMouseButtonDown()
                                           && IsPointerOnSettingsDismissButton(settingsWindow);
        if (!suppressHiddenFlyoutRestore)
        {
            _isHandlingSettingsActivation = true;
            try
            {
                HoldOpen();
            }
            finally
            {
                _isHandlingSettingsActivation = false;
            }
        }

        (_attachedFlyoutWindow ?? _flyoutWindowProvider?.Invoke())?.ClearNextAutoHideSuppression();
    }

    private void OnSettingsWindowDeactivated(object? sender, EventArgs e)
    {
        if (_disposed || !ReferenceEquals(sender, _attachedSettingsWindow)) return;
        QueueFocusGroupEvaluation();
    }

    private void OnSettingsWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Window? settingsWindow = _attachedSettingsWindow;
        if (_disposed || settingsWindow == null || !ReferenceEquals(sender, settingsWindow)) return;
        if (!e.GetCurrentPoint(settingsWindow).Properties.IsLeftButtonPressed) return;
        if (IsSettingsDismissButton(e.Source as StyledElement)) return;

        HoldOpen();
    }

    private void OnSettingsWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_disposed || !ReferenceEquals(sender, _attachedSettingsWindow)) return;
        if (e.Property != Window.WindowStateProperty) return;

        if (sender is Window { WindowState: WindowState.Minimized })
            ReleaseCore(hideFlyout: true, activateFlyout: false);
        else
            QueueFocusGroupEvaluation();
    }

    private void OnFlyoutWindowDeactivated(object? sender, EventArgs e)
    {
        if (_disposed || !ReferenceEquals(sender, _attachedFlyoutWindow)) return;
        if (sender is not FlyoutWindowCommon flyout) return;
        if (!flyout.IsVisible || !flyout.CanHideFromCoordinator) return;

        QueueFocusGroupEvaluation();
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        if (_disposed || !ReferenceEquals(sender, _attachedSettingsWindow)) return;

        bool hideFlyout = _hideRestoredFlyoutOnImmediateSettingsClose;
        ClearImmediateSettingsCloseTracking();
        try
        {
            ReleaseCore(hideFlyout: hideFlyout, activateFlyout: !hideFlyout);
        }
        finally
        {
            DetachSettingsWindow();
            InvalidateQueuedFocusGroupEvaluation();
        }
    }

    private void MarkRestoredFlyoutForImmediateSettingsClose()
    {
        if (_disposed || !IsLeftMouseButtonDown()) return;

        Window? settingsWindow = _attachedSettingsWindow ?? _windowProvider?.Invoke();
        if (settingsWindow == null) return;

        ClearImmediateSettingsCloseTracking();
        _hideRestoredFlyoutOnImmediateSettingsClose = true;
        _immediateSettingsCloseWindow = settingsWindow;
        int restoreVersion = ++_immediateSettingsCloseRestoreVersion;
        UIResourceScope resources = new(nameof(SettingsFlyoutKeepOpenCoordinator) + ".ImmediateClose");
        try
        {
            settingsWindow.AddHandler(
                InputElement.PointerReleasedEvent,
                OnImmediateSettingsClosePointerReleased,
                RoutingStrategies.Bubble,
                handledEventsToo: true);
            resources.Add(() => settingsWindow.RemoveHandler(
                InputElement.PointerReleasedEvent,
                OnImmediateSettingsClosePointerReleased));
            _immediateSettingsCloseResources = resources;
        }
        catch
        {
            resources.Dispose();
            if (restoreVersion == _immediateSettingsCloseRestoreVersion)
                ClearImmediateSettingsCloseTracking();
            throw;
        }
    }

    private void OnImmediateSettingsClosePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_disposed || !ReferenceEquals(sender, _immediateSettingsCloseWindow)) return;

        int restoreVersion = _immediateSettingsCloseRestoreVersion;
        Window? trackedWindow = _immediateSettingsCloseWindow;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_disposed || _immediateSettingsCloseRestoreVersion != restoreVersion) return;
                if (!ReferenceEquals(trackedWindow, _immediateSettingsCloseWindow)) return;
                ClearImmediateSettingsCloseTracking();
            },
            DispatcherPriority.ApplicationIdle);
    }

    private void ClearImmediateSettingsCloseTracking()
    {
        _immediateSettingsCloseWindow = null;
        _hideRestoredFlyoutOnImmediateSettingsClose = false;
        _immediateSettingsCloseRestoreVersion++;
        UIResourceScope? resources = Interlocked.Exchange(ref _immediateSettingsCloseResources, null);
        resources?.Dispose();
    }

    private void OnSettingsChildWindowActivated(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (sender is Window childWindow && _attachedSettingsChildWindows.ContainsKey(childWindow))
            HoldOpen();
    }

    private void OnSettingsChildWindowDeactivated(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (sender is Window childWindow && _attachedSettingsChildWindows.ContainsKey(childWindow))
            QueueFocusGroupEvaluation();
    }

    private void OnSettingsChildWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_disposed || e.Property != Window.WindowStateProperty) return;
        if (sender is Window childWindow && _attachedSettingsChildWindows.ContainsKey(childWindow))
            QueueFocusGroupEvaluation();
    }

    private void OnSettingsChildWindowClosed(object? sender, EventArgs e)
    {
        if (_disposed || sender is not Window childWindow) return;

        DetachSettingsChildWindow(childWindow);
        QueueFocusGroupEvaluation();
    }

    private void QueueFocusGroupEvaluation()
    {
        if (_disposed || _focusGroupEvaluationQueued) return;

        long version = _attachmentVersion;
        Window? settingsWindow = _attachedSettingsWindow;
        _focusGroupEvaluationQueued = true;
        _queuedFocusGroupVersion = version;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_queuedFocusGroupVersion == version)
                    _focusGroupEvaluationQueued = false;
                if (_disposed || version != _attachmentVersion) return;
                if (!ReferenceEquals(settingsWindow, _attachedSettingsWindow)) return;
                EvaluateFocusGroup();
            },
            DispatcherPriority.Input);
    }

    private void InvalidateQueuedFocusGroupEvaluation()
    {
        _attachmentVersion++;
        _focusGroupEvaluationQueued = false;
        _queuedFocusGroupVersion = 0;
    }

    private void EvaluateFocusGroup()
    {
        if (_disposed) return;

        Window? settingsWindow = _windowProvider?.Invoke();
        FlyoutWindowCommon? flyout = _attachedFlyoutWindow ?? _flyoutWindowProvider?.Invoke();

        if (settingsWindow is not { IsVisible: true })
        {
            ReleaseCore(hideFlyout: false, activateFlyout: false);
            return;
        }

        if (settingsWindow.WindowState == WindowState.Minimized)
        {
            ReleaseCore(hideFlyout: true, activateFlyout: false);
            return;
        }

        AttachSettingsChildWindows(settingsWindow);
        if (flyout == null) return;
        AttachFlyout(flyout);

        if (!flyout.IsVisible || !flyout.CanHideFromCoordinator)
        {
            flyout.KeepOpenForSettingsWindow = false;
            return;
        }

        if (flyout.IsActive || IsSettingsFocusGroupActive(settingsWindow))
        {
            flyout.KeepOpenForSettingsWindow = true;
            return;
        }

        HideFlyoutAndRelease(flyout);
    }

    private void AttachFlyout(FlyoutWindowCommon flyout)
    {
        CancelPendingHide();
        if (ReferenceEquals(_attachedFlyoutWindow, flyout)) return;

        DetachFlyout(cancelPendingHide: false);
        InvalidateQueuedFocusGroupEvaluation();
        UIResourceScope resources = new(nameof(SettingsFlyoutKeepOpenCoordinator) + ".Flyout");
        _attachedFlyoutWindow = flyout;
        try
        {
            flyout.Deactivated += OnFlyoutWindowDeactivated;
            resources.Add(() => flyout.Deactivated -= OnFlyoutWindowDeactivated);
            flyout.Closed += OnFlyoutWindowClosed;
            resources.Add(() => flyout.Closed -= OnFlyoutWindowClosed);
            _flyoutResources = resources;
        }
        catch
        {
            _attachedFlyoutWindow = null;
            resources.Dispose();
            throw;
        }
    }

    private void DetachFlyout(bool cancelPendingHide)
    {
        if (cancelPendingHide)
            CancelPendingHide();

        if (_attachedFlyoutWindow == null && _flyoutResources == null) return;
        _attachedFlyoutWindow = null;
        UIResourceScope? resources = Interlocked.Exchange(ref _flyoutResources, null);
        resources?.Dispose();
        InvalidateQueuedFocusGroupEvaluation();
    }

    private void OnFlyoutWindowClosed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _attachedFlyoutWindow)) return;
        DetachFlyout(cancelPendingHide: true);
    }

    private void AttachSettingsChildWindows(Window settingsWindow)
    {
        HashSet<Window> ownedNow = [];
        foreach (Window childWindow in settingsWindow.OwnedWindows)
        {
            if (ReferenceEquals(childWindow, settingsWindow)) continue;

            ownedNow.Add(childWindow);
            AttachSettingsChildWindow(childWindow);
        }

        foreach (Window attachedChild in _attachedSettingsChildWindows.Keys.ToArray())
        {
            if (!ownedNow.Contains(attachedChild))
                DetachSettingsChildWindow(attachedChild);
        }
    }

    private void AttachSettingsChildWindow(Window childWindow)
    {
        if (_attachedSettingsChildWindows.ContainsKey(childWindow)) return;

        UIResourceScope resources = new(nameof(SettingsFlyoutKeepOpenCoordinator) + ".SettingsChild");
        try
        {
            childWindow.Activated += OnSettingsChildWindowActivated;
            resources.Add(() => childWindow.Activated -= OnSettingsChildWindowActivated);
            childWindow.Deactivated += OnSettingsChildWindowDeactivated;
            resources.Add(() => childWindow.Deactivated -= OnSettingsChildWindowDeactivated);
            childWindow.PropertyChanged += OnSettingsChildWindowPropertyChanged;
            resources.Add(() => childWindow.PropertyChanged -= OnSettingsChildWindowPropertyChanged);
            childWindow.Closed += OnSettingsChildWindowClosed;
            resources.Add(() => childWindow.Closed -= OnSettingsChildWindowClosed);
            _attachedSettingsChildWindows.Add(childWindow, resources);
        }
        catch
        {
            resources.Dispose();
            throw;
        }
    }

    private void DetachSettingsChildWindow(Window childWindow)
    {
        if (!_attachedSettingsChildWindows.Remove(childWindow, out UIResourceScope? resources)) return;
        resources.Dispose();
    }

    private void DetachSettingsChildWindows()
    {
        foreach (Window childWindow in _attachedSettingsChildWindows.Keys.ToArray())
            DetachSettingsChildWindow(childWindow);
    }

    private bool IsSettingsFocusGroupActive(Window settingsWindow)
    {
        if (settingsWindow.IsActive) return true;

        AttachSettingsChildWindows(settingsWindow);
        foreach (Window childWindow in _attachedSettingsChildWindows.Keys)
        {
            if (ReferenceEquals(childWindow.Owner, settingsWindow)
                && childWindow.IsVisible
                && childWindow.WindowState != WindowState.Minimized
                && childWindow.IsActive)
                return true;
        }

        return false;
    }

    private static void HideFlyoutAndRelease(FlyoutWindowCommon flyout)
    {
        flyout.KeepOpenForSettingsWindow = false;
        if (flyout is { IsVisible: true, CanHideFromCoordinator: true })
            flyout.HideFromCoordinator();
    }

    private bool HideFlyoutNowOrWhenOpened(FlyoutWindowCommon flyout)
    {
        CancelPendingHide();
        if (flyout.IsVisible)
        {
            if (flyout.CanHideFromCoordinator)
                flyout.HideFromCoordinator();
            return false;
        }

        long version = ++_pendingHideVersion;
        UIResourceScope resources = new(nameof(SettingsFlyoutKeepOpenCoordinator) + ".PendingHide");
        EventHandler opened = (sender, e) => CompletePendingHide(flyout, version, opened: true);
        EventHandler closed = (sender, e) => CompletePendingHide(flyout, version, opened: false);
        _pendingHideFlyout = flyout;
        _pendingHideResources = resources;
        try
        {
            flyout.Opened += opened;
            resources.Add(() => flyout.Opened -= opened);
            flyout.Closed += closed;
            resources.Add(() => flyout.Closed -= closed);
            return true;
        }
        catch
        {
            CancelPendingHide();
            throw;
        }
    }

    private void CompletePendingHide(FlyoutWindowCommon flyout, long version, bool opened)
    {
        if (!ReferenceEquals(flyout, _pendingHideFlyout) || version != _pendingHideVersion) return;

        CancelPendingHide();
        if (_disposed || !opened) return;
        if (flyout is { IsVisible: true, CanHideFromCoordinator: true })
            flyout.HideFromCoordinator();
    }

    private void CancelPendingHide()
    {
        _pendingHideVersion++;
        _pendingHideFlyout = null;
        UIResourceScope? resources = Interlocked.Exchange(ref _pendingHideResources, null);
        resources?.Dispose();
    }

    private static bool IsLeftMouseButtonDown() =>
        OperatingSystem.IsWindows()
        && (User32.GetAsyncKeyState(User32.VK_LBUTTON) & unchecked((short)0x8000)) != 0;

    private static bool IsPointerOnSettingsDismissButton(Window settingsWindow)
    {
        if (!OperatingSystem.IsWindows() || !User32.GetCursorPos(out User32.POINT cursorPosition)) return false;

        Point clientPosition = settingsWindow.PointToClient(
            new PixelPoint(cursorPosition.X, cursorPosition.Y));
        IInputElement? hitElement = settingsWindow.InputHitTest(clientPosition, enabledElementsOnly: false);
        return IsSettingsDismissButton(hitElement as StyledElement);
    }

    private static bool IsSettingsDismissButton(StyledElement? hitElement)
    {
        StyledElement? currentElement = hitElement;
        while (currentElement != null)
        {
            if (currentElement is SettingsButton
                {
                    IsSettingsWindowCloseButton: true
                } or SettingsButton
                {
                    IsSettingsWindowMinimizeButton: true
                })
                return true;
            currentElement = currentElement.Parent;
        }

        return false;
    }
}
