using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.Visuals;
using TaskManagerGlyphCatalog = TaskManagerTrayAppDotNET.Visuals.GlyphCatalog;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Displays a filterable, pointer-draggable ordering over a caller-owned item list.</summary>
internal sealed class TaskManagerReorderList<TItem> : Grid, IDisposable
    where TItem : class
{
    private const string MoveUpToolTip = "Move up";
    private const string MoveDownToolTip = "Move down";

    private readonly IList<TItem> _items;
    private readonly IReadOnlyList<TItem> _readOnlyItems;
    private readonly Func<TItem, string> _getSearchText;
    private readonly Func<TItem, Control> _buildPrimaryContent;
    private readonly SettingsPalette _palette;
    private readonly bool _enableRoundedCorners;
    private readonly Action<TItem>? _activateItem;
    private readonly StackPanel _rows;
    private readonly DispatcherTimer _autoScrollTimer;
    private List<TItem> _visibleItems = [];
    private List<ReorderRow> _visibleRows = [];
    private string _filter = string.Empty;
    private IPointer? _capturedPointer;
    private TItem? _draggedItem;
    private Border? _draggedSlot;
    private Point _dragStart;
    private double _dragPointerOffsetY;
    private double _draggedSlotHeight;
    private Point _lastDragPointerPosition;
    private double[] _dragMidpoints = [];
    private int _dropInsertionIndex = -1;
    private int _publishedPreviewTargetIndex = -1;
    private int _autoScrollDirection;
    private bool _isDragging;
    private bool _hasPublishedOrderPreview;
    private bool _isResettingCapture;
    private SettingsVerticalScrollViewport? _scrollViewport;
    private double _lastViewportOffset;
    private TItem? _selectedItem;
    private int _disposed;

    public TaskManagerReorderList(
        IList<TItem> items,
        Func<TItem, string> getSearchText,
        Func<TItem, Control> buildPrimaryContent,
        SettingsPalette palette,
        bool enableRoundedCorners,
        Action<TItem>? activateItem = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(getSearchText);
        ArgumentNullException.ThrowIfNull(buildPrimaryContent);
        ArgumentNullException.ThrowIfNull(palette);
        if (items.IsReadOnly)
            throw new ArgumentException("The reorder item list must be mutable.", nameof(items));

        _items = items;
        _readOnlyItems = items as IReadOnlyList<TItem> ?? new ReadOnlyListView(items);
        _getSearchText = getSearchText;
        _buildPrimaryContent = buildPrimaryContent;
        _palette = palette;
        _enableRoundedCorners = enableRoundedCorners;
        _activateItem = activateItem;
        _rows = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Spacing = 0
        };
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Children.Add(_rows);

        _autoScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(
                TaskManagerReorderResources.Current.AutoScrollIntervalMilliseconds)
        };
        _autoScrollTimer.Tick += OnAutoScrollTick;

        TaskManagerReorderResources.ResourcesReloaded += OnResourcesReloaded;
        GlyphCatalogHotReload.ResourcesReloaded += OnGlyphResourcesReloaded;
        RebuildRows();
    }

    /// <summary>Gets a live read-only view over the caller-owned order.</summary>
    public IReadOnlyList<TItem> Items => _readOnlyItems;

    /// <summary>Raised after a pointer or button action mutates item order or state.</summary>
    public event Action? ItemsChanged;

    /// <summary>Raised with the prospective order whenever an active drag changes slots.</summary>
    public event Action<IReadOnlyList<TItem>>? OrderPreviewChanged;

    /// <summary>Enables edge auto-scroll while a row is dragged inside the supplied viewport.</summary>
    public void AttachScrollViewport(SettingsVerticalScrollViewport scrollViewport)
    {
        ArgumentNullException.ThrowIfNull(scrollViewport);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (ReferenceEquals(_scrollViewport, scrollViewport)) return;

        if (_scrollViewport != null)
            _scrollViewport.VerticalOffsetChanged -= OnScrollViewportVerticalOffsetChanged;
        _scrollViewport = scrollViewport;
        _lastViewportOffset = scrollViewport.VerticalOffset;
        scrollViewport.VerticalOffsetChanged += OnScrollViewportVerticalOffsetChanged;
    }

    /// <summary>Applies a case-insensitive fuzzy filter without changing item order.</summary>
    public void SetFilter(string? filter)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        string nextFilter = filter ?? string.Empty;
        if (string.Equals(_filter, nextFilter, StringComparison.Ordinal)) return;

        CancelDrag();
        _filter = nextFilter;
        RebuildRows();
    }

    /// <summary>Rebuilds row content from the caller-owned list and current filter.</summary>
    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        CancelDrag();
        RebuildRows();
    }

    private void RebuildRows()
    {
        if (_selectedItem != null
            && TaskManagerReorderListLogic.IndexOfReference(_readOnlyItems, _selectedItem) < 0)
        {
            _selectedItem = null;
        }

        List<TItem> visibleItems = TaskManagerReorderListLogic.FilterItems(
            _items,
            _filter,
            _getSearchText);
        List<ReorderRow> visibleRows = new(visibleItems.Count);
        TaskManagerReorderResources resources = TaskManagerReorderResources.Current;

        _rows.Children.Clear();
        for (int visibleIndex = 0; visibleIndex < visibleItems.Count; visibleIndex++)
        {
            TItem item = visibleItems[visibleIndex];
            bool isLast = visibleIndex == visibleItems.Count - 1;
            ReorderRow row = BuildRow(item, visibleIndex, visibleItems.Count, isLast, resources);
            visibleRows.Add(row);
            _rows.Children.Add(row.Slot);
        }

        _visibleItems = visibleItems;
        _visibleRows = visibleRows;
    }

    private ReorderRow BuildRow(
        TItem item,
        int visibleIndex,
        int visibleCount,
        bool isLast,
        TaskManagerReorderResources resources)
    {
        Control primaryContent = _buildPrimaryContent(item)
            ?? throw new InvalidOperationException("The reorder primary-content factory returned null.");
        Border primaryHost = new()
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = resources.PrimaryContentMargin,
            Child = primaryContent
        };

        SettingsButton upButton = BuildMoveButton(
            TaskManagerGlyphCatalog.CHEVRON_UP_BIG,
            MoveUpToolTip,
            visibleIndex > 0,
            resources);
        SettingsButton downButton = BuildMoveButton(
            TaskManagerGlyphCatalog.CHEVRON_DOWN_BIG,
            MoveDownToolTip,
            visibleIndex < visibleCount - 1,
            resources);
        upButton.Click += (_, _) => MoveItem(item, -1);
        downButton.Click += (_, _) => MoveItem(item, 1);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = resources.ButtonSpacing,
            Children = { upButton, downButton }
        };

        Grid rowContent = new()
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = resources.RowMinHeight,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        rowContent.Children.Add(primaryHost);
        Grid.SetColumn(buttons, 1);
        rowContent.Children.Add(buttons);

        Border highlightSurface = new()
        {
            Background = Brushes.Transparent,
            CornerRadius = _enableRoundedCorners ? resources.RowCornerRadius : default,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = resources.RowHighlightPadding,
            Child = rowContent
        };

        Border slot = new()
        {
            Background = Brushes.Transparent,
            Cursor = TrayAppDotNETCursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = isLast ? default : resources.RowHitSlotPadding,
            Child = highlightSurface
        };
        ReorderRow row = new(item, slot, highlightSurface);
        slot.PointerEntered += (_, _) => OnRowPointerEntered(row);
        slot.PointerExited += (_, _) => OnRowPointerExited(row);
        slot.AddHandler(
            InputElement.PointerPressedEvent,
            (_, eventArgs) => OnRowPointerPressed(item, slot, eventArgs),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        slot.PointerMoved += (_, eventArgs) => OnRowPointerMoved(slot, eventArgs);
        slot.PointerReleased += (_, eventArgs) => OnRowPointerReleased(slot, eventArgs);
        slot.PointerCaptureLost += (_, eventArgs) => OnRowPointerCaptureLost(slot, eventArgs);
        UpdateRowHighlight(row);
        return row;
    }

    private SettingsButton BuildMoveButton(
        Glyph glyph,
        string toolTip,
        bool isEnabled,
        TaskManagerReorderResources resources)
    {
        SettingsButton button = new(glyph, _palette)
        {
            Width = resources.ButtonSize,
            Height = resources.ButtonSize,
            MinHeight = resources.ButtonSize,
            Padding = resources.ButtonPadding,
            IsEnabled = isEnabled
        };
        button.Label.FontSize = resources.ButtonGlyphFontSize;
        TrayAppDotNETToolTip.SetTip(button, toolTip);
        TrayAppDotNETToolTip.SuppressWhileEngaged(button);
        return button;
    }

    private void MoveItem(TItem item, int direction)
    {
        if (Volatile.Read(ref _disposed) != 0 || direction == 0) return;

        SelectItem(item);
        int sourceVisibleIndex = TaskManagerReorderListLogic.IndexOfReference(_visibleItems, item);
        if (sourceVisibleIndex < 0) return;

        int targetVisibleIndex = sourceVisibleIndex + Math.Sign(direction);
        if (!TaskManagerReorderListLogic.MoveVisibleItem(
                _items,
                _visibleItems,
                item,
                targetVisibleIndex))
        {
            return;
        }

        RebuildRows();
        ItemsChanged?.Invoke();
    }

    private void OnRowPointerEntered(ReorderRow row)
    {
        row.IsPointerOver = true;
        UpdateRowHighlight(row);
    }

    private void OnRowPointerExited(ReorderRow row)
    {
        row.IsPointerOver = false;
        UpdateRowHighlight(row);
    }

    private void SelectItem(TItem item)
    {
        if (ReferenceEquals(_selectedItem, item)) return;

        _selectedItem = item;
        foreach (ReorderRow row in _visibleRows)
            UpdateRowHighlight(row);
    }

    private void UpdateRowHighlight(ReorderRow row)
    {
        Color background = ReferenceEquals(row.Item, _selectedItem)
            ? _palette.SearchListItemSelected
            : row.IsPointerOver
                ? _palette.SearchListItemHover
                : Colors.Transparent;
        row.HighlightSurface.Background = TrayAppDotNETSettingsUI.Brush(background);
    }

    private void OnRowPointerPressed(
        TItem item,
        Border slot,
        PointerPressedEventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (!eventArgs.GetCurrentPoint(slot).Properties.IsLeftButtonPressed) return;
        SelectItem(item);
        if (IsInteractiveDescendant(eventArgs.Source as Visual, slot)) return;
        if (_capturedPointer != null)
        {
            eventArgs.Handled = true;
            return;
        }

        SynchronizeViewportOffset();
        _capturedPointer = eventArgs.Pointer;
        _draggedItem = item;
        _draggedSlot = slot;
        _dragStart = eventArgs.GetPosition(_rows);
        _lastDragPointerPosition = _dragStart;
        _dragPointerOffsetY = eventArgs.GetPosition(slot).Y;
        _draggedSlotHeight = Math.Max(1, slot.Bounds.Height);
        _dragMidpoints = SnapshotRowMidpoints();
        _dropInsertionIndex = CalculateInsertionIndex(
            _dragMidpoints,
            DraggedRowMidpoint(_dragStart.Y));
        _publishedPreviewTargetIndex = TaskManagerReorderListLogic.IndexOfReference(
            _visibleItems,
            item);
        _hasPublishedOrderPreview = false;
        try
        {
            eventArgs.Pointer.Capture(slot);
        }
        catch
        {
            ClearDragState();
            throw;
        }

        eventArgs.Handled = true;
    }

    private void OnRowPointerMoved(Border slot, PointerEventArgs eventArgs)
    {
        if (!ReferenceEquals(_capturedPointer, eventArgs.Pointer)) return;
        if (!ReferenceEquals(_draggedSlot, slot)) return;

        Point current = eventArgs.GetPosition(_rows);
        if (!_isDragging)
        {
            double horizontalDistance = current.X - _dragStart.X;
            double verticalDistance = current.Y - _dragStart.Y;
            double dragThreshold = TaskManagerReorderResources.Current.DragThreshold;
            if (horizontalDistance * horizontalDistance + verticalDistance * verticalDistance
                < dragThreshold * dragThreshold)
            {
                return;
            }

            _isDragging = true;
            ApplyDraggingVisual(slot, true);
        }

        UpdateDraggedRowPosition(current);
        UpdateAutoScrollDirection(eventArgs);
        eventArgs.Handled = true;
    }

    private void OnRowPointerReleased(Border slot, PointerReleasedEventArgs eventArgs)
    {
        if (!ReferenceEquals(_capturedPointer, eventArgs.Pointer)) return;
        if (!ReferenceEquals(_draggedSlot, slot)) return;
        if (eventArgs.InitialPressMouseButton != MouseButton.Left) return;

        eventArgs.Handled = true;
        Point releasePosition = eventArgs.GetPosition(slot);
        bool activateOnClick = new Rect(slot.Bounds.Size).Contains(releasePosition);
        CompleteDrag(
            eventArgs.Pointer,
            releaseCapture: true,
            commit: true,
            activateOnClick);
    }

    private void OnRowPointerCaptureLost(Border slot, PointerCaptureLostEventArgs eventArgs)
    {
        if (_isResettingCapture) return;
        if (!ReferenceEquals(_capturedPointer, eventArgs.Pointer)) return;
        if (!ReferenceEquals(_draggedSlot, slot)) return;

        CompleteDrag(
            eventArgs.Pointer,
            releaseCapture: false,
            commit: false,
            activateOnClick: false);
    }

    private void CompleteDrag(
        IPointer pointer,
        bool releaseCapture,
        bool commit,
        bool activateOnClick)
    {
        TItem? draggedItem = _draggedItem;
        bool hadActiveDrag = _isDragging;
        bool hadPublishedOrderPreview = _hasPublishedOrderPreview;
        int insertionIndex = _dropInsertionIndex;
        List<TItem> visibleItems = _visibleItems;
        ResetDraggedSlotVisual();
        ClearDragState();

        if (releaseCapture)
        {
            _isResettingCapture = true;
            try
            {
                pointer.Capture(null);
            }
            finally
            {
                _isResettingCapture = false;
            }
        }

        if (!commit || draggedItem == null)
        {
            if (hadPublishedOrderPreview) PublishCurrentOrder();
            return;
        }
        if (!hadActiveDrag)
        {
            if (activateOnClick)
            {
                _activateItem?.Invoke(draggedItem);
                if (_activateItem != null)
                {
                    RebuildRows();
                    ItemsChanged?.Invoke();
                }
            }
            return;
        }

        int sourceVisibleIndex = TaskManagerReorderListLogic.IndexOfReference(visibleItems, draggedItem);
        if (sourceVisibleIndex < 0)
        {
            if (hadPublishedOrderPreview) PublishCurrentOrder();
            return;
        }

        int targetVisibleIndex = TaskManagerReorderListLogic.ResolveDropTargetIndex(
            sourceVisibleIndex,
            insertionIndex,
            visibleItems.Count);
        if (!TaskManagerReorderListLogic.MoveVisibleItem(
                _items,
                visibleItems,
                draggedItem,
                targetVisibleIndex))
        {
            if (hadPublishedOrderPreview) PublishCurrentOrder();
            return;
        }

        RebuildRows();
        ItemsChanged?.Invoke();
    }

    private double[] SnapshotRowMidpoints()
    {
        double[] midpoints = new double[_visibleRows.Count];
        double fallbackTop = 0;
        for (int rowIndex = 0; rowIndex < _visibleRows.Count; rowIndex++)
        {
            Border slot = _visibleRows[rowIndex].Slot;
            Point? topLeft = slot.TranslatePoint(default, _rows);
            double top = topLeft?.Y ?? fallbackTop;
            double height = Math.Max(1, slot.Bounds.Height);
            midpoints[rowIndex] = top + height / 2;
            fallbackTop = top + height;
        }

        return midpoints;
    }

    private static int CalculateInsertionIndex(IReadOnlyList<double> midpoints, double pointerY)
    {
        int insertionIndex = 0;
        while (insertionIndex < midpoints.Count && pointerY >= midpoints[insertionIndex])
            insertionIndex++;
        return insertionIndex;
    }

    private double DraggedRowMidpoint(double pointerY) =>
        pointerY - _dragPointerOffsetY + _draggedSlotHeight / 2;

    private void UpdateDraggedRowPosition(Point pointerPosition)
    {
        Border? draggedSlot = _draggedSlot;
        if (draggedSlot == null) return;

        _lastDragPointerPosition = pointerPosition;
        _dropInsertionIndex = CalculateInsertionIndex(
            _dragMidpoints,
            DraggedRowMidpoint(pointerPosition.Y));
        ApplyDragPreview();
        PublishDragPreview();
        draggedSlot.RenderTransform = new TranslateTransform(
            0,
            pointerPosition.Y - _dragStart.Y);
    }

    private void PublishDragPreview()
    {
        TItem? draggedItem = _draggedItem;
        if (!_isDragging || draggedItem == null) return;

        int sourceVisibleIndex = TaskManagerReorderListLogic.IndexOfReference(
            _visibleItems,
            draggedItem);
        if (sourceVisibleIndex < 0) return;

        int targetVisibleIndex = TaskManagerReorderListLogic.ResolveDropTargetIndex(
            sourceVisibleIndex,
            _dropInsertionIndex,
            _visibleItems.Count);
        if (targetVisibleIndex == _publishedPreviewTargetIndex) return;

        _publishedPreviewTargetIndex = targetVisibleIndex;
        if (targetVisibleIndex == sourceVisibleIndex)
        {
            bool restoreCurrentOrder = _hasPublishedOrderPreview;
            _hasPublishedOrderPreview = false;
            if (restoreCurrentOrder) PublishCurrentOrder();
            return;
        }

        List<TItem> previewItems = [.. _items];
        if (!TaskManagerReorderListLogic.MoveVisibleItem(
                previewItems,
                _visibleItems,
                draggedItem,
                targetVisibleIndex))
        {
            return;
        }

        _hasPublishedOrderPreview = true;
        OrderPreviewChanged?.Invoke(previewItems);
    }

    private void PublishCurrentOrder() => OrderPreviewChanged?.Invoke([.. _items]);

    private void ApplyDragPreview()
    {
        TItem? draggedItem = _draggedItem;
        Border? draggedSlot = _draggedSlot;
        if (!_isDragging || draggedItem == null || draggedSlot == null) return;

        int sourceVisibleIndex = TaskManagerReorderListLogic.IndexOfReference(
            _visibleItems,
            draggedItem);
        if (sourceVisibleIndex < 0) return;

        int targetVisibleIndex = TaskManagerReorderListLogic.ResolveDropTargetIndex(
            sourceVisibleIndex,
            _dropInsertionIndex,
            _visibleRows.Count);
        double displacement = Math.Max(1, draggedSlot.Bounds.Height);
        for (int rowIndex = 0; rowIndex < _visibleRows.Count; rowIndex++)
        {
            Border slot = _visibleRows[rowIndex].Slot;
            if (ReferenceEquals(slot, draggedSlot)) continue;

            double offset = TaskManagerReorderListLogic.ResolvePreviewOffset(
                rowIndex,
                sourceVisibleIndex,
                targetVisibleIndex,
                displacement);
            slot.RenderTransform = offset.Equals(0)
                ? null
                : new TranslateTransform(0, offset);
        }
    }

    private void UpdateAutoScrollDirection(PointerEventArgs eventArgs)
    {
        SettingsVerticalScrollViewport? scrollViewport = _scrollViewport;
        if (!_isDragging || scrollViewport == null)
        {
            SetAutoScrollDirection(0);
            return;
        }

        Point pointerPosition = eventArgs.GetPosition(scrollViewport);
        double viewportHeight = scrollViewport.Bounds.Height;
        double edgeSize = Math.Min(
            TaskManagerReorderResources.Current.AutoScrollEdgeSize,
            viewportHeight / 2);
        int direction = pointerPosition.Y < edgeSize
            ? -1
            : pointerPosition.Y > viewportHeight - edgeSize
                ? 1
                : 0;
        SetAutoScrollDirection(direction);
    }

    private void SetAutoScrollDirection(int direction)
    {
        int normalizedDirection = Math.Sign(direction);
        if (_autoScrollDirection == normalizedDirection) return;

        _autoScrollDirection = normalizedDirection;
        if (_autoScrollDirection == 0)
            _autoScrollTimer.Stop();
        else
            _autoScrollTimer.Start();
    }

    private void OnAutoScrollTick(object? sender, EventArgs eventArgs)
    {
        SettingsVerticalScrollViewport? scrollViewport = _scrollViewport;
        if (!_isDragging || scrollViewport == null || _autoScrollDirection == 0)
        {
            SetAutoScrollDirection(0);
            return;
        }

        double previousOffset = scrollViewport.VerticalOffset;
        double requestedOffset = previousOffset
                                 + _autoScrollDirection
                                 * TaskManagerReorderResources.Current.AutoScrollStep;
        scrollViewport.SetVerticalOffset(requestedOffset);
        SynchronizeViewportOffset();
        if (scrollViewport.VerticalOffset.Equals(previousOffset))
        {
            SetAutoScrollDirection(0);
        }
    }

    private void OnScrollViewportVerticalOffsetChanged(object? sender, EventArgs eventArgs) =>
        SynchronizeViewportOffset();

    private void SynchronizeViewportOffset()
    {
        SettingsVerticalScrollViewport? scrollViewport = _scrollViewport;
        if (scrollViewport == null) return;

        double verticalOffset = scrollViewport.VerticalOffset;
        double offsetChange = verticalOffset - _lastViewportOffset;
        _lastViewportOffset = verticalOffset;
        if (!_isDragging || offsetChange.Equals(0)) return;

        UpdateDraggedRowPosition(new Point(
            _lastDragPointerPosition.X,
            _lastDragPointerPosition.Y + offsetChange));
    }

    private static bool IsInteractiveDescendant(Visual? source, Border slot)
    {
        Visual? current = source;
        while (current != null && !ReferenceEquals(current, slot))
        {
            if (current is Control { Focusable: true }) return true;
            current = current.GetVisualParent();
        }

        return false;
    }

    private static void ApplyDraggingVisual(Border slot, bool isDragging)
    {
        TaskManagerReorderResources resources = TaskManagerReorderResources.Current;
        slot.Opacity = isDragging ? resources.DraggingOpacity : 1;
        slot.SetValue(ZIndexProperty, isDragging ? resources.DraggingZIndex : resources.NormalZIndex);
    }

    private void ResetDraggedSlotVisual()
    {
        Border? draggedSlot = _draggedSlot;
        if (draggedSlot == null) return;

        foreach (ReorderRow row in _visibleRows)
        {
            if (!ReferenceEquals(row.Slot, draggedSlot))
                row.Slot.RenderTransform = null;
        }
        draggedSlot.RenderTransform = null;
        ApplyDraggingVisual(draggedSlot, false);
    }

    private void CancelDrag()
    {
        IPointer? pointer = _capturedPointer;
        bool restorePublishedOrderPreview = _hasPublishedOrderPreview;
        ResetDraggedSlotVisual();
        ClearDragState();
        if (pointer != null)
        {
            _isResettingCapture = true;
            try
            {
                pointer.Capture(null);
            }
            finally
            {
                _isResettingCapture = false;
            }
        }

        if (restorePublishedOrderPreview) PublishCurrentOrder();
    }

    private void ClearDragState()
    {
        SetAutoScrollDirection(0);
        _capturedPointer = null;
        _draggedItem = null;
        _draggedSlot = null;
        _dragPointerOffsetY = 0;
        _draggedSlotHeight = 0;
        _lastDragPointerPosition = default;
        _dragMidpoints = [];
        _dropInsertionIndex = -1;
        _publishedPreviewTargetIndex = -1;
        _isDragging = false;
        _hasPublishedOrderPreview = false;
    }

    /// <summary>Cancels an active drag and restores any externally published preview.</summary>
    public void CancelActiveDrag()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        CancelDrag();
    }

    private void OnResourcesReloaded()
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        _autoScrollTimer.Interval = TimeSpan.FromMilliseconds(
            TaskManagerReorderResources.Current.AutoScrollIntervalMilliseconds);
        Refresh();
    }

    private void OnGlyphResourcesReloaded()
    {
        if (Volatile.Read(ref _disposed) == 0) Refresh();
    }

    /// <summary>Releases pointer capture, generated rows, and hot-reload subscriptions.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        TaskManagerReorderResources.ResourcesReloaded -= OnResourcesReloaded;
        GlyphCatalogHotReload.ResourcesReloaded -= OnGlyphResourcesReloaded;
        _autoScrollTimer.Stop();
        _autoScrollTimer.Tick -= OnAutoScrollTick;
        CancelDrag();
        if (_scrollViewport != null)
            _scrollViewport.VerticalOffsetChanged -= OnScrollViewportVerticalOffsetChanged;
        _scrollViewport = null;
        _rows.Children.Clear();
        Children.Clear();
        _visibleItems.Clear();
        _visibleRows.Clear();
        ItemsChanged = null;
        OrderPreviewChanged = null;
    }

    private sealed class ReorderRow(TItem item, Border slot, Border highlightSurface)
    {
        public TItem Item { get; } = item;
        public Border Slot { get; } = slot;
        public Border HighlightSurface { get; } = highlightSurface;
        public bool IsPointerOver { get; set; }
    }

    private sealed class ReadOnlyListView(IList<TItem> items) : IReadOnlyList<TItem>
    {
        public int Count => items.Count;

        public TItem this[int index] => items[index];

        public IEnumerator<TItem> GetEnumerator() => items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>Pure ordering and filtering operations used by Task Manager reorder lists.</summary>
internal static class TaskManagerReorderListLogic
{
    /// <summary>Filters items without score-sorting them, preserving their current order.</summary>
    internal static List<TItem> FilterItems<TItem>(
        IEnumerable<TItem> items,
        string? filter,
        Func<TItem, string> getSearchText)
        where TItem : class
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(getSearchText);

        List<TItem> visibleItems = [];
        foreach (TItem item in items)
        {
            SearchMatch match = SearchMatcher.Score(getSearchText(item), filter);
            if (match.IsMatch) visibleItems.Add(item);
        }

        return visibleItems;
    }

    /// <summary>Moves one visible item while leaving every unmatched full-list slot untouched.</summary>
    internal static bool MoveVisibleItem<TItem>(
        IList<TItem> items,
        IReadOnlyList<TItem> visibleItems,
        TItem item,
        int targetVisibleIndex)
        where TItem : class
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(visibleItems);
        ArgumentNullException.ThrowIfNull(item);
        if (items.IsReadOnly || visibleItems.Count < 2) return false;

        int sourceVisibleIndex = IndexOfReference(visibleItems, item);
        if (sourceVisibleIndex < 0) return false;

        int clampedTargetIndex = Math.Clamp(targetVisibleIndex, 0, visibleItems.Count - 1);
        if (clampedTargetIndex == sourceVisibleIndex) return false;

        HashSet<TItem> visibleSet = new(ReferenceEqualityComparer.Instance);
        for (int visibleIndex = 0; visibleIndex < visibleItems.Count; visibleIndex++)
        {
            if (!visibleSet.Add(visibleItems[visibleIndex])) return false;
        }

        List<int> visibleSlots = new(visibleItems.Count);
        for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            if (visibleSet.Contains(items[itemIndex])) visibleSlots.Add(itemIndex);
        }

        if (visibleSlots.Count != visibleItems.Count) return false;
        for (int visibleIndex = 0; visibleIndex < visibleItems.Count; visibleIndex++)
        {
            if (!ReferenceEquals(items[visibleSlots[visibleIndex]], visibleItems[visibleIndex]))
                return false;
        }

        List<TItem> reordered = new(visibleItems.Count);
        for (int visibleIndex = 0; visibleIndex < visibleItems.Count; visibleIndex++)
            reordered.Add(visibleItems[visibleIndex]);
        TItem moved = reordered[sourceVisibleIndex];
        reordered.RemoveAt(sourceVisibleIndex);
        reordered.Insert(clampedTargetIndex, moved);

        for (int visibleIndex = 0; visibleIndex < visibleSlots.Count; visibleIndex++)
            items[visibleSlots[visibleIndex]] = reordered[visibleIndex];
        return true;
    }

    /// <summary>Maps a between-row drop insertion to a final visible item index.</summary>
    internal static int ResolveDropTargetIndex(
        int sourceVisibleIndex,
        int insertionIndex,
        int visibleCount)
    {
        if (visibleCount <= 0) return -1;

        int clampedSourceIndex = Math.Clamp(sourceVisibleIndex, 0, visibleCount - 1);
        int clampedInsertionIndex = Math.Clamp(insertionIndex, 0, visibleCount);
        int targetVisibleIndex = clampedInsertionIndex > clampedSourceIndex
            ? clampedInsertionIndex - 1
            : clampedInsertionIndex;
        return Math.Clamp(targetVisibleIndex, 0, visibleCount - 1);
    }

    /// <summary>Resolves one sibling row's vertical displacement for a reorder preview.</summary>
    internal static double ResolvePreviewOffset(
        int rowIndex,
        int sourceIndex,
        int targetIndex,
        double displacement)
    {
        if (displacement <= 0 || rowIndex == sourceIndex || sourceIndex == targetIndex) return 0;
        if (targetIndex < sourceIndex)
            return rowIndex >= targetIndex && rowIndex < sourceIndex ? displacement : 0;

        return rowIndex > sourceIndex && rowIndex <= targetIndex ? -displacement : 0;
    }

    /// <summary>Finds an item by object identity rather than value equality.</summary>
    internal static int IndexOfReference<TItem>(IReadOnlyList<TItem> items, TItem item)
        where TItem : class
    {
        for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            if (ReferenceEquals(items[itemIndex], item)) return itemIndex;
        }

        return -1;
    }
}
