using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace TrayAppDotNETCommon.UI;

public static class TrayAppDotNETToolTip
{
    private const double TargetGap = 4.0;
    private const PopupPositionerConstraintAdjustment NonOccludingAdjustments =
        PopupPositionerConstraintAdjustment.FlipY |
        PopupPositionerConstraintAdjustment.SlideX |
        PopupPositionerConstraintAdjustment.ResizeX;

    private static readonly CustomPopupPlacementCallback NonOccludingPlacementCallback =
        ConfigureNonOccludingPlacement;

    public static int ShowDelayMs
    {
        get;
        set => field = Math.Clamp(
            value,
            TimeConstants.ToolTipShowDelayMinMs,
            TimeConstants.ToolTipShowDelayMaxMs);
    } = TimeConstants.ToolTipShowDelayDefaultMs;

    public static void SetTip(Control control, object? tip)
    {
        ApplyNonOccludingPlacement(control);
        ApplyShowDelay(control);
        ToolTip.SetTip(control, tip);
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

    private static void ApplyNonOccludingPlacement(Control control)
    {
        ToolTip.SetPlacement(control, PlacementMode.Custom);
        ToolTip.SetHorizontalOffset(control, 0);
        ToolTip.SetVerticalOffset(control, 0);
        ToolTip.SetCustomPopupPlacementCallback(control, NonOccludingPlacementCallback);
    }

    private static void ConfigureNonOccludingPlacement(CustomPopupPlacement placement)
    {
        placement.AnchorRectangle = placement.AnchorRectangle.Inflate(TargetGap);
        placement.Anchor = PopupAnchor.Top;
        placement.Gravity = PopupGravity.Top;

        // Vertical sliding or resizing can move a constrained tooltip back across its target
        placement.ConstraintAdjustment = NonOccludingAdjustments;
        placement.Offset = default;
    }

    private static bool IsEngagingPress(Control control, PointerPressedEventArgs e)
    {
        PointerUpdateKind kind = e.GetCurrentPoint(control).Properties.PointerUpdateKind;
        return kind is PointerUpdateKind.LeftButtonPressed
            or PointerUpdateKind.RightButtonPressed
            or PointerUpdateKind.MiddleButtonPressed;
    }
}
