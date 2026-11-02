using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>
/// Keeps a full-size invisible hit target around a smaller visual button surface. Hover, press and activation follow
/// the shared <see cref="FlyoutButtonState"/> rule, painted on the inner surface.
/// </summary>
internal sealed class InsetGlyphButton : Border, IDisposable
{
    private const double EnabledOpacity = 1;

    private readonly Border _surface;
    private readonly FlyoutButtonState _state;
#if DEBUG
    private readonly TextBlock _glyphText;
#endif
    private double _disabledOpacity;
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

        _state = FlyoutButtonState.Attach(
            this,
            static () => Brushes.Transparent,
            () => TrayAppDotNETSettingsUI.Brush(palette.Hover),
            () => TrayAppDotNETSettingsUI.Brush(palette.Pressed),
            _ =>
            {
                if (!_disposed) Click?.Invoke(this, EventArgs.Empty);
            },
            surface: _surface);
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
        UpdateEnabledVisual();
    }
#endif

    /// <summary>Dims a disabled button; the shared state drops its hover and press surface.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_disposed || change.Property != IsEnabledProperty) return;

        UpdateEnabledVisual();
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (_disposed || !IsEnabled || eventArgs.Key is not (Key.Enter or Key.Space)) return;

        Click?.Invoke(this, EventArgs.Empty);
        eventArgs.Handled = true;
    }

    private void UpdateEnabledVisual()
    {
        _state.IsEnabled = IsEnabled;

        // A disabled inset button shows no cursor of its own, like the text it sits in
        Cursor = IsEnabled ? TrayAppDotNETCursors.Hand : null;
        Opacity = IsEnabled ? EnabledOpacity : _disabledOpacity;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        KeyDown -= OnKeyDown;
        Click = null;
        Cursor = null;
        Child = null;
    }
}
