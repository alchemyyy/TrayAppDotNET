using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BatteryTrayAppDotNET.UI.Flyout;

/// <summary>A battery outline with the flyout's continuously proportional charge fill.</summary>
public sealed partial class BatteryChargeGlyph : UserControl
{
    private readonly BatterySnapshot? _snapshot;
    private readonly Color _outlineColor;
    private readonly Color _fillColor;
    private readonly double _height;

    public BatteryChargeGlyph()
    {
        InitializeComponent();
        IsHitTestVisible = false;
    }

    internal BatteryChargeGlyph(BatterySnapshot snapshot, Color outlineColor, Color fillColor, double height)
        : this()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _snapshot = snapshot;
        _outlineColor = outlineColor;
        _fillColor = fillColor;
        _height = height;
        InitializeComponentState();
    }

    /// <summary>Reapplies code-created geometry when this control's AXAML resources reload.</summary>
    private void InitializeComponentState()
    {
        if (_snapshot == null) return;

        BatteryGlyphAxamlProperties layout = AxamlBatteryGlyph;
        double bodyWidth = _height * layout.BodyWidthRatio;
        double terminalWidth = _height * layout.TerminalWidthRatio;
        double stroke = _height * layout.StrokeRatio;
        double inset = _height * layout.FillInsetRatio;
        double fillFraction = _snapshot.BatteryPresent
            ? Math.Clamp(_snapshot.ChargePercentage, min: 0, max: 100) / 100.0
            : 0;
        double fillWidth = Math.Max(val1: 0, bodyWidth - inset * 2) * fillFraction;
        double fillHeight = Math.Max(val1: 0, _height - inset * 2);
        double fillRadius = Math.Min(_height * layout.FillCornerRadiusRatio, Math.Min(fillWidth, fillHeight) / 2);
        SolidColorBrush outlineBrush = new(_outlineColor);

        double baseWidth = bodyWidth + terminalWidth;
        double scale = double.IsFinite(layout.Scale) && layout.Scale > 0 ? layout.Scale : 1;
        Width = baseWidth * scale;
        Height = _height * scale;
        VerticalAlignment = VerticalAlignment.Center;

        // Preserve the existing BatteryBar's Grid/Border composition and proportional width calculation.
        Grid battery = new() { Width = baseWidth, Height = _height };
        battery.Children.Add(new Border
        {
            Width = terminalWidth + stroke,
            Height = _height * layout.TerminalHeightRatio,
            Margin = new Thickness(bodyWidth - stroke, top: 0, right: 0, bottom: 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Background = outlineBrush,
            CornerRadius = new CornerRadius(_height * layout.TerminalCornerRadiusRatio)
        });
        battery.Children.Add(new Border
        {
            Width = bodyWidth,
            Height = _height,
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderBrush = outlineBrush,
            BorderThickness = new Thickness(stroke),
            CornerRadius = new CornerRadius(_height * layout.BodyCornerRadiusRatio)
        });
        battery.Children.Add(new Border
        {
            Width = fillWidth,
            Height = fillHeight,
            Margin = new Thickness(inset, top: 0, right: 0, bottom: 0),
            RenderTransform = layout.FillTransform,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(_fillColor),
            CornerRadius = new CornerRadius(fillRadius),
            IsVisible = fillFraction > 0
        });

        // Scale the completed visual so fill offsets, corner radii, and stroke widths scale together.
        Content = new Viewbox
        {
            Width = Width,
            Height = Height,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            Child = battery
        };
    }
}
