using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace TrayAppDotNETCommon.UI.Drag;

public sealed record VerticalReorderGestureOptions
{
    /// <summary>Gets the panel whose children are the reorderable rows, in display order.</summary>
    public required Panel List { get; init; }

    /// <summary>
    /// Gets the callback that applies a finished drag: the dragged row's index among the list's children and the
    /// index it should end up at. It only runs when the two differ.
    /// </summary>
    public required Action<int, int> Commit { get; init; }

    /// <summary>Gets the vertical distance in DIPs the pointer must travel before a press becomes a drag.</summary>
    public double Threshold { get; init; } = 4;

    /// <summary>Gets a callback raised when a row becomes or stops being the gesture's row.</summary>
    public Action<Control, bool>? ActiveRowChanged { get; init; }
}

/// <summary>
/// Drag-to-reorder for a vertical list of rows. A press captures the pointer and snapshots every row's position, so
/// the preview offsets it applies never feed back into the target. The threshold latches once crossed. A release
/// commits; a lost capture (Alt+Tab, a window taking the pointer) or <see cref="Cancel"/> puts the rows back
/// unchanged.
/// </summary>
public sealed class VerticalReorderGesture : IDisposable
{
    private readonly VerticalReorderGestureOptions _options;
    private Control? _row;
    private IPointer? _pointer;
    private double[] _midpoints = [];
    private double _startPointerY;
    private double _rowTop;
    private double _rowHeight;
    private double _shift;
    private int _sourceIndex = -1;
    private bool _disposed;

    public VerticalReorderGesture(VerticalReorderGestureOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(options.List);
        ArgumentNullException.ThrowIfNull(options.Commit);
    }

    /// <summary>Gets whether a press holds the pointer, whether or not it has become a drag.</summary>
    public bool IsActive => _pointer != null;

    /// <summary>Gets whether the pointer crossed the threshold and rows are being moved.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>Gets the index the dragged row would land at if released now.</summary>
    public int TargetIndex { get; private set; } = -1;

    /// <summary>
    /// Starts a gesture on <paramref name="row"/>, a child of the list, from a left press. Returns false when another
    /// gesture is active or the press does not qualify.
    /// </summary>
    public bool TryBegin(Control row, PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(e);
        if (_disposed || IsActive) return false;
        if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return false;

        Panel list = _options.List;
        int sourceIndex = list.Children.IndexOf(row);
        if (sourceIndex < 0) return false;

        int count = list.Children.Count;
        double[] tops = new double[count];
        double[] heights = new double[count];
        for (int index = 0; index < count; index++)
        {
            Control child = list.Children[index];
            child.RenderTransform = null;
            tops[index] = child.TranslatePoint(default, list)?.Y ?? child.Bounds.Y;
            heights[index] = child.Bounds.Height;
        }

        _midpoints = new double[count - 1];
        for (int index = 0, other = 0; index < count; index++)
        {
            if (index == sourceIndex) continue;
            _midpoints[other++] = tops[index] + heights[index] / 2.0;
        }

        double spacing = list is StackPanel stack ? stack.Spacing : 0;
        _row = row;
        _pointer = e.Pointer;
        _sourceIndex = sourceIndex;
        _rowTop = tops[sourceIndex];
        _rowHeight = Math.Max(val1: 1, heights[sourceIndex]);
        _shift = _rowHeight + spacing + row.Margin.Top + row.Margin.Bottom;
        _startPointerY = e.GetPosition(list).Y;
        TargetIndex = sourceIndex;
        IsDragging = false;

        row.PointerMoved += OnPointerMoved;
        row.PointerReleased += OnPointerReleased;
        row.PointerCaptureLost += OnPointerCaptureLost;
        try
        {
            e.Pointer.Capture(row);
        }
        catch
        {
            Reset(releaseCapture: false);
            throw;
        }

        _options.ActiveRowChanged?.Invoke(row, true);
        e.Handled = true;
        return true;
    }

    /// <summary>Ends an active gesture without reordering.</summary>
    public void Cancel() => Complete(commit: false, releaseCapture: true);

    public void Dispose()
    {
        if (_disposed) return;
        Cancel();
        _disposed = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer) || _row is not { } row) return;

        double offset = e.GetPosition(_options.List).Y - _startPointerY;
        if (!IsDragging && Math.Abs(offset) < _options.Threshold) return;

        IsDragging = true;
        TargetIndex = ReorderMath.ResolveTargetIndex(_midpoints, _rowTop + offset + _rowHeight / 2.0);
        Avalonia.Controls.Controls children = _options.List.Children;
        for (int index = 0; index < children.Count; index++)
        {
            Control child = children[index];
            double previewOffset = index == _sourceIndex
                ? offset
                : ReorderMath.ResolvePreviewOffset(index, _sourceIndex, TargetIndex, _shift);
            child.RenderTransform = previewOffset == 0 ? null : new TranslateTransform(x: 0, previewOffset);
        }

        row.ZIndex = 1;
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer)) return;

        Complete(commit: true, releaseCapture: true);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer)) return;

        Complete(commit: false, releaseCapture: false);
    }

    private void Complete(bool commit, bool releaseCapture)
    {
        if (_row is not { } row) return;

        int source = _sourceIndex;
        int target = TargetIndex;
        bool moved = commit && IsDragging && target >= 0 && target != source;
        Reset(releaseCapture);
        _options.ActiveRowChanged?.Invoke(row, false);
        if (moved) _options.Commit(source, target);
    }

    private void Reset(bool releaseCapture)
    {
        Control? row = _row;
        IPointer? pointer = _pointer;
        _row = null;
        _pointer = null;
        _sourceIndex = -1;
        _midpoints = [];
        TargetIndex = -1;
        IsDragging = false;
        if (row == null) return;

        row.PointerMoved -= OnPointerMoved;
        row.PointerReleased -= OnPointerReleased;
        row.PointerCaptureLost -= OnPointerCaptureLost;
        row.ZIndex = 0;
        foreach (Control child in _options.List.Children)
            child.RenderTransform = null;

        if (!releaseCapture || pointer == null) return;
        try
        {
            pointer.Capture(null);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"VerticalReorderGesture pointer release failed: {exception}");
        }
    }
}

/// <summary>Pure reorder arithmetic shared by drag and keyboard reordering.</summary>
public static class ReorderMath
{
    /// <summary>
    /// Gets where a dragged row lands: the number of other rows (by their press-time midpoints, in order) whose
    /// midpoint lies above the dragged row's midpoint.
    /// </summary>
    public static int ResolveTargetIndex(ReadOnlySpan<double> otherMidpoints, double draggedMidpoint)
    {
        int target = 0;
        while (target < otherMidpoints.Length && draggedMidpoint > otherMidpoints[target]) target++;
        return target;
    }

    /// <summary>Gets how far a row at <paramref name="index"/> moves aside while a row travels source → target.</summary>
    public static double ResolvePreviewOffset(int index, int sourceIndex, int targetIndex, double shift)
    {
        if (index == sourceIndex) return 0;
        if (sourceIndex < targetIndex && index > sourceIndex && index <= targetIndex) return -shift;
        if (targetIndex < sourceIndex && index >= targetIndex && index < sourceIndex) return shift;
        return 0;
    }

    /// <summary>Moves the item at <paramref name="sourceIndex"/> to <paramref name="targetIndex"/>.</summary>
    public static void Move<T>(IList<T> items, int sourceIndex, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (sourceIndex < 0 || sourceIndex >= items.Count) throw new ArgumentOutOfRangeException(nameof(sourceIndex));
        int target = Math.Clamp(targetIndex, min: 0, items.Count - 1);
        if (target == sourceIndex) return;

        T item = items[sourceIndex];
        items.RemoveAt(sourceIndex);
        items.Insert(target, item);
    }
}
