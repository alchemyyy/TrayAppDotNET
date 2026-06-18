using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.Models;

namespace TrayAppDotNETCommon.UI;

public static class TrayAppDotNETToolTip
{
    public static int ShowDelayMs
    {
        get;
        set => field = Math.Clamp(
            value,
            AppSettingsCommon.ToolTipShowDelayMinMs,
            AppSettingsCommon.ToolTipShowDelayMaxMs);
    } = AppSettingsCommon.ToolTipShowDelayDefaultMs;

    public static void SetTip(Control control, object? tip)
    {
        ToolTip.SetTip(control, tip);
        ApplyShowDelay(control);
    }

    public static void SuppressWhileEngaged(Control control)
    {
        control.PointerPressed += (_, e) =>
        {
            if (!IsEngagingPress(control, e)) return;
            Suppress(control);
        };
        control.PointerReleased += (_, _) => Restore(control);
        control.PointerCaptureLost += (_, _) => Restore(control);
    }

    public static void Suppress(Control control)
    {
        ToolTip.SetIsOpen(control, false);
        ToolTip.SetServiceEnabled(control, false);
    }

    public static void Restore(Control control)
    {
        ToolTip.SetServiceEnabled(control, true);
        ApplyShowDelay(control);
    }

    public static void ApplyShowDelayToSubtree(Control root)
    {
        ApplyShowDelay(root);
        foreach (Control control in root.GetVisualDescendants().OfType<Control>())
            ApplyShowDelay(control);
    }

    private static void ApplyShowDelay(Control control)
    {
        ToolTip.SetShowDelay(control, ShowDelayMs);
        ToolTip.SetBetweenShowDelay(control, ShowDelayMs);
    }

    private static bool IsEngagingPress(Control control, PointerPressedEventArgs e)
    {
        PointerUpdateKind kind = e.GetCurrentPoint(control).Properties.PointerUpdateKind;
        return kind is PointerUpdateKind.LeftButtonPressed
            or PointerUpdateKind.RightButtonPressed
            or PointerUpdateKind.MiddleButtonPressed;
    }
}
