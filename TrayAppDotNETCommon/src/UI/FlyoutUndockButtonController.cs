using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI;

public sealed class FlyoutUndockButtonOptions
{
    public required Window Owner { get; init; }
    public required FlyoutWindowDragHelper DragHelper { get; init; }
    public required FlyoutControlPalette Palette { get; init; }
    public required Func<(PixelPoint DockedPosition, int SnapTolerance)> CaptureDockedPosition { get; init; }
    public required Func<bool> IsUndocked { get; init; }
    public required Action SetUndockedFromDrag { get; init; }
    public required Action ToggleUndocked { get; init; }
    public required Action CommitDragPosition { get; init; }
    public Action<bool>? DraggingChanged { get; init; }
    public Func<string> UndockTooltip { get; init; } = static () => "Undock";
    public Func<string> RedockTooltip { get; init; } = static () => "Redock";
    public double Width { get; init; } = 40;
    public double Height { get; init; } = 32;
    public double FontSize { get; init; } = 18;
    public string? FontFamily { get; init; }
    public FontWeight? FontWeight { get; init; }
    public double DragThreshold { get; init; } = 4;
    public bool IsEnabled { get; init; } = true;
    public bool IsVisible { get; init; } = true;
    public Thickness Margin { get; init; } = new(0);
    public CornerRadius CornerRadius { get; init; } = new(4);
}

public sealed class FlyoutUndockButtonController
{
    private readonly Window _owner;
    private readonly FlyoutWindowDragHelper _dragHelper;
    private readonly FlyoutControlPalette _palette;
    private readonly Func<(PixelPoint DockedPosition, int SnapTolerance)> _captureDockedPosition;
    private readonly Func<bool> _isUndocked;
    private readonly Action _setUndockedFromDrag;
    private readonly Action _toggleUndocked;
    private readonly Action _commitDragPosition;
    private readonly Action<bool>? _draggingChanged;
    private readonly Func<string> _undockTooltip;
    private readonly Func<string> _redockTooltip;
    private readonly double _dragThreshold;

    private bool _pointerInside;

    public FlyoutUndockButtonController(FlyoutUndockButtonOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _owner = options.Owner ?? throw new ArgumentNullException(nameof(options.Owner));
        _dragHelper = options.DragHelper ?? throw new ArgumentNullException(nameof(options.DragHelper));
        _palette = options.Palette;
        _captureDockedPosition = options.CaptureDockedPosition ??
                                 throw new ArgumentNullException(nameof(options.CaptureDockedPosition));
        _isUndocked = options.IsUndocked ?? throw new ArgumentNullException(nameof(options.IsUndocked));
        _setUndockedFromDrag = options.SetUndockedFromDrag ??
                               throw new ArgumentNullException(nameof(options.SetUndockedFromDrag));
        _toggleUndocked = options.ToggleUndocked ?? throw new ArgumentNullException(nameof(options.ToggleUndocked));
        _commitDragPosition = options.CommitDragPosition ??
                              throw new ArgumentNullException(nameof(options.CommitDragPosition));
        _draggingChanged = options.DraggingChanged;
        _undockTooltip = options.UndockTooltip ?? throw new ArgumentNullException(nameof(options.UndockTooltip));
        _redockTooltip = options.RedockTooltip ?? throw new ArgumentNullException(nameof(options.RedockTooltip));
        _dragThreshold = options.DragThreshold;

        Glyph = TrayAppDotNETFlyoutUI.IconText(GlyphText(), _palette, options.FontSize, options.FontFamily,
            options.FontWeight);
        Button = new Border
        {
            Width = options.Width,
            Height = options.Height,
            Margin = options.Margin,
            CornerRadius = options.CornerRadius,
            Background = Brushes.Transparent,
            Child = Glyph,
            Cursor = options.IsEnabled ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow),
            IsEnabled = options.IsEnabled,
            IsVisible = options.IsVisible,
        };

        UpdateVisual();
        TrayAppDotNETToolTip.SuppressWhileEngaged(Button);
        WireButton();
    }

    public Border Button { get; }

    public TextBlock Glyph { get; }

    public bool IsPointerCaptured { get; private set; }

    public bool DragOccurred { get; private set; }

    public bool IsDragging { get; private set; }

    public void UpdateVisual()
    {
        Glyph.Text = GlyphText();
        TrayAppDotNETToolTip.SetTip(Button, _isUndocked() ? _redockTooltip() : _undockTooltip());
    }

    private string GlyphText() =>
        _isUndocked() ? GlyphCatalog.FLYOUT_REDOCK_ACTION : GlyphCatalog.FLYOUT_UNDOCK_ACTION;

    private void WireButton()
    {
        Button.PointerEntered += (_, _) =>
        {
            _pointerInside = true;
            if (!IsDragging && Button.IsEnabled)
                Button.Background = TrayAppDotNETFlyoutUI.Brush(_palette.Hover);
        };
        Button.PointerExited += (_, _) =>
        {
            _pointerInside = false;
            if (!IsDragging)
                Button.Background = Brushes.Transparent;
        };
        Button.PointerPressed += (_, e) =>
        {
            if (!Button.IsEnabled) return;
            if (e.GetCurrentPoint(Button).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;

            _pointerInside = true;
            BeginButtonDrag(e);
            Button.Background = TrayAppDotNETFlyoutUI.Brush(_palette.Pressed);
            e.Handled = true;
        };
        Button.PointerMoved += (_, e) =>
        {
            if (!IsPointerCaptured) return;
            ContinueButtonDrag(e);
            e.Handled = true;
        };
        Button.PointerReleased += (_, e) =>
        {
            if (!IsPointerCaptured || e.InitialPressMouseButton != MouseButton.Left) return;

            bool releasedInside = TrayAppDotNETFlyoutUI.IsPointerInside(Button, e);
            FinishButtonDrag(e.Pointer, commitDrag: true, clickWhenNotDragged: releasedInside);
            Button.Background = releasedInside ? TrayAppDotNETFlyoutUI.Brush(_palette.Hover) : Brushes.Transparent;
            e.Handled = true;
        };
        Button.PointerCaptureLost += (_, _) =>
        {
            if (!IsPointerCaptured) return;

            FinishButtonDrag(null, commitDrag: DragOccurred, clickWhenNotDragged: false);
            Button.Background = _pointerInside ? TrayAppDotNETFlyoutUI.Brush(_palette.Hover) : Brushes.Transparent;
        };
    }

    private void BeginButtonDrag(PointerPressedEventArgs e)
    {
        (PixelPoint dockedPosition, int snapTolerance) = _captureDockedPosition();
        PixelPoint pointer = Button.PointToScreen(e.GetPosition(Button));

        _dragHelper.BeginDrag(pointer, _owner.Position, dockedPosition, snapTolerance);
        IsPointerCaptured = true;
        DragOccurred = false;
        SetDragging(true);
        e.Pointer.Capture(Button);
    }

    private void ContinueButtonDrag(PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(_owner).Properties.IsLeftButtonPressed)
        {
            FinishButtonDrag(e.Pointer, commitDrag: true, clickWhenNotDragged: false);
            return;
        }

        PixelPoint pointer = Button.PointToScreen(e.GetPosition(Button));
        PixelPoint natural = _dragHelper.ComputeNatural(pointer);

        if (!DragOccurred)
        {
            double thresholdPixels = _dragThreshold * _owner.RenderScaling;
            if (!_dragHelper.ExceedsThreshold(natural, thresholdPixels)) return;

            DragOccurred = true;
            _setUndockedFromDrag();
            UpdateVisual();
        }

        _dragHelper.ApplyDragPosition(_owner, natural);
    }

    private void FinishButtonDrag(IPointer? pointer, bool commitDrag, bool clickWhenNotDragged)
    {
        bool dragOccurred = DragOccurred;
        IsPointerCaptured = false;
        DragOccurred = false;
        SetDragging(false);
        pointer?.Capture(null);

        if (dragOccurred)
        {
            if (commitDrag) _commitDragPosition();
            return;
        }

        if (clickWhenNotDragged) _toggleUndocked();
    }

    private void SetDragging(bool value)
    {
        if (IsDragging == value) return;
        IsDragging = value;
        _draggingChanged?.Invoke(value);
    }
}
