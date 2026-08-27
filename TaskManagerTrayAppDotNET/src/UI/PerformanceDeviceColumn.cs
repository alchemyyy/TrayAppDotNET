using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Associates caller-owned device-row content with its persistent identity.</summary>
internal readonly record struct PerformanceDeviceColumnRow(
    string StableID,
    Control Content);

/// <summary>Pure geometry and list operations used by the Performance device column.</summary>
internal static class PerformanceDeviceColumnLayout
{
    public const double DragThreshold = 4.0;

    /// <summary>Returns whether a pending pointer gesture has reached the drag threshold.</summary>
    public static bool HasReachedDragThreshold(Point start, Point current)
    {
        if (!double.IsFinite(start.X)
            || !double.IsFinite(start.Y)
            || !double.IsFinite(current.X)
            || !double.IsFinite(current.Y))
        {
            return false;
        }

        return Math.Abs(current.X - start.X) >= DragThreshold
               || Math.Abs(current.Y - start.Y) >= DragThreshold;
    }

    /// <summary>Returns the final list index for a dragged row midpoint.</summary>
    public static int GetInsertionIndex(
        double draggedMidpointY,
        IReadOnlyList<PerformanceDeviceRowGeometry> rows,
        int sourceIndex)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (!double.IsFinite(draggedMidpointY)
            || (uint)sourceIndex >= (uint)rows.Count)
        {
            return -1;
        }

        int insertionIndex = 0;
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            if (rowIndex == sourceIndex) continue;

            PerformanceDeviceRowGeometry row = rows[rowIndex];
            if (!double.IsFinite(row.Top)
                || !double.IsFinite(row.Height)
                || row.Height < 0)
            {
                return sourceIndex;
            }

            if (draggedMidpointY > row.Top + row.Height / 2.0)
                insertionIndex++;
            else
                break;
        }

        return Math.Clamp(insertionIndex, 0, Math.Max(0, rows.Count - 1));
    }

    /// <summary>Returns a copy with one item moved to its final list index.</summary>
    public static List<TItem> Move<TItem>(
        IReadOnlyList<TItem> items,
        int sourceIndex,
        int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(items);
        if ((uint)sourceIndex >= (uint)items.Count)
            throw new ArgumentOutOfRangeException(nameof(sourceIndex));
        if ((uint)targetIndex >= (uint)items.Count)
            throw new ArgumentOutOfRangeException(nameof(targetIndex));

        List<TItem> moved = new(items.Count);
        for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
            moved.Add(items[itemIndex]);

        TItem item = moved[sourceIndex];
        moved.RemoveAt(sourceIndex);
        moved.Insert(targetIndex, item);
        return moved;
    }
}

/// <summary>Immutable vertical geometry for one device row.</summary>
internal readonly record struct PerformanceDeviceRowGeometry(
    double Top,
    double Height);

/// <summary>
/// Hosts caller-styled Performance device rows and adds selection and reorder behavior.
/// </summary>
internal sealed class PerformanceDeviceColumn : StackPanel, IDisposable
{
    private readonly Action<string> _selectionRequested;
    private readonly Action<IReadOnlyList<string>> _orderChanged;
    private readonly List<RowEntry> _rows = [];
    private IPointer? _capturedPointer;
    private RowEntry? _pressedRow;
    private Point _pressPosition;
    private double _pressedRowPointerOffsetY;
    private int _targetIndex = -1;
    private bool _isDragging;
    private bool _isResettingGesture;
    private bool _disposed;

    internal PerformanceDeviceColumn(
        Action<string> selectionRequested,
        Action<IReadOnlyList<string>> orderChanged)
    {
        ArgumentNullException.ThrowIfNull(selectionRequested);
        ArgumentNullException.ThrowIfNull(orderChanged);

        _selectionRequested = selectionRequested;
        _orderChanged = orderChanged;
        Orientation = Avalonia.Layout.Orientation.Vertical;
    }

    /// <summary>Gets a snapshot of the current visible stable-ID sequence.</summary>
    internal IReadOnlyList<string> StableIDs
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return CreateStableIDSnapshot();
        }
    }

    /// <summary>
    /// Reconciles the column to the supplied order while retaining unchanged content hosts.
    /// </summary>
    internal void ReconcileRows(IEnumerable<PerformanceDeviceColumnRow> rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(rows);

        List<PerformanceDeviceColumnRow> requestedRows = [];
        HashSet<string> requestedStableIDs = new(StringComparer.Ordinal);
        HashSet<Control> requestedContent = new(ReferenceEqualityComparer.Instance);
        foreach (PerformanceDeviceColumnRow row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.StableID))
                throw new ArgumentException("Device stable IDs cannot be empty.", nameof(rows));
            if (row.Content == null)
                throw new ArgumentException("Device row content cannot be null.", nameof(rows));
            if (!requestedStableIDs.Add(row.StableID))
                throw new ArgumentException($"Duplicate device stable ID '{row.StableID}'.", nameof(rows));
            if (!requestedContent.Add(row.Content))
                throw new ArgumentException("A device row control cannot be used more than once.", nameof(rows));

            requestedRows.Add(row);
        }

        if (MatchesCurrentRows(requestedRows)) return;

        ResetGesture(releasePointer: true);

        Dictionary<string, RowEntry> existingByStableID = new(StringComparer.Ordinal);
        foreach (RowEntry row in _rows)
            existingByStableID[row.StableID] = row;

        HashSet<RowEntry> retainedRows = [];
        foreach (PerformanceDeviceColumnRow requestedRow in requestedRows)
        {
            if (existingByStableID.TryGetValue(requestedRow.StableID, out RowEntry? existing)
                && ReferenceEquals(existing.Content, requestedRow.Content))
            {
                retainedRows.Add(existing);
            }
        }

        Children.Clear();
        foreach (RowEntry row in _rows)
        {
            if (!retainedRows.Contains(row)) DetachRow(row);
        }

        List<RowEntry> nextRows = new(requestedRows.Count);
        foreach (PerformanceDeviceColumnRow requestedRow in requestedRows)
        {
            RowEntry nextRow = existingByStableID.TryGetValue(requestedRow.StableID, out RowEntry? existing)
                               && retainedRows.Contains(existing)
                ? existing
                : AttachRow(requestedRow);
            nextRows.Add(nextRow);
        }

        _rows.Clear();
        _rows.AddRange(nextRows);
        foreach (RowEntry row in _rows)
            Children.Add(row.Host);
    }

    /// <summary>Removes all current rows and releases their gesture handlers.</summary>
    internal void ClearRows() => ReconcileRows([]);

    private RowEntry AttachRow(PerformanceDeviceColumnRow row)
    {
        Grid host = new()
        {
            Focusable = true
        };
        host.Children.Add(row.Content);
        RowEntry entry = new(row.StableID, row.Content, host);

        host.PointerPressed += OnRowPointerPressed;
        host.PointerMoved += OnRowPointerMoved;
        host.PointerReleased += OnRowPointerReleased;
        host.PointerCaptureLost += OnRowPointerCaptureLost;
        host.KeyDown += OnRowKeyDown;
        return entry;
    }

    private void DetachRow(RowEntry row)
    {
        row.Host.PointerPressed -= OnRowPointerPressed;
        row.Host.PointerMoved -= OnRowPointerMoved;
        row.Host.PointerReleased -= OnRowPointerReleased;
        row.Host.PointerCaptureLost -= OnRowPointerCaptureLost;
        row.Host.KeyDown -= OnRowKeyDown;
        row.Host.Children.Remove(row.Content);
    }

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (_disposed || sender is not Grid host) return;
        RowEntry? row = FindRow(host);
        if (row == null) return;

        PointerPoint point = eventArgs.GetCurrentPoint(host);
        if (!point.Properties.IsLeftButtonPressed) return;
        if (_capturedPointer != null)
        {
            eventArgs.Handled = true;
            return;
        }

        _capturedPointer = eventArgs.Pointer;
        _pressedRow = row;
        _pressPosition = eventArgs.GetPosition(this);
        _pressedRowPointerOffsetY = eventArgs.GetPosition(host).Y;
        _targetIndex = _rows.IndexOf(row);
        _isDragging = false;
        host.Focus();

        try
        {
            eventArgs.Pointer.Capture(host);
        }
        catch (Exception exception)
        {
            ResetGesture(releasePointer: true);
            TADNLog.Log($"PerformanceDeviceColumn pointer capture failed: {exception.Message}");
        }

        eventArgs.Handled = true;
    }

    private void OnRowPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (_disposed
            || sender is not Grid host
            || !ReferenceEquals(_capturedPointer, eventArgs.Pointer)
            || _pressedRow is not { } pressedRow
            || !ReferenceEquals(pressedRow.Host, host))
        {
            return;
        }

        Point currentPosition = eventArgs.GetPosition(this);
        if (!_isDragging)
        {
            if (!PerformanceDeviceColumnLayout.HasReachedDragThreshold(_pressPosition, currentPosition)) return;
            _isDragging = true;
            host.SetValue(Panel.ZIndexProperty, 1);
        }

        ResetPreviewTransforms();
        int sourceIndex = _rows.IndexOf(pressedRow);
        if (sourceIndex < 0)
        {
            ResetGesture(releasePointer: true);
            return;
        }

        double draggedMidpointY = currentPosition.Y
                                    - _pressedRowPointerOffsetY
                                    + Math.Max(1, host.Bounds.Height) / 2.0;
        _targetIndex = CalculateInsertionIndex(draggedMidpointY, sourceIndex);
        ApplyDragPreview(sourceIndex, _targetIndex);
        host.RenderTransform = new TranslateTransform(0, currentPosition.Y - _pressPosition.Y);
        eventArgs.Handled = true;
    }

    private void OnRowPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        if (_disposed
            || sender is not Grid host
            || !ReferenceEquals(_capturedPointer, eventArgs.Pointer)
            || _pressedRow is not { } pressedRow
            || !ReferenceEquals(pressedRow.Host, host))
        {
            return;
        }
        if (eventArgs.InitialPressMouseButton != MouseButton.Left) return;

        bool wasDragging = _isDragging;
        int sourceIndex = _rows.IndexOf(pressedRow);
        int targetIndex = _targetIndex;
        Point releasePosition = eventArgs.GetPosition(host);
        bool isClick = !wasDragging
                       && new Rect(0, 0, host.Bounds.Width, host.Bounds.Height).Contains(releasePosition);

        ResetGesture(releasePointer: true);

        if (wasDragging)
        {
            if (sourceIndex >= 0
                && targetIndex >= 0
                && targetIndex < _rows.Count
                && sourceIndex != targetIndex)
            {
                MoveRow(sourceIndex, targetIndex);
            }
        }
        else if (isClick)
        {
            _selectionRequested(pressedRow.StableID);
        }

        eventArgs.Handled = true;
    }

    private void OnRowPointerCaptureLost(object? sender, PointerCaptureLostEventArgs eventArgs)
    {
        if (_disposed
            || _isResettingGesture
            || !ReferenceEquals(_capturedPointer, eventArgs.Pointer))
        {
            return;
        }

        ResetGesture(releasePointer: false);
    }

    private void OnRowKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (_disposed || _capturedPointer != null || sender is not Grid host) return;
        RowEntry? row = FindRow(host);
        if (row == null) return;

        if ((eventArgs.KeyModifiers & KeyModifiers.Control) != 0
            && eventArgs.Key is Key.Up or Key.Down)
        {
            int sourceIndex = _rows.IndexOf(row);
            int targetIndex = eventArgs.Key == Key.Up ? sourceIndex - 1 : sourceIndex + 1;
            if (sourceIndex >= 0 && targetIndex >= 0 && targetIndex < _rows.Count)
                MoveRow(sourceIndex, targetIndex);

            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.KeyModifiers == KeyModifiers.None
            && eventArgs.Key is Key.Enter or Key.Space)
        {
            _selectionRequested(row.StableID);
            eventArgs.Handled = true;
        }
    }

    private int CalculateInsertionIndex(double draggedMidpointY, int sourceIndex)
    {
        PerformanceDeviceRowGeometry[] geometry = new PerformanceDeviceRowGeometry[_rows.Count];
        for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            Grid host = _rows[rowIndex].Host;
            Point? topLeft = host.TranslatePoint(default, this);
            if (!topLeft.HasValue) return sourceIndex;

            geometry[rowIndex] = new PerformanceDeviceRowGeometry(
                topLeft.Value.Y,
                Math.Max(1, host.Bounds.Height));
        }

        return PerformanceDeviceColumnLayout.GetInsertionIndex(
            draggedMidpointY,
            geometry,
            sourceIndex);
    }

    private void ApplyDragPreview(int sourceIndex, int targetIndex)
    {
        if ((uint)sourceIndex >= (uint)_rows.Count
            || (uint)targetIndex >= (uint)_rows.Count)
        {
            return;
        }

        double slotOffset = Math.Max(
            1,
            _rows[sourceIndex].Host.Bounds.Height + Math.Max(0, Spacing));
        if (targetIndex < sourceIndex)
        {
            for (int rowIndex = targetIndex; rowIndex < sourceIndex; rowIndex++)
                _rows[rowIndex].Host.RenderTransform = new TranslateTransform(0, slotOffset);
            return;
        }

        for (int rowIndex = sourceIndex + 1; rowIndex <= targetIndex; rowIndex++)
            _rows[rowIndex].Host.RenderTransform = new TranslateTransform(0, -slotOffset);
    }

    private void ResetPreviewTransforms()
    {
        foreach (RowEntry row in _rows)
            row.Host.RenderTransform = null;
    }

    private void MoveRow(int sourceIndex, int targetIndex)
    {
        List<RowEntry> reordered = PerformanceDeviceColumnLayout.Move(_rows, sourceIndex, targetIndex);
        RowEntry movedRow = _rows[sourceIndex];

        _rows.Clear();
        _rows.AddRange(reordered);
        Children.RemoveAt(sourceIndex);
        Children.Insert(targetIndex, movedRow.Host);
        movedRow.Host.Focus();
        _orderChanged(CreateStableIDSnapshot());
    }

    private string[] CreateStableIDSnapshot()
    {
        string[] stableIDs = new string[_rows.Count];
        for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
            stableIDs[rowIndex] = _rows[rowIndex].StableID;
        return stableIDs;
    }

    private RowEntry? FindRow(Grid host)
    {
        foreach (RowEntry row in _rows)
        {
            if (ReferenceEquals(row.Host, host)) return row;
        }

        return null;
    }

    private bool MatchesCurrentRows(IReadOnlyList<PerformanceDeviceColumnRow> rows)
    {
        if (rows.Count != _rows.Count) return false;
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            PerformanceDeviceColumnRow requestedRow = rows[rowIndex];
            RowEntry currentRow = _rows[rowIndex];
            if (!string.Equals(requestedRow.StableID, currentRow.StableID, StringComparison.Ordinal)
                || !ReferenceEquals(requestedRow.Content, currentRow.Content))
            {
                return false;
            }
        }

        return true;
    }

    private void ResetGesture(bool releasePointer)
    {
        IPointer? pointer = _capturedPointer;
        _capturedPointer = null;
        _pressedRow = null;
        _pressPosition = default;
        _pressedRowPointerOffsetY = 0;
        _targetIndex = -1;
        _isDragging = false;

        ResetPreviewTransforms();
        foreach (RowEntry row in _rows)
            row.Host.SetValue(Panel.ZIndexProperty, 0);

        if (!releasePointer || pointer == null) return;

        bool wasResettingGesture = _isResettingGesture;
        _isResettingGesture = true;
        try
        {
            pointer.Capture(null);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"PerformanceDeviceColumn pointer release failed: {exception.Message}");
        }
        finally
        {
            _isResettingGesture = wasResettingGesture;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        ResetGesture(releasePointer: true);
        Children.Clear();
        foreach (RowEntry row in _rows)
            DetachRow(row);
        _rows.Clear();
        _disposed = true;
    }

    private sealed class RowEntry(string stableID, Control content, Grid host)
    {
        public string StableID { get; } = stableID;
        public Control Content { get; } = content;
        public Grid Host { get; } = host;
    }
}
