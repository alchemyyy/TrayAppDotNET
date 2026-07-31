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
    public required FlyoutDockingController Docking { get; init; }
    public required FlyoutControlPalette Palette { get; init; }
    public Func<bool>? CanStartInteraction { get; init; }
    public Action<bool>? DraggingChanged { get; init; }
    public Action<FlyoutDockStateChange?>? InteractionCompleted { get; init; }
    public Func<string> UndockTooltip { get; init; } = static () => "Undock";
    public Func<string> RedockTooltip { get; init; } = static () => "Redock";
    public double Width { get; init; } = FlyoutUndockButtonLayout.Width;
    public double Height { get; init; } = FlyoutUndockButtonLayout.Height;
    public double FontSize { get; init; } = FlyoutUndockButtonLayout.FontSize;
    public string? FontFamily { get; init; }
    public FontWeight? FontWeight { get; init; }
    public double DragThreshold { get; init; } = FlyoutUndockButtonLayout.DragThreshold;
    public bool IsEnabled { get; init; } = true;
    public bool IsVisible { get; init; } = true;
    public Thickness Margin { get; init; } = FlyoutUndockButtonLayout.Margin;
    public CornerRadius CornerRadius { get; init; } = FlyoutUndockButtonLayout.CornerRadius;
}

public sealed class FlyoutUndockButtonController : IDisposable
{
    private readonly Window _owner;
    private readonly FlyoutDockingController _docking;
    private readonly FlyoutControlPalette _palette;
    private readonly Func<bool>? _canStartInteraction;
    private readonly Action<bool>? _draggingChanged;
    private readonly Action<FlyoutDockStateChange?>? _interactionCompleted;
    private readonly Func<string> _undockTooltip;
    private readonly Func<string> _redockTooltip;
    private readonly double _dragThreshold;
    private readonly bool _applyGlyphFontFamily;

    private bool _pointerInside;
    private bool _disposed;
    private IPointer? _capturedPointer;

    public FlyoutUndockButtonController(FlyoutUndockButtonOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _owner = options.Owner ?? throw new ArgumentNullException(nameof(options.Owner));
        _docking = options.Docking ?? throw new ArgumentNullException(nameof(options.Docking));
        _palette = options.Palette;
        _canStartInteraction = options.CanStartInteraction;
        _draggingChanged = options.DraggingChanged;
        _interactionCompleted = options.InteractionCompleted;
        _undockTooltip = options.UndockTooltip ?? throw new ArgumentNullException(nameof(options.UndockTooltip));
        _redockTooltip = options.RedockTooltip ?? throw new ArgumentNullException(nameof(options.RedockTooltip));
        _dragThreshold = options.DragThreshold;
        _applyGlyphFontFamily = options.FontFamily == null;

        Glyph = TrayAppDotNETFlyoutUI.IconText(CurrentGlyph(), _palette, options.FontSize, options.FontFamily,
            options.FontWeight);
        Button = new Border
        {
            Width = options.Width,
            Height = options.Height,
            Margin = options.Margin,
            CornerRadius = options.CornerRadius,
            Background = Brushes.Transparent,
            Child = Glyph,
            Cursor = options.IsEnabled ? TrayAppDotNETCursors.Hand : TrayAppDotNETCursors.Arrow,
            IsEnabled = options.IsEnabled,
            IsVisible = options.IsVisible
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

    public IPointer? CapturedPointer => _capturedPointer;

    public void UpdateVisual()
    {
        GlyphApplicator.ApplyTo(Glyph, CurrentGlyph(), _applyGlyphFontFamily);
        TrayAppDotNETToolTip.SetTip(Button, _docking.IsUndocked ? _redockTooltip() : _undockTooltip());
    }

    private TrayAppDotNETCommon.Visuals.Glyph CurrentGlyph() =>
        _docking.IsUndocked ? GlyphCatalog.REDOCK : GlyphCatalog.UNDOCK;

    private void WireButton()
    {
        Button.PointerEntered += OnPointerEntered;
        Button.PointerExited += OnPointerExited;
        Button.PointerPressed += OnPointerPressed;
        Button.PointerMoved += OnPointerMoved;
        Button.PointerReleased += OnPointerReleased;
        Button.PointerCaptureLost += OnPointerCaptureLost;
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (_disposed) return;
        _pointerInside = true;
        if (!IsDragging && Button.IsEnabled)
            Button.Background = TrayAppDotNETFlyoutUI.Brush(_palette.Hover);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (_disposed) return;
        _pointerInside = false;
        if (!IsDragging)
            Button.Background = Brushes.Transparent;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_disposed || !Button.IsEnabled) return;
        if (_canStartInteraction?.Invoke() == false)
        {
            e.Handled = true;
            return;
        }
        if (e.GetCurrentPoint(Button).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;

        _pointerInside = true;
        BeginButtonDrag(e);
        Button.Background = TrayAppDotNETFlyoutUI.Brush(_palette.Pressed);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_disposed || !IsPointerCaptured) return;
        if (!ReferenceEquals(_capturedPointer, e.Pointer)) return;
        ContinueButtonDrag(e);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_disposed || !IsPointerCaptured || e.InitialPressMouseButton != MouseButton.Left) return;
        if (!ReferenceEquals(_capturedPointer, e.Pointer)) return;

        bool releasedInside = TrayAppDotNETFlyoutUI.IsPointerInside(Button, e);
        FinishButtonDrag(e.Pointer, commitDrag: true, clickWhenNotDragged: releasedInside);
        Button.Background = releasedInside ? TrayAppDotNETFlyoutUI.Brush(_palette.Hover) : Brushes.Transparent;
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_disposed || !IsPointerCaptured) return;
        if (!ReferenceEquals(_capturedPointer, e.Pointer)) return;

        FinishButtonDrag(e.Pointer, commitDrag: DragOccurred, clickWhenNotDragged: false);
        Button.Background = _pointerInside ? TrayAppDotNETFlyoutUI.Brush(_palette.Hover) : Brushes.Transparent;
    }

    private void BeginButtonDrag(PointerPressedEventArgs e)
    {
        (PixelPoint dockedPosition, int snapTolerance) = _docking.CaptureDockedPosition();
        PixelPoint pointer = Button.PointToScreen(e.GetPosition(Button));

        _docking.DragHelper.BeginDrag(pointer, _owner.Position, dockedPosition, snapTolerance);
        IsPointerCaptured = true;
        DragOccurred = false;
        _capturedPointer = e.Pointer;
        SetDragging(true);
        try
        {
            e.Pointer.Capture(Button);
        }
        catch
        {
            CancelInteraction();
            throw;
        }
    }

    private void ContinueButtonDrag(PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(_owner).Properties.IsLeftButtonPressed)
        {
            FinishButtonDrag(e.Pointer, commitDrag: true, clickWhenNotDragged: false);
            return;
        }

        PixelPoint pointer = Button.PointToScreen(e.GetPosition(Button));
        PixelPoint natural = _docking.DragHelper.ComputeNatural(pointer);

        if (!DragOccurred)
        {
            double thresholdPixels = _dragThreshold * _owner.RenderScaling;
            if (!_docking.DragHelper.ExceedsThreshold(natural, thresholdPixels)) return;

            DragOccurred = true;
            _docking.SetUndockedFromDrag();
            UpdateVisual();
        }

        _docking.DragHelper.ApplyDragPosition(_owner, natural);
    }

    private void FinishButtonDrag(IPointer? pointer, bool commitDrag, bool clickWhenNotDragged)
    {
        bool dragOccurred = DragOccurred;
        FlyoutDockStateChange? committedChange = null;
        IPointer? capturedPointer = _capturedPointer ?? pointer;
        IsPointerCaptured = false;
        DragOccurred = false;
        _capturedPointer = null;
        SetDragging(false);
        ReleasePointerCapture(capturedPointer);

        try
        {
            if (dragOccurred)
            {
                if (commitDrag) committedChange = _docking.CommitDragPosition();
                return;
            }

            if (clickWhenNotDragged)
            {
                _docking.ToggleUndocked();
            }
        }
        finally
        {
            _interactionCompleted?.Invoke(committedChange);
        }
    }

    private void SetDragging(bool value)
    {
        if (IsDragging == value) return;
        IsDragging = value;
        _draggingChanged?.Invoke(value);
    }

    /// <summary>Releases an active button interaction without committing it.</summary>
    public void CancelInteraction()
    {
        IPointer? capturedPointer = _capturedPointer;
        _capturedPointer = null;
        IsPointerCaptured = false;
        DragOccurred = false;
        SetDragging(false);
        ReleasePointerCapture(capturedPointer);
    }

    private static void ReleasePointerCapture(IPointer? pointer)
    {
        if (pointer == null) return;

        try
        {
            pointer.Capture(null);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"FlyoutUndockButtonController pointer release failed: {exception.Message}");
        }
    }

    /// <summary>Releases pointer capture and every handler owned by the generated button.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CancelInteraction();

        Button.PointerEntered -= OnPointerEntered;
        Button.PointerExited -= OnPointerExited;
        Button.PointerPressed -= OnPointerPressed;
        Button.PointerMoved -= OnPointerMoved;
        Button.PointerReleased -= OnPointerReleased;
        Button.PointerCaptureLost -= OnPointerCaptureLost;
        TrayAppDotNETToolTip.SetTip(Button, null);
        Button.Cursor = null;
        Button.Child = null;
    }
}

internal static class FlyoutUndockButtonLayout
{
    private static readonly Lazy<FlyoutUndockButtonResources> Resources = new(
        static () => new FlyoutUndockButtonResources());

    private static FlyoutUndockButtonResources AXAMLResources => Resources.Value;

    public static double Width => AXAMLResources.AxamlFlyoutUndockButton.Width;

    public static double Height => AXAMLResources.AxamlFlyoutUndockButton.Height;

    public static double FontSize => AXAMLResources.AxamlFlyoutUndockButton.FontSize;

    public static double DragThreshold => AXAMLResources.AxamlFlyoutUndockButton.DragThreshold;

    public static Thickness Margin => AXAMLResources.AxamlFlyoutUndockButton.Margin;

    public static CornerRadius CornerRadius => AXAMLResources.AxamlFlyoutUndockButton.CornerRadius;
}
