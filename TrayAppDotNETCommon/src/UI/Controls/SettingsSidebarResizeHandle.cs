using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TrayAppDotNETCommon.UI.Controls;

/// <summary>
/// Provides an invisible Ctrl-gated hit target for resizing a settings navigation sidebar.
/// </summary>
internal sealed class SettingsSidebarResizeHandle : Border, IDisposable
{
    private readonly Control _coordinateSpace;
    private readonly Func<double> _getCurrentWidth;
    private readonly Func<double> _getMaximumWidth;
    private readonly Action<double> _previewWidth;
    private readonly Action<double> _commitWidth;
    private readonly Action _resetWidth;
    private readonly double _minimumWidth;
    private IPointer? _capturedPointer;
    private double _dragStartX;
    private double _dragStartWidth;
    private double _previewedWidth;
    private bool _isControlModifierDown;
    private bool _disposed;

    public SettingsSidebarResizeHandle(
        Control coordinateSpace,
        double hitTargetWidth,
        double minimumWidth,
        Func<double> getCurrentWidth,
        Func<double> getMaximumWidth,
        Action<double> previewWidth,
        Action<double> commitWidth,
        Action resetWidth)
    {
        ArgumentNullException.ThrowIfNull(coordinateSpace);
        ArgumentNullException.ThrowIfNull(getCurrentWidth);
        ArgumentNullException.ThrowIfNull(getMaximumWidth);
        ArgumentNullException.ThrowIfNull(previewWidth);
        ArgumentNullException.ThrowIfNull(commitWidth);
        ArgumentNullException.ThrowIfNull(resetWidth);

        _coordinateSpace = coordinateSpace;
        _getCurrentWidth = getCurrentWidth;
        _getMaximumWidth = getMaximumWidth;
        _previewWidth = previewWidth;
        _commitWidth = commitWidth;
        _resetWidth = resetWidth;
        _minimumWidth = minimumWidth;

        Width = hitTargetWidth;
        HorizontalAlignment = HorizontalAlignment.Right;
        Background = Brushes.Transparent;
        Cursor = TrayAppDotNETCursors.SizeWestEast;
        IsHitTestVisible = false;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
    }

    /// <summary>Shows or hides the resize hit target as the Ctrl modifier changes.</summary>
    public void SetControlModifierDown(bool isControlModifierDown)
    {
        if (_disposed) return;

        _isControlModifierDown = isControlModifierDown;
        IsHitTestVisible = isControlModifierDown || _capturedPointer != null;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        bool isControlModifierDown =
            (eventArgs.KeyModifiers & KeyModifiers.Control) != 0 || _isControlModifierDown;
        SetControlModifierDown(isControlModifierDown);
        if (!isControlModifierDown) return;

        PointerPoint pointerPoint = eventArgs.GetCurrentPoint(this);
        if (pointerPoint.Properties.IsRightButtonPressed)
        {
            eventArgs.Handled = true;
            _resetWidth();
            return;
        }

        if (!pointerPoint.Properties.IsLeftButtonPressed || _capturedPointer != null) return;

        Point position = eventArgs.GetPosition(_coordinateSpace);
        double currentWidth = _getCurrentWidth();
        if (!double.IsFinite(position.X) || !double.IsFinite(currentWidth)) return;

        _capturedPointer = eventArgs.Pointer;
        _dragStartX = position.X;
        _dragStartWidth = currentWidth;
        _previewedWidth = currentWidth;
        IsHitTestVisible = true;
        eventArgs.Handled = true;

        try
        {
            eventArgs.Pointer.Capture(this);
        }
        catch (Exception exception)
        {
            _capturedPointer = null;
            IsHitTestVisible = _isControlModifierDown;
            TADNLog.Log($"Settings sidebar pointer capture failed: {exception.Message}");
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        _isControlModifierDown = (eventArgs.KeyModifiers & KeyModifiers.Control) != 0;
        if (!ReferenceEquals(_capturedPointer, eventArgs.Pointer))
        {
            IsHitTestVisible = _isControlModifierDown;
            return;
        }

        Point position = eventArgs.GetPosition(_coordinateSpace);
        double maximumWidth = Math.Max(_minimumWidth, _getMaximumWidth());
        if (!double.IsFinite(position.X) || !double.IsFinite(maximumWidth)) return;

        double nextWidth = Math.Clamp(
            _dragStartWidth + position.X - _dragStartX,
            _minimumWidth,
            maximumWidth);
        if (SettingsSidebarWidthLayout.AreEqual(nextWidth, _previewedWidth)) return;

        _previewedWidth = nextWidth;
        _previewWidth(nextWidth);
        eventArgs.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        if (!ReferenceEquals(_capturedPointer, eventArgs.Pointer)
            || eventArgs.InitialPressMouseButton != MouseButton.Left)
            return;

        _isControlModifierDown = (eventArgs.KeyModifiers & KeyModifiers.Control) != 0;
        eventArgs.Handled = true;
        CompleteDrag();
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs eventArgs)
    {
        if (!ReferenceEquals(_capturedPointer, eventArgs.Pointer)) return;

        _capturedPointer = null;
        IsHitTestVisible = _isControlModifierDown;
        CommitPreviewedWidthIfChanged();
    }

    private void CompleteDrag()
    {
        IPointer? capturedPointer = _capturedPointer;
        _capturedPointer = null;
        IsHitTestVisible = _isControlModifierDown;

        try
        {
            capturedPointer?.Capture(null);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Settings sidebar pointer release failed: {exception.Message}");
        }

        CommitPreviewedWidthIfChanged();
    }

    private void CommitPreviewedWidthIfChanged()
    {
        if (SettingsSidebarWidthLayout.AreEqual(_previewedWidth, _dragStartWidth)) return;

        _commitWidth(_previewedWidth);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        PointerPressed -= OnPointerPressed;
        PointerMoved -= OnPointerMoved;
        PointerReleased -= OnPointerReleased;
        PointerCaptureLost -= OnPointerCaptureLost;

        IPointer? capturedPointer = _capturedPointer;
        _capturedPointer = null;
        IsHitTestVisible = false;
        Cursor = null;
        try
        {
            capturedPointer?.Capture(null);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Settings sidebar pointer cleanup failed: {exception.Message}");
        }
    }
}

/// <summary>Normalizes persisted and window-constrained settings sidebar widths.</summary>
internal static class SettingsSidebarWidthLayout
{
    private const double WidthComparisonTolerance = 0.01;

    public static double ResolvePersistedWidth(
        double persistedWidth,
        double defaultWidth,
        double minimumWidth,
        double maximumWidth)
    {
        double normalizedMaximumWidth = NormalizeMaximumWidth(minimumWidth, maximumWidth);
        double normalizedDefaultWidth = double.IsFinite(defaultWidth)
            ? Math.Clamp(defaultWidth, minimumWidth, normalizedMaximumWidth)
            : minimumWidth;
        double candidateWidth = double.IsFinite(persistedWidth) && persistedWidth > 0
            ? persistedWidth
            : normalizedDefaultWidth;
        return Math.Clamp(candidateWidth, minimumWidth, normalizedMaximumWidth);
    }

    public static double GetAvailableMaximumWidth(
        double windowWidth,
        double minimumWidth,
        double maximumWidth,
        double minimumContentWidth)
    {
        double normalizedMaximumWidth = NormalizeMaximumWidth(minimumWidth, maximumWidth);
        if (!double.IsFinite(windowWidth) || windowWidth <= 0) return normalizedMaximumWidth;

        double normalizedMinimumContentWidth = double.IsFinite(minimumContentWidth)
            ? Math.Max(val1: 0, minimumContentWidth)
            : 0;
        return Math.Clamp(
            windowWidth - normalizedMinimumContentWidth,
            minimumWidth,
            normalizedMaximumWidth);
    }

    public static bool AreEqual(double left, double right) =>
        Math.Abs(left - right) < WidthComparisonTolerance;

    private static double NormalizeMaximumWidth(double minimumWidth, double maximumWidth) =>
        double.IsFinite(maximumWidth) ? Math.Max(minimumWidth, maximumWidth) : minimumWidth;
}
