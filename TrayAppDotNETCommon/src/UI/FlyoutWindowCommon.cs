using Avalonia.Controls;
using Avalonia.Threading;
using TrayAppDotNETCommon.UI.WarmWindows;

namespace TrayAppDotNETCommon.UI;

public abstract class FlyoutWindowCommon : Window, ITrayAppDotNETWarmWindow
{
    public bool KeepOpenForSettingsWindow { get; set; }
    public bool IsWarmPriming { get; set; }
    public bool IsManagedByWarmSlot { get; set; }

    public event EventHandler? WarmDismissed;

    protected FlyoutWindowCommon()
    {
        Deactivated += (_, _) =>
        {
            if (!ShouldHideWhenInactive()) return;
            HideFlyout();
        };
    }

    protected virtual bool HasOpenChildWindow => false;

    protected virtual bool ShouldAutoHideWhenDeactivated => true;

    protected virtual void HideFlyout() => Hide();

    internal bool CanHideFromCoordinator => ShouldAutoHideWhenDeactivated && !HasOpenChildWindow;

    internal void HideFromCoordinator() => HideFlyout();

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
                HideFlyout();
            },
            DispatcherPriority.Input);

    private bool ShouldHideWhenInactive() =>
        !IsWarmPriming
        && CanHideFromCoordinator
        && !KeepOpenForSettingsWindow;
}
