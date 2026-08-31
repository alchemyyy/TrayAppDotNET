using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TrayAppDotNETCommon.UI.Controls;

internal static class FlyoutFrameLayout
{
    private static FlyoutFrameResources AXAMLResources => FlyoutFrameResources.Current;

    public static Thickness ZeroThickness => AXAMLResources.AxamlFlyoutFrame.ZeroThickness;
    public static CornerRadius ZeroCornerRadius => AXAMLResources.AxamlFlyoutFrame.ZeroCornerRadius;
    public static Thickness BorderThickness => AXAMLResources.AxamlFlyoutFrame.BorderThickness;
    public static CornerRadius CornerRadius => AXAMLResources.AxamlFlyoutFrame.CornerRadius;
    public static CornerRadius InnerCornerRadius => AXAMLResources.AxamlFlyoutFrame.InnerCornerRadius;
}

/// <summary>Hosts flyout content inside the shared border and rounded clipping surfaces.</summary>
public sealed class FlyoutFrame : Border
{
    /// <summary>Resolves the shared outer radius for the current rounding preference.</summary>
    public static CornerRadius ResolveCornerRadius(bool enableRoundedCorners) =>
        enableRoundedCorners ? FlyoutFrameLayout.CornerRadius : FlyoutFrameLayout.ZeroCornerRadius;

    /// <summary>Creates a flyout frame using the shared outer and inner geometry.</summary>
    public FlyoutFrame(
        Control content,
        Color backgroundColor,
        Color borderColor,
        bool enableRoundedCorners,
        Thickness? framePadding = null,
        Thickness? contentMargin = null,
        Thickness? contentPadding = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        IBrush backgroundBrush = TrayAppDotNETFlyoutUI.Brush(backgroundColor);
        CornerRadius cornerRadius = ResolveCornerRadius(enableRoundedCorners);
        CornerRadius innerCornerRadius = enableRoundedCorners
            ? FlyoutFrameLayout.InnerCornerRadius
            : FlyoutFrameLayout.ZeroCornerRadius;

        Background = backgroundBrush;
        BorderBrush = TrayAppDotNETFlyoutUI.Brush(borderColor);
        BorderThickness = FlyoutFrameLayout.BorderThickness;
        CornerRadius = cornerRadius;
        ClipToBounds = false;
        Padding = framePadding ?? FlyoutFrameLayout.ZeroThickness;

        // Avoid outer shadows because the frame is flush with the rectangular HWND bounds
        BoxShadow = default;
        Child = new Border
        {
            Background = backgroundBrush,
            CornerRadius = innerCornerRadius,
            ClipToBounds = true,
            Margin = contentMargin ?? FlyoutFrameLayout.ZeroThickness,
            Padding = contentPadding ?? FlyoutFrameLayout.ZeroThickness,
            Child = content
        };
    }
}
