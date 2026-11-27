using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>
/// A borderless glyph button that sizes and centers its glyph by the glyph's drawn outline rather than its font box,
/// so different glyphs look the same size and sit on the same center. Hover emboldens the glyph instead of painting a
/// backdrop.
/// </summary>
internal sealed class InkGlyphButton : Border, IDisposable
{
    private const double EnabledOpacity = 1;

    private readonly InkGlyph _glyph;
    private readonly FlyoutButtonState _state;
    private readonly double _disabledOpacity;
    private bool _disposed;

    public InkGlyphButton(
        Glyph glyph,
        SettingsPalette palette,
        double hitTargetWidth,
        double hitTargetHeight,
        double inkSize,
        double hoverStrokeThickness,
        double disabledOpacity)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        ArgumentNullException.ThrowIfNull(palette);

        _disabledOpacity = Math.Clamp(disabledOpacity, min: 0, max: 1);
        Width = Math.Max(0, hitTargetWidth);
        Height = Math.Max(0, hitTargetHeight);
        Background = Brushes.Transparent;
        Focusable = true;
        _glyph = new InkGlyph(
            glyph,
            TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            Math.Max(0, inkSize),
            Math.Max(0, hoverStrokeThickness))
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false
        };
        Child = _glyph;

        _state = FlyoutButtonState.Attach(
            this,
            static () => Brushes.Transparent,
            static () => Brushes.Transparent,
            static () => Brushes.Transparent,
            _ =>
            {
                if (!_disposed) Click?.Invoke(this, EventArgs.Empty);
            });
        KeyDown += OnKeyDown;
        UpdateEnabledVisual();
    }

    public event EventHandler? Click;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_disposed) return;

        if (change.Property == IsEnabledProperty)
            UpdateEnabledVisual();
        else if (change.Property == IsPointerOverProperty)
            _glyph.SetEmboldened(IsPointerOver && IsEnabled);
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
        Cursor = IsEnabled ? TrayAppDotNETCursors.Hand : null;
        Opacity = IsEnabled ? EnabledOpacity : _disabledOpacity;
        _glyph.SetEmboldened(IsPointerOver && IsEnabled);
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

    /// <summary>Draws one glyph's outline scaled so its larger side spans the ink size, centered in the control.</summary>
    private sealed class InkGlyph : Control
    {
        // Outlines are built at this size and scaled, so every ink size shares one geometry
        private const double OutlineEmSize = 100;

        private readonly Geometry? _outline;
        private readonly Rect _outlineBounds;
        private readonly IBrush _brush;
        private readonly double _inkSize;
        private readonly double _hoverStrokeThickness;
        private bool _isEmboldened;

        public InkGlyph(Glyph glyph, IBrush brush, double inkSize, double hoverStrokeThickness)
        {
            _brush = brush;
            _inkSize = inkSize;
            _hoverStrokeThickness = hoverStrokeThickness;
            FormattedText text = new(
                glyph.Text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(
                    TADNFontResolver.ResolveFontFamily(glyph.Font),
                    FontStyle.Normal,
                    glyph.FontWeight ?? FontWeight.Normal),
                OutlineEmSize,
                brush);
            _outline = text.BuildGeometry(default);
            _outlineBounds = _outline?.Bounds ?? default;
        }

        public void SetEmboldened(bool isEmboldened)
        {
            if (_isEmboldened == isEmboldened) return;

            _isEmboldened = isEmboldened;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            double outlineSize = Math.Max(_outlineBounds.Width, _outlineBounds.Height);
            if (_outline == null || outlineSize <= 0 || _inkSize <= 0) return;

            double scale = _inkSize / outlineSize;
            Matrix transform = Matrix.CreateTranslation(-_outlineBounds.Center.X, -_outlineBounds.Center.Y)
                               * Matrix.CreateScale(scale, scale)
                               * Matrix.CreateTranslation(Bounds.Width / 2, Bounds.Height / 2);
            using (context.PushTransform(transform))
            {
                // The stroke is drawn in outline units, so divide by the scale to get device-independent pixels
                Pen? pen = _isEmboldened && _hoverStrokeThickness > 0
                    ? new Pen(_brush, _hoverStrokeThickness / scale, lineJoin: PenLineJoin.Round)
                    : null;
                context.DrawGeometry(_brush, pen, _outline);
            }
        }
    }
}
