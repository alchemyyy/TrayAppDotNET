using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Keeps a full-size invisible hit target around a smaller visual button surface.</summary>
internal sealed class InsetGlyphButton : Border, IDisposable
{
    private const double EnabledOpacity = 1;

    private readonly SettingsPalette _palette;
    private readonly Border _surface;
#if DEBUG
    private readonly TextBlock _glyphText;
#endif
    private double _disabledOpacity;
    private bool _isPointerOver;
    private bool _isPressed;
    private bool _disposed;

    public InsetGlyphButton(
        Glyph glyph,
        SettingsPalette palette,
        double hitTargetSize,
        double glyphFontSize,
        double visualInset,
        CornerRadius cornerRadius,
        Thickness visualPadding,
        double glyphOpacity,
        double disabledOpacity)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        ArgumentNullException.ThrowIfNull(palette);

        _palette = palette;
        _disabledOpacity = Math.Clamp(disabledOpacity, min: 0, max: 1);
        double normalizedHitTargetSize = Math.Max(val1: 0, hitTargetSize);
        double normalizedInset = Math.Clamp(
            visualInset,
            min: 0,
            normalizedHitTargetSize / 2);
        Width = normalizedHitTargetSize;
        Height = normalizedHitTargetSize;
        MinHeight = normalizedHitTargetSize;
        Background = Brushes.Transparent;
        Cursor = TrayAppDotNETCursors.Hand;
        Focusable = true;

        TextBlock glyphText = TrayAppDotNETSettingsUI.Text(
            string.Empty,
            palette,
            Math.Max(val1: 0, glyphFontSize));
#if DEBUG
        _glyphText = glyphText;
#endif
        GlyphApplicator.ApplyTo(glyphText, glyph);
        glyphText.HorizontalAlignment = HorizontalAlignment.Center;
        glyphText.VerticalAlignment = VerticalAlignment.Center;
        glyphText.IsHitTestVisible = false;
        glyphText.Opacity = Math.Clamp(glyphOpacity, min: 0, max: 1);

        _surface = new Border
        {
            Margin = new Thickness(normalizedInset),
            Padding = visualPadding,
            CornerRadius = cornerRadius,
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
            Child = glyphText
        };
        Child = _surface;

        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        KeyDown += OnKeyDown;
    }

    public event EventHandler? Click;

#if DEBUG
    /// <summary>Applies current glyph-button metrics while retaining its input and pointer state.</summary>
    internal void ApplyAXAMLResources(
        Glyph glyph,
        double hitTargetSize,
        double glyphFontSize,
        double visualInset,
        CornerRadius cornerRadius,
        Thickness visualPadding,
        double glyphOpacity,
        double disabledOpacity)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        if (_disposed) return;

        double normalizedHitTargetSize = Math.Max(val1: 0, hitTargetSize);
        double normalizedInset = Math.Clamp(
            visualInset,
            min: 0,
            normalizedHitTargetSize / 2);
        Width = normalizedHitTargetSize;
        Height = normalizedHitTargetSize;
        MinHeight = normalizedHitTargetSize;
        _surface.Margin = new Thickness(normalizedInset);
        _surface.Padding = visualPadding;
        _surface.CornerRadius = cornerRadius;
        _glyphText.FontSize = Math.Max(val1: 0, glyphFontSize);
        _glyphText.Opacity = Math.Clamp(glyphOpacity, min: 0, max: 1);
        _disabledOpacity = Math.Clamp(disabledOpacity, min: 0, max: 1);
        GlyphApplicator.ApplyTo(_glyphText, glyph);
        UpdateVisual();
    }
#endif

    /// <summary>Resets pointer state and the cursor whenever the enabled state flips.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_disposed || change.Property != IsEnabledProperty) return;

        // Pointer events bypass a disabled control, so hover and press state would otherwise go stale
        _isPointerOver = false;
        _isPressed = false;
        Cursor = IsEnabled ? TrayAppDotNETCursors.Hand : null;
        UpdateVisual();
    }

    private void OnPointerEntered(object? sender, PointerEventArgs eventArgs)
    {
        if (_disposed) return;

        _isPointerOver = true;
        UpdateVisual();
    }

    private void OnPointerExited(object? sender, PointerEventArgs eventArgs)
    {
        if (_disposed) return;

        _isPointerOver = false;
        _isPressed = false;
        UpdateVisual();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (_disposed || !IsEnabled
                      || !eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _isPressed = true;
        UpdateVisual();
        eventArgs.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        if (_disposed || !IsEnabled || eventArgs.InitialPressMouseButton != MouseButton.Left)
            return;

        bool releasedInside = TrayAppDotNETFlyoutUI.IsPointerInside(this, eventArgs);
        bool clicked = _isPressed && releasedInside;
        _isPointerOver = releasedInside;
        _isPressed = false;
        UpdateVisual();
        if (!clicked) return;

        Click?.Invoke(this, EventArgs.Empty);
        eventArgs.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (_disposed || !IsEnabled || eventArgs.Key is not (Key.Enter or Key.Space)) return;

        Click?.Invoke(this, EventArgs.Empty);
        eventArgs.Handled = true;
    }

    private void UpdateVisual()
    {
        // A disabled button dims its glyph and never shows a hover or pressed surface
        if (!IsEnabled)
        {
            Opacity = _disabledOpacity;
            _surface.Background = Brushes.Transparent;
            return;
        }

        Opacity = EnabledOpacity;
        _surface.Background = _isPressed
            ? TrayAppDotNETSettingsUI.Brush(_palette.Pressed)
            : _isPointerOver
                ? TrayAppDotNETSettingsUI.Brush(_palette.Hover)
                : Brushes.Transparent;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        PointerEntered -= OnPointerEntered;
        PointerExited -= OnPointerExited;
        PointerPressed -= OnPointerPressed;
        PointerReleased -= OnPointerReleased;
        KeyDown -= OnKeyDown;
        Click = null;
        Cursor = null;
        Child = null;
    }
}
