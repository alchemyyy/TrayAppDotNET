using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace TrayAppDotNETCommon.UI;

public sealed class SettingsFlyoutKeepOpenCoordinator(
    Func<Window?> window,
    Func<FlyoutWindowCommon?> flyoutWindow,
    Action showFlyoutWithoutActivation)
    : IDisposable
{
    private Window? _attachedSettingsWindow;
    private FlyoutWindowCommon? _attachedFlyoutWindow;
    private readonly HashSet<Window> _attachedSettingsChildWindows = [];
    private bool _focusGroupEvaluationQueued;
    private bool _disposed;

    public void Attach(Window settingsWindow)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(_attachedSettingsWindow, settingsWindow)) return;

        if (_attachedSettingsWindow != null)
            Detach();

        _attachedSettingsWindow = settingsWindow;
        settingsWindow.Activated += OnSettingsWindowActivated;
        settingsWindow.Deactivated += OnSettingsWindowDeactivated;
        settingsWindow.PropertyChanged += OnSettingsWindowPropertyChanged;
        settingsWindow.Closed += OnSettingsWindowClosed;
        AttachSettingsChildWindows(settingsWindow);
    }

    public void HoldOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Window? settingsWindow = window();
        if (settingsWindow == null || settingsWindow.WindowState == WindowState.Minimized) return;

        AttachSettingsChildWindows(settingsWindow);

        if (flyoutWindow() is not { IsVisible: true })
            showFlyoutWithoutActivation();

        if (flyoutWindow() is { } flyout)
        {
            AttachFlyout(flyout);
            flyout.KeepOpenForSettingsWindow = true;
        }
    }

    public void Release()
    {
        FlyoutWindowCommon? flyout = _attachedFlyoutWindow ?? flyoutWindow();

        if (flyout != null)
        {
            flyout.KeepOpenForSettingsWindow = false;
            if (flyout is { IsVisible: true, CanHideFromCoordinator: true })
                flyout.HideFromCoordinator();
        }

        DetachFlyout();
        DetachSettingsChildWindows();
    }

    public void Detach()
    {
        Release();
        DetachSettingsWindow();
    }

    private void DetachSettingsWindow()
    {
        if (_attachedSettingsWindow == null) return;

        _attachedSettingsWindow.Activated -= OnSettingsWindowActivated;
        _attachedSettingsWindow.Deactivated -= OnSettingsWindowDeactivated;
        _attachedSettingsWindow.PropertyChanged -= OnSettingsWindowPropertyChanged;
        _attachedSettingsWindow.Closed -= OnSettingsWindowClosed;
        _attachedSettingsWindow = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
    }

    private void OnSettingsWindowActivated(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _attachedSettingsWindow)) return;

        HoldOpen();
        (_attachedFlyoutWindow ?? flyoutWindow())?.ClearNextAutoHideSuppression();
    }

    private void OnSettingsWindowDeactivated(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _attachedSettingsWindow)) return;

        QueueFocusGroupEvaluation();
    }

    private void OnSettingsWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _attachedSettingsWindow)) return;
        if (e.Property != Window.WindowStateProperty) return;

        if (sender is Window settingsWindow && settingsWindow.WindowState == WindowState.Minimized)
            Release();
        else
            QueueFocusGroupEvaluation();
    }

    private void OnFlyoutWindowDeactivated(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _attachedFlyoutWindow)) return;
        if (sender is not FlyoutWindowCommon flyout) return;

        if (!flyout.IsVisible || !flyout.CanHideFromCoordinator)
            return;

        QueueFocusGroupEvaluation();
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        Release();
        DetachSettingsWindow();
    }

    private void OnSettingsChildWindowActivated(object? sender, EventArgs e)
    {
        if (sender is Window childWindow && _attachedSettingsChildWindows.Contains(childWindow))
            HoldOpen();
    }

    private void OnSettingsChildWindowDeactivated(object? sender, EventArgs e)
    {
        if (sender is Window childWindow && _attachedSettingsChildWindows.Contains(childWindow))
            QueueFocusGroupEvaluation();
    }

    private void OnSettingsChildWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty) return;
        if (sender is Window childWindow && _attachedSettingsChildWindows.Contains(childWindow))
            QueueFocusGroupEvaluation();
    }

    private void OnSettingsChildWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window childWindow) return;

        DetachSettingsChildWindow(childWindow);
        QueueFocusGroupEvaluation();
    }

    private void QueueFocusGroupEvaluation()
    {
        if (_disposed || _focusGroupEvaluationQueued) return;

        _focusGroupEvaluationQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _focusGroupEvaluationQueued = false;
                EvaluateFocusGroup();
            },
            DispatcherPriority.Input);
    }

    private void EvaluateFocusGroup()
    {
        if (_disposed) return;

        Window? settingsWindow = window();
        FlyoutWindowCommon? flyout = _attachedFlyoutWindow ?? flyoutWindow();

        if (settingsWindow == null || IsUnavailableSettingsWindow(settingsWindow))
        {
            Release();
            return;
        }

        AttachSettingsChildWindows(settingsWindow);

        if (flyout == null) return;
        AttachFlyout(flyout);

        if (!flyout.IsVisible)
        {
            flyout.KeepOpenForSettingsWindow = false;
            return;
        }

        if (!flyout.CanHideFromCoordinator)
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
        if (ReferenceEquals(_attachedFlyoutWindow, flyout)) return;

        DetachFlyout();
        _attachedFlyoutWindow = flyout;
        flyout.Deactivated += OnFlyoutWindowDeactivated;
        flyout.Closed += OnFlyoutWindowClosed;
    }

    private void DetachFlyout()
    {
        if (_attachedFlyoutWindow == null) return;

        _attachedFlyoutWindow.Deactivated -= OnFlyoutWindowDeactivated;
        _attachedFlyoutWindow.Closed -= OnFlyoutWindowClosed;
        _attachedFlyoutWindow = null;
    }

    private void OnFlyoutWindowClosed(object? sender, EventArgs e) => DetachFlyout();

    private void AttachSettingsChildWindows(Window settingsWindow)
    {
        HashSet<Window> ownedNow = [];
        foreach (Window childWindow in settingsWindow.OwnedWindows)
        {
            if (ReferenceEquals(childWindow, settingsWindow)) continue;

            ownedNow.Add(childWindow);
            AttachSettingsChildWindow(childWindow);
        }

        foreach (Window attachedChild in _attachedSettingsChildWindows.ToArray())
        {
            if (!ownedNow.Contains(attachedChild))
                DetachSettingsChildWindow(attachedChild);
        }
    }

    private void AttachSettingsChildWindow(Window childWindow)
    {
        if (!_attachedSettingsChildWindows.Add(childWindow)) return;

        childWindow.Activated += OnSettingsChildWindowActivated;
        childWindow.Deactivated += OnSettingsChildWindowDeactivated;
        childWindow.PropertyChanged += OnSettingsChildWindowPropertyChanged;
        childWindow.Closed += OnSettingsChildWindowClosed;
    }

    private void DetachSettingsChildWindow(Window childWindow)
    {
        if (!_attachedSettingsChildWindows.Remove(childWindow)) return;

        childWindow.Activated -= OnSettingsChildWindowActivated;
        childWindow.Deactivated -= OnSettingsChildWindowDeactivated;
        childWindow.PropertyChanged -= OnSettingsChildWindowPropertyChanged;
        childWindow.Closed -= OnSettingsChildWindowClosed;
    }

    private void DetachSettingsChildWindows()
    {
        foreach (Window childWindow in _attachedSettingsChildWindows.ToArray())
            DetachSettingsChildWindow(childWindow);
    }

    private bool IsSettingsFocusGroupActive(Window settingsWindow)
    {
        if (settingsWindow.IsActive) return true;

        AttachSettingsChildWindows(settingsWindow);
        foreach (Window childWindow in _attachedSettingsChildWindows)
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

    private static bool IsUnavailableSettingsWindow(Window settingsWindow) =>
        !settingsWindow.IsVisible || settingsWindow.WindowState == WindowState.Minimized;
}
