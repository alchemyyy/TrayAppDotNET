using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using TrayAppDotNETCommon.UI.Controls;

namespace TrayAppDotNETCommon.UI;

/// <summary>
/// Moves an undocked flyout when its chrome (any surface that is not an interactive control) is dragged with the left
/// button. A release or a lost capture commits the position through the docking controller, which snaps back to the
/// dock inside the snap tolerance; <see cref="Cancel"/> releases the pointer without committing.
/// </summary>
public sealed class FlyoutChromeDragController : IDisposable
{
    private readonly Window _owner;
    private readonly FlyoutDockingController _docking;
    private readonly Func<bool> _canStart;
    private readonly Action<bool>? _draggingChanged;
    private readonly Action? _completed;
    private Control? _captureOwner;
    private IPointer? _capturedPointer;
    private bool _disposed;

    public FlyoutChromeDragController(
        Window owner,
        FlyoutDockingController docking,
        Func<bool> canStart,
        Action<bool>? draggingChanged = null,
        Action? completed = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _docking = docking ?? throw new ArgumentNullException(nameof(docking));
        _canStart = canStart ?? throw new ArgumentNullException(nameof(canStart));
        _draggingChanged = draggingChanged;
        _completed = completed;
    }

    /// <summary>Gets whether a chrome drag currently holds the pointer.</summary>
    public bool IsDragging => _capturedPointer != null;

    /// <summary>Makes a surface start chrome drags; disposing the result detaches it and cancels its drag.</summary>
    public IDisposable Attach(Control surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        surface.PointerPressed += OnPointerPressed;
        surface.PointerMoved += OnPointerMoved;
        surface.PointerReleased += OnPointerReleased;
        surface.PointerCaptureLost += OnPointerCaptureLost;
        return new Attachment(this, surface);
    }

    /// <summary>Releases an active drag without committing its position.</summary>
    public void Cancel() => End(commit: false, releaseCapture: true);

    public void Dispose()
    {
        if (_disposed) return;
        Cancel();
        _disposed = true;
    }

    private void Detach(Control surface)
    {
        if (ReferenceEquals(_captureOwner, surface)) Cancel();
        surface.PointerPressed -= OnPointerPressed;
        surface.PointerMoved -= OnPointerMoved;
        surface.PointerReleased -= OnPointerReleased;
        surface.PointerCaptureLost -= OnPointerCaptureLost;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_disposed || IsDragging || sender is not Control surface) return;
        if (!_docking.IsUndocked || !_canStart()) return;
        if (TrayAppDotNETFlyoutUI.IsInteractiveDragSource(e.Source as Visual)) return;
        if (e.GetCurrentPoint(surface).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;

        (PixelPoint dockedPosition, int snapTolerance) = _docking.CaptureDockedPosition();
        _docking.DragHelper.BeginDrag(
            surface.PointToScreen(e.GetPosition(surface)),
            _owner.Position,
            dockedPosition,
            snapTolerance);
        _captureOwner = surface;
        _capturedPointer = e.Pointer;
        try
        {
            e.Pointer.Capture(surface);
        }
        catch
        {
            _captureOwner = null;
            _capturedPointer = null;
            throw;
        }

        _draggingChanged?.Invoke(true);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!IsDragging || !ReferenceEquals(sender, _captureOwner) || !ReferenceEquals(e.Pointer, _capturedPointer))
            return;

        Control surface = _captureOwner!;
        if (!e.GetCurrentPoint(surface).Properties.IsLeftButtonPressed)
        {
            End(commit: true, releaseCapture: true);
            e.Handled = true;
            return;
        }

        PixelPoint natural = _docking.DragHelper.ComputeNatural(surface.PointToScreen(e.GetPosition(surface)));
        _docking.DragHelper.ApplyDragPosition(_owner, natural);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsDragging || !ReferenceEquals(sender, _captureOwner) || !ReferenceEquals(e.Pointer, _capturedPointer))
            return;

        End(commit: true, releaseCapture: true);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!IsDragging || !ReferenceEquals(sender, _captureOwner) || !ReferenceEquals(e.Pointer, _capturedPointer))
            return;

        // The window already sits where the pointer left it, so a stolen capture keeps that position
        End(commit: true, releaseCapture: false);
    }

    private void End(bool commit, bool releaseCapture)
    {
        IPointer? pointer = _capturedPointer;
        if (pointer == null) return;

        _captureOwner = null;
        _capturedPointer = null;
        if (releaseCapture)
        {
            try
            {
                pointer.Capture(null);
            }
            catch (Exception exception)
            {
                TADNLog.Log($"FlyoutChromeDragController pointer release failed: {exception}");
            }
        }

        try
        {
            if (commit) _docking.CommitDragPosition();
        }
        finally
        {
            _draggingChanged?.Invoke(false);
            _completed?.Invoke();
        }
    }

    private sealed class Attachment(FlyoutChromeDragController controller, Control surface) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            controller.Detach(surface);
        }
    }
}
