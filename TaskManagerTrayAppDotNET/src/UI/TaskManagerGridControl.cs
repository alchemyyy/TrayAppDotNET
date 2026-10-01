using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Coordinates input, viewport state, and coalesced zoom rendering for Task Manager grids.</summary>
internal abstract class TaskManagerGridControl : Control, IDisposable
{
    private const double MetricEqualityTolerance = 0.01;
    private const double ZoomRebuildBatchBudgetMilliseconds = 1.0;
    private const int MaximumZoomRowsPerBatch = 8;

    private enum ZoomWorkKind : byte
    {
        VisibleRows,
        Settle
    }

    private readonly AsyncThrottler<ZoomWorkKind> _zoomThrottler = new(0);
    private Rect _effectiveViewport;
    private int _zoomRequestVersion;
    private bool _isZoomActive;
    private volatile bool _disposed;

    protected TaskManagerGridControl() => EffectiveViewportChanged += OnEffectiveViewportChanged;

    public event Action<double, double>? GridMetricsChanged;
    public event Action<int>? GridZoomRequested;
    public event Action? GridZoomResetRequested;
    public event Action? GridZoomBaselineRequested;
    public event Action<int>? GridRowSpacingRequested;
    public event Action? GridRowSpacingResetRequested;
    public event Action? GridRowSpacingBaselineRequested;

    protected bool IsTaskManagerGridDisposed => _disposed;
    protected bool IsTaskManagerGridZoomActive => _isZoomActive;

    protected abstract int TaskManagerGridRowCount { get; }
    protected abstract double TaskManagerGridHeaderHeight { get; }
    protected abstract double TaskManagerGridRowHeight { get; }
    protected abstract double TaskManagerGridFontSize { get; }
    protected abstract double TaskManagerGridDefaultViewportHeight { get; }
    protected virtual bool CanResetTaskManagerGridZoom => true;

    /// <summary>Applies font and row geometry before concrete-grid visual updates run.</summary>
    protected abstract void ApplyTaskManagerGridMetrics(double fontSize, double rowHeight);

    /// <summary>Runs concrete-grid visual updates after font and row geometry change.</summary>
    protected virtual void OnTaskManagerGridMetricsChanged()
    {
    }

    /// <summary>Applies font and row geometry and starts a coalesced zoom rebuild.</summary>
    public void SetGridMetrics(double fontSize, double rowHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (!double.IsFinite(rowHeight) || rowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowHeight));
        if (Math.Abs(TaskManagerGridFontSize - fontSize) < MetricEqualityTolerance
            && Math.Abs(TaskManagerGridRowHeight - rowHeight) < MetricEqualityTolerance)
            return;

        ApplyTaskManagerGridMetrics(fontSize, rowHeight);
        _isZoomActive = true;
        NotifyTaskManagerGridMetricsChanged(fontSize, rowHeight);
        OnTaskManagerGridMetricsChanged();
        QueueTaskManagerGridZoomWork();
    }

    /// <summary>Rebuilds one row for the current metrics and reports whether its drawing changed.</summary>
    protected abstract bool RebuildTaskManagerGridZoomRow(int rowIndex);

    /// <summary>Commits the retained row range after zoom work settles.</summary>
    protected abstract void CommitTaskManagerGridRetainedRange(int firstRow, int lastRowExclusive);

    /// <summary>Invalidates the concrete grid's row render layers.</summary>
    protected abstract void InvalidateTaskManagerGridRows();

    /// <summary>Runs concrete-grid work after normal retained rendering resumes.</summary>
    protected virtual void OnTaskManagerGridZoomCompleted()
    {
    }

    /// <summary>Runs concrete-grid work after the effective viewport changes.</summary>
    protected virtual void OnTaskManagerGridViewportChanged()
    {
    }

    /// <summary>Releases resources owned by the concrete grid.</summary>
    protected virtual void DisposeTaskManagerGridResources()
    {
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs eventArgs)
    {
        base.OnPointerWheelChanged(eventArgs);
        if (_disposed || eventArgs.Handled || eventArgs.Delta.Y == 0) return;

        TaskManagerGridShortcutAction action = TaskManagerGridShortcuts.Resolve(
            TaskManagerGridShortcutTrigger.MouseWheel,
            eventArgs.KeyModifiers);
        int direction = eventArgs.Delta.Y > 0 ? 1 : -1;
        if (!TryRaiseShortcut(action, direction)) return;

        eventArgs.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs eventArgs)
    {
        base.OnPointerPressed(eventArgs);
        if (_disposed || eventArgs.Handled) return;

        PointerPoint pointerPoint = eventArgs.GetCurrentPoint(this);
        if (!pointerPoint.Properties.IsMiddleButtonPressed) return;

        TaskManagerGridShortcutAction action = TaskManagerGridShortcuts.Resolve(
            TaskManagerGridShortcutTrigger.MiddleClick,
            eventArgs.KeyModifiers);
        if (!TryRaiseShortcut(action, direction: 0)) return;

        eventArgs.Handled = true;
    }

    /// <summary>Raises the grid request for a resolved shortcut and reports whether one was raised.</summary>
    private bool TryRaiseShortcut(TaskManagerGridShortcutAction action, int direction)
    {
        if (TaskManagerGridShortcuts.IsZoomReset(action) && !CanResetTaskManagerGridZoom) return false;

        switch (action)
        {
            case TaskManagerGridShortcutAction.Zoom:
                GridZoomRequested?.Invoke(direction);
                return true;
            case TaskManagerGridShortcutAction.Stretch:
                GridRowSpacingRequested?.Invoke(direction);
                return true;
            case TaskManagerGridShortcutAction.ResetZoom:
                GridZoomResetRequested?.Invoke();
                return true;
            case TaskManagerGridShortcutAction.ResetStretch:
                GridRowSpacingResetRequested?.Invoke();
                return true;
            case TaskManagerGridShortcutAction.SetZoomBaseline:
                GridZoomBaselineRequested?.Invoke();
                return true;
            case TaskManagerGridShortcutAction.SetStretchBaseline:
                GridRowSpacingBaselineRequested?.Invoke();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Publishes applied font and row geometry to the owning page.</summary>
    protected void NotifyTaskManagerGridMetricsChanged(double fontSize, double rowHeight) =>
        GridMetricsChanged?.Invoke(fontSize, rowHeight);

    /// <summary>Requeues active zoom work after rows or viewport geometry change.</summary>
    protected void QueueTaskManagerGridZoomWork()
    {
        if (_disposed || !_isZoomActive) return;

        int requestVersion = Interlocked.Increment(ref _zoomRequestVersion);
        _ = _zoomThrottler.RunAsync(
            ZoomWorkKind.VisibleRows,
            context => RebuildZoomRowsAsync(
                requestVersion,
                includeRetainedOverscan: false,
                context));
        _ = _zoomThrottler.RunAsync(
            ZoomWorkKind.Settle,
            context => SettleZoomAsync(requestVersion, context));
    }

    /// <summary>Returns the viewport clipped to the current grid bounds.</summary>
    protected Rect ResolveTaskManagerGridViewport()
    {
        if (_effectiveViewport is { Width: > 0, Height: > 0 })
        {
            double left = Math.Clamp(_effectiveViewport.X, min: 0, Bounds.Width);
            double right = Math.Clamp(_effectiveViewport.Right, left, Bounds.Width);
            double top = Math.Clamp(_effectiveViewport.Y, min: 0, Bounds.Height);
            double bottom = Math.Clamp(_effectiveViewport.Bottom, top, Bounds.Height);
            return new Rect(left, top, right - left, bottom - top);
        }

        return new Rect(
            x: 0,
            y: 0,
            Bounds.Width,
            Math.Min(Bounds.Height, TaskManagerGridDefaultViewportHeight));
    }

    private async Task RebuildZoomRowsAsync(
        int requestVersion,
        bool includeRetainedOverscan,
        ThrottlerContext context)
    {
        ZoomRowWorkState workState = new(includeRetainedOverscan);
        while (!ShouldDropZoomWork(requestVersion, context))
        {
            bool hasMoreRows = await Dispatcher.UIThread.InvokeAsync(
                () => RebuildZoomRowBatch(requestVersion, workState, context),
                DispatcherPriority.Background);
            if (!hasMoreRows) return;
        }
    }

    private async Task SettleZoomAsync(int requestVersion, ThrottlerContext context)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
               < TimeConstants.TaskManagerGridZoomSettleDelayMilliseconds)
        {
            if (ShouldDropZoomWork(requestVersion, context)) return;

            double elapsedMilliseconds = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            int remainingMilliseconds = Math.Max(
                val1: 1,
                (int)Math.Ceiling(
                    TimeConstants.TaskManagerGridZoomSettleDelayMilliseconds - elapsedMilliseconds));
            int delayMilliseconds = Math.Min(
                remainingMilliseconds,
                TimeConstants.TaskManagerGridZoomReplacementPollMilliseconds);
            await Task.Delay(delayMilliseconds, context.CancellationToken).ConfigureAwait(false);
        }

        if (ShouldDropZoomWork(requestVersion, context)) return;
        await RebuildZoomRowsAsync(
            requestVersion,
            includeRetainedOverscan: true,
            context).ConfigureAwait(false);
        if (ShouldDropZoomWork(requestVersion, context)) return;

        await Dispatcher.UIThread.InvokeAsync(
            () => CompleteZoom(requestVersion, context),
            DispatcherPriority.Background);
    }

    private bool RebuildZoomRowBatch(
        int requestVersion,
        ZoomRowWorkState workState,
        ThrottlerContext context)
    {
        if (ShouldDropZoomWork(requestVersion, context)) return false;

        long startTimestamp = Stopwatch.GetTimestamp();
        int processedRowCount = 0;
        bool paintedRowsChanged = false;
        while (processedRowCount < MaximumZoomRowsPerBatch
               && !ShouldDropZoomWork(requestVersion, context))
        {
            if (!TryRebuildNextZoomRow(workState, out bool paintedRowChanged)) break;

            processedRowCount++;
            paintedRowsChanged |= paintedRowChanged;
            if (Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                >= ZoomRebuildBatchBudgetMilliseconds)
                break;
        }

        if (paintedRowsChanged && !ShouldDropZoomWork(requestVersion, context))
            InvalidateTaskManagerGridRows();

        return !_disposed
               && _isZoomActive
               && !ShouldDropZoomWork(requestVersion, context)
               && workState.NextRow < workState.LastRowExclusive;
    }

    private bool TryRebuildNextZoomRow(
        ZoomRowWorkState workState,
        out bool paintedRowChanged)
    {
        paintedRowChanged = false;
        if (_disposed || !_isZoomActive) return false;

        if (!workState.IsInitialized)
        {
            ZoomRowRange rowRange = ResolveZoomRowRange(workState.IncludeRetainedOverscan);
            workState.NextRow = rowRange.FirstRow;
            workState.LastRowExclusive = rowRange.LastRowExclusive;
            workState.PaintedFirstRow = rowRange.PaintedFirstRow;
            workState.PaintedLastRowExclusive = rowRange.PaintedLastRowExclusive;
            workState.IsInitialized = true;
        }

        if (workState.NextRow >= workState.LastRowExclusive) return false;

        int rowIndex = workState.NextRow;
        workState.NextRow++;
        bool rebuiltDrawing = RebuildTaskManagerGridZoomRow(rowIndex);
        paintedRowChanged = rebuiltDrawing
                            && rowIndex >= workState.PaintedFirstRow
                            && rowIndex < workState.PaintedLastRowExclusive;
        return true;
    }

    private ZoomRowRange ResolveZoomRowRange(bool includeRetainedOverscan)
    {
        Rect viewport = ResolveTaskManagerGridViewport();
        int rowCount = TaskManagerGridRowCount;
        double headerHeight = TaskManagerGridHeaderHeight;
        double rowHeight = TaskManagerGridRowHeight;
        TaskManagerGridLayout.GetVisibleRowRange(
            viewport,
            rowCount,
            headerHeight,
            rowHeight,
            out int paintedFirstRow,
            out int paintedLastRowExclusive);
        if (!includeRetainedOverscan)
        {
            return new ZoomRowRange(
                paintedFirstRow,
                paintedLastRowExclusive,
                paintedFirstRow,
                paintedLastRowExclusive);
        }

        TaskManagerGridLayout.GetRetainedRowRange(
            viewport,
            rowCount,
            headerHeight,
            rowHeight,
            out int firstRow,
            out int lastRowExclusive);
        return new ZoomRowRange(
            firstRow,
            lastRowExclusive,
            paintedFirstRow,
            paintedLastRowExclusive);
    }

    private void CompleteZoom(int requestVersion, ThrottlerContext context)
    {
        if (ShouldDropZoomWork(requestVersion, context) || !_isZoomActive) return;

        ZoomRowRange rowRange = ResolveZoomRowRange(true);
        CommitTaskManagerGridRetainedRange(rowRange.FirstRow, rowRange.LastRowExclusive);
        _isZoomActive = false;
        OnTaskManagerGridZoomCompleted();
        InvalidateTaskManagerGridRows();
    }

    private bool ShouldDropZoomWork(int requestVersion, ThrottlerContext context) =>
        _disposed
        || context.CancellationToken.IsCancellationRequested
        || context.HasReplacement
        || Volatile.Read(ref _zoomRequestVersion) != requestVersion;

    private void OnEffectiveViewportChanged(
        object? sender,
        EffectiveViewportChangedEventArgs eventArgs)
    {
        if (_disposed) return;

        _effectiveViewport = eventArgs.EffectiveViewport;
        OnTaskManagerGridViewportChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _isZoomActive = false;
        Interlocked.Increment(ref _zoomRequestVersion);
        _zoomThrottler.Drop(ZoomWorkKind.VisibleRows);
        _zoomThrottler.Drop(ZoomWorkKind.Settle);
        _zoomThrottler.Dispose();
        EffectiveViewportChanged -= OnEffectiveViewportChanged;
        GridMetricsChanged = null;
        GridZoomRequested = null;
        GridZoomResetRequested = null;
        GridZoomBaselineRequested = null;
        GridRowSpacingRequested = null;
        GridRowSpacingResetRequested = null;
        GridRowSpacingBaselineRequested = null;
        DisposeTaskManagerGridResources();
        GC.SuppressFinalize(this);
    }

    private readonly record struct ZoomRowRange(
        int FirstRow,
        int LastRowExclusive,
        int PaintedFirstRow,
        int PaintedLastRowExclusive);

    private sealed class ZoomRowWorkState(bool includeRetainedOverscan)
    {
        public readonly bool IncludeRetainedOverscan = includeRetainedOverscan;
        public bool IsInitialized;
        public int NextRow;
        public int LastRowExclusive;
        public int PaintedFirstRow;
        public int PaintedLastRowExclusive;
    }
}
