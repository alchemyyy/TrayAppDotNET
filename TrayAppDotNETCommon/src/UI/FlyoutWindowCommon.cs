using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TrayAppDotNETCommon.UI.WarmWindows;

namespace TrayAppDotNETCommon.UI;

public abstract class FlyoutWindowCommon : Window, ITrayAppDotNETWarmWindow
{
    public bool KeepOpenForSettingsWindow { get; set; }
    public bool IsWarmPriming { get; set; }
    public bool IsManagedByWarmSlot { get; set; }

    public event EventHandler? WarmDismissed;

    private bool _suppressNextAutoHide;

    protected FlyoutWindowCommon()
    {
        Deactivated += (_, _) =>
        {
            if (!ShouldHideWhenInactive()) return;
            if (ConsumeNextAutoHideSuppression()) return;
            HideFlyout();
        };
    }

    protected virtual bool HasOpenChildWindow => false;

    protected virtual bool ShouldAutoHideWhenDeactivated => true;

    protected virtual void HideFlyout() => Hide();

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
        NotifyWarmDismissed();
    }

    public virtual void CloseForWarmEviction()
    {
        IsManagedByWarmSlot = false;
        Close();
    }

    protected void NotifyWarmDismissed()
    {
        if (IsWarmPriming) return;
        if (!IsManagedByWarmSlot) return;
        WarmDismissed?.Invoke(this, EventArgs.Empty);
    }

    protected void NotifyChildWindowClosedFromDeactivation() =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!IsVisible || IsActive) return;
                if (!ShouldHideWhenInactive()) return;
                if (ConsumeNextAutoHideSuppression()) return;
                HideFlyout();
            },
            DispatcherPriority.Input);

    private bool ShouldHideWhenInactive() =>
        !IsWarmPriming
        && CanHideFromCoordinator
        && !KeepOpenForSettingsWindow;

    private bool ConsumeNextAutoHideSuppression()
    {
        if (!_suppressNextAutoHide) return false;

        _suppressNextAutoHide = false;
        return true;
    }
}
