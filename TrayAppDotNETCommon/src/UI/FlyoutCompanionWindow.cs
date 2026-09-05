using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TrayAppDotNETCommon.UI;

/// <summary>
/// Keeps an owned editor in its flyout owner's focus group without inheriting the settings shell.
/// </summary>
public abstract class FlyoutCompanionWindow : Window
{
    private FlyoutWindowCommon? _flyoutOwner;

    protected FlyoutCompanionWindow()
    {
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        CanResize = true;
        Activated += OnCompanionActivated;
        Deactivated += OnCompanionDeactivated;
        PropertyChanged += OnCompanionPropertyChanged;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        AttachToFlyoutOwner();
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            DetachFromFlyoutOwner();
            Activated -= OnCompanionActivated;
            Deactivated -= OnCompanionDeactivated;
            PropertyChanged -= OnCompanionPropertyChanged;
        }
        finally
        {
            base.OnClosed(e);
        }
    }

    private void OnCompanionActivated(object? sender, EventArgs e)
    {
        AttachToFlyoutOwner();
        _flyoutOwner?.NotifyCompanionWindowActivated(this);
    }

    private void OnCompanionDeactivated(object? sender, EventArgs e) =>
        _flyoutOwner?.NotifyCompanionWindowDeactivated(this);

    private void OnCompanionPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
            _flyoutOwner?.NotifyCompanionWindowStateChanged(this);
    }

    private void AttachToFlyoutOwner()
    {
        FlyoutWindowCommon? flyoutOwner = FindFlyoutOwner();
        if (ReferenceEquals(_flyoutOwner, flyoutOwner)) return;

        _flyoutOwner?.DetachCompanionWindow(this);
        _flyoutOwner = flyoutOwner;
        _flyoutOwner?.AttachCompanionWindow(this);
    }

    private void DetachFromFlyoutOwner()
    {
        FlyoutWindowCommon? flyoutOwner = Interlocked.Exchange(ref _flyoutOwner, value: null);
        flyoutOwner?.DetachCompanionWindow(this);
    }

    private FlyoutWindowCommon? FindFlyoutOwner()
    {
        WindowBase? owner = Owner;
        while (owner != null)
        {
            if (owner is FlyoutWindowCommon flyoutWindow)
                return flyoutWindow;

            owner = owner.Owner;
        }

        return null;
    }
}
