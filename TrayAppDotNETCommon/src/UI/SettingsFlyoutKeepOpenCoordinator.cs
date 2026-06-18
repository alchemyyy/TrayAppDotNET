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
    private bool _disposed;

    public void Attach(Window settingsWindow)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(_attachedSettingsWindow, settingsWindow)) return;

        Detach();
        _attachedSettingsWindow = settingsWindow;
        settingsWindow.Activated += OnSettingsWindowActivated;
        settingsWindow.Deactivated += OnSettingsWindowDeactivated;
        settingsWindow.Closed += OnSettingsWindowClosed;
    }

    public void HoldOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (window() == null) return;

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
            if (flyout is { IsVisible: true, IsActive: false, CanHideFromCoordinator: true })
                flyout.HideFromCoordinator();
        }

        DetachFlyout();
    }

    public void Detach()
    {
        if (_attachedSettingsWindow == null) return;

        _attachedSettingsWindow.Activated -= OnSettingsWindowActivated;
        _attachedSettingsWindow.Deactivated -= OnSettingsWindowDeactivated;
        _attachedSettingsWindow.Closed -= OnSettingsWindowClosed;
        _attachedSettingsWindow = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Release();
        Detach();
    }

    private void OnSettingsWindowActivated(object? sender, EventArgs e) => HoldOpen();

    private void OnSettingsWindowDeactivated(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _attachedSettingsWindow)) return;

        FlyoutWindowCommon? flyout = flyoutWindow();
        if (flyout == null) return;
        AttachFlyout(flyout);

        if (!flyout.IsVisible || !flyout.CanHideFromCoordinator)
        {
            flyout.KeepOpenForSettingsWindow = false;
            return;
        }

        if (flyout.IsActive) return;

        HideUnlessTargetActivates(
            flyout,
            () => flyout.IsActive,
            () => HideFlyoutAndRelease(flyout));
    }

    private void OnFlyoutWindowDeactivated(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _attachedFlyoutWindow)) return;
        if (sender is not FlyoutWindowCommon flyout) return;
        if (!flyout.IsVisible || !flyout.CanHideFromCoordinator) return;

        Window? settingsWindow = window();
        if (settingsWindow == null)
        {
            HideFlyoutAndRelease(flyout);
            return;
        }

        if (settingsWindow.IsActive) return;

        HideUnlessTargetActivates(
            settingsWindow,
            () => settingsWindow.IsActive,
            () => HideFlyoutAndRelease(flyout));
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        Release();
        Detach();
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

    private static void HideUnlessTargetActivates(Window target, Func<bool> isTargetActive, Action hide)
    {
        bool keep = false;
        EventHandler? onActivated = null;
        onActivated = (_, _) =>
        {
            keep = true;
            target.Activated -= onActivated;
        };
        target.Activated += onActivated;

        Dispatcher.UIThread.Post(
            () =>
            {
                target.Activated -= onActivated;
                if (keep || isTargetActive()) return;

                hide();
            },
            DispatcherPriority.Input);
    }

    private static void HideFlyoutAndRelease(FlyoutWindowCommon flyout)
    {
        flyout.KeepOpenForSettingsWindow = false;
        if (flyout is { IsVisible: true, CanHideFromCoordinator: true })
            flyout.HideFromCoordinator();
    }
}
