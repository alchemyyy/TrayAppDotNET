using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using TrayAppDotNETCommon.Services;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Coordinates input, viewport state, and coalesced zoom rendering for details grids.</summary>
internal abstract class DetailsGridControl : Control, IDisposable
{
    private const double MetricEqualityTolerance = 0.01;
    private const double ZoomRebuildBatchBudgetMilliseconds = 1.0;
    private const int MaximumZoomRowsPerBatch = 8;

    private enum ZoomWorkKind : byte
    {
        VisibleRows,
        Settle
    }

    private readonly AsyncThrottler<ZoomWorkKind> _zoomThrottler = new(cooldownMs: 0);
    private Rect _effectiveViewport;
    private int _zoomRequestVersion;
    private bool _isZoomActive;
    private volatile bool _disposed;

    protected DetailsGridControl()
    {
        EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    public event Action<double, double>? GridMetricsChanged;
    public event Action<int>? GridZoomRequested;
    public event Action? GridZoomResetRequested;
    public event Action<int>? GridRowSpacingRequested;
    public event Action? GridRowSpacingResetRequested;

    protected bool IsDetailsGridDisposed => _disposed;
    protected bool IsDetailsGridZoomActive => _isZoomActive;

    protected abstract int DetailsGridRowCount { get; }
    protected abstract double DetailsGridHeaderHeight { get; }
    protected abstract double DetailsGridRowHeight { get; }
    protected abstract double DetailsGridFontSize { get; }
    protected abstract double DetailsGridDefaultViewportHeight { get; }
    protected virtual bool CanResetDetailsGridZoom => true;

    /// <summary>Applies font and row geometry before concrete-grid visual updates run.</summary>
    protected abstract void ApplyDetailsGridMetrics(double fontSize, double rowHeight);

    /// <summary>Runs concrete-grid visual updates after font and row geometry change.</summary>
    protected virtual void OnDetailsGridMetricsChanged()
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
        if (Math.Abs(DetailsGridFontSize - fontSize) < MetricEqualityTolerance
            && Math.Abs(DetailsGridRowHeight - rowHeight) < MetricEqualityTolerance)
        {
            return;
        }

        ApplyDetailsGridMetrics(fontSize, rowHeight);
        _isZoomActive = true;
        NotifyDetailsGridMetricsChanged(fontSize, rowHeight);
        OnDetailsGridMetricsChanged();
        QueueDetailsGridZoomWork();
    }

    /// <summary>Rebuilds one row for the current metrics and reports whether its drawing changed.</summary>
    protected abstract bool RebuildDetailsGridZoomRow(int rowIndex);

    /// <summary>Commits the retained row range after zoom work settles.</summary>
    protected abstract void CommitDetailsGridRetainedRange(int firstRow, int lastRowExclusive);

    /// <summary>Invalidates the concrete grid's row render layers.</summary>
    protected abstract void InvalidateDetailsGridRows();

    /// <summary>Runs concrete-grid work after normal retained rendering resumes.</summary>
    protected virtual void OnDetailsGridZoomCompleted()
    {
    }

    /// <summary>Runs concrete-grid work after the effective viewport changes.</summary>
    protected virtual void OnDetailsGridViewportChanged()
    {
    }

    /// <summary>Releases resources owned by the concrete grid.</summary>
    protected virtual void DisposeDetailsGridResources()
    {
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs eventArgs)
    {
        base.OnPointerWheelChanged(eventArgs);
        if (_disposed || eventArgs.Handled || eventArgs.Delta.Y == 0) return;

        int direction = eventArgs.Delta.Y > 0 ? 1 : -1;
        bool resetRequested = eventArgs.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (resetRequested)
            {
                if (!CanResetDetailsGridZoom) return;
                GridRowSpacingResetRequested?.Invoke();
            }
            else
            {
                GridRowSpacingRequested?.Invoke(direction);
            }
        }
        else if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (resetRequested)
            {
                if (!CanResetDetailsGridZoom) return;
                GridZoomResetRequested?.Invoke();
            }
            else
            {
                GridZoomRequested?.Invoke(direction);
            }
        }
        else
        {
            return;
        }
        eventArgs.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs eventArgs)
    {
        base.OnPointerPressed(eventArgs);
        if (_disposed || eventArgs.Handled || !CanResetDetailsGridZoom) return;

        PointerPoint pointerPoint = eventArgs.GetCurrentPoint(this);
        if (!pointerPoint.Properties.IsMiddleButtonPressed) return;

        if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift))
            GridRowSpacingResetRequested?.Invoke();
        else if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control))
            GridZoomResetRequested?.Invoke();
        else
            return;
        eventArgs.Handled = true;
    }

    /// <summary>Publishes applied font and row geometry to the owning page.</summary>
    protected void NotifyDetailsGridMetricsChanged(double fontSize, double rowHeight) =>
        GridMetricsChanged?.Invoke(fontSize, rowHeight);

    /// <summary>Requeues active zoom work after rows or viewport geometry change.</summary>
    protected void QueueDetailsGridZoomWork()
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
    protected Rect ResolveDetailsGridViewport()
    {
        if (_effectiveViewport.Width > 0 && _effectiveViewport.Height > 0)
        {
            double left = Math.Clamp(_effectiveViewport.X, 0, Bounds.Width);
            double right = Math.Clamp(_effectiveViewport.Right, left, Bounds.Width);
            double top = Math.Clamp(_effectiveViewport.Y, 0, Bounds.Height);
            double bottom = Math.Clamp(_effectiveViewport.Bottom, top, Bounds.Height);
            return new Rect(left, top, right - left, bottom - top);
        }

        return new Rect(
            0,
            0,
            Bounds.Width,
            Math.Min(Bounds.Height, DetailsGridDefaultViewportHeight));
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
               < TimeConstants.DetailsGridZoomSettleDelayMilliseconds)
        {
            if (ShouldDropZoomWork(requestVersion, context)) return;

            double elapsedMilliseconds = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            int remainingMilliseconds = Math.Max(
                1,
                (int)Math.Ceiling(
                    TimeConstants.DetailsGridZoomSettleDelayMilliseconds - elapsedMilliseconds));
            int delayMilliseconds = Math.Min(
                remainingMilliseconds,
                TimeConstants.DetailsGridZoomReplacementPollMilliseconds);
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
            {
                break;
            }
        }

        if (paintedRowsChanged && !ShouldDropZoomWork(requestVersion, context))
            InvalidateDetailsGridRows();

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
            DetailsGridZoomRowRange rowRange = ResolveZoomRowRange(workState.IncludeRetainedOverscan);
            workState.NextRow = rowRange.FirstRow;
            workState.LastRowExclusive = rowRange.LastRowExclusive;
            workState.PaintedFirstRow = rowRange.PaintedFirstRow;
            workState.PaintedLastRowExclusive = rowRange.PaintedLastRowExclusive;
            workState.IsInitialized = true;
        }

        if (workState.NextRow >= workState.LastRowExclusive) return false;

        int rowIndex = workState.NextRow;
        workState.NextRow++;
        bool rebuiltDrawing = RebuildDetailsGridZoomRow(rowIndex);
        paintedRowChanged = rebuiltDrawing
                            && rowIndex >= workState.PaintedFirstRow
                            && rowIndex < workState.PaintedLastRowExclusive;
        return true;
    }

    private DetailsGridZoomRowRange ResolveZoomRowRange(bool includeRetainedOverscan)
    {
        Rect viewport = ResolveDetailsGridViewport();
        int rowCount = DetailsGridRowCount;
        double headerHeight = DetailsGridHeaderHeight;
        double rowHeight = DetailsGridRowHeight;
        DetailsGridLayout.GetVisibleRowRange(
            viewport,
            rowCount,
            headerHeight,
            rowHeight,
            out int paintedFirstRow,
            out int paintedLastRowExclusive);
        if (!includeRetainedOverscan)
        {
            return new DetailsGridZoomRowRange(
                paintedFirstRow,
                paintedLastRowExclusive,
                paintedFirstRow,
                paintedLastRowExclusive);
        }

        DetailsGridLayout.GetRetainedRowRange(
            viewport,
            rowCount,
            headerHeight,
            rowHeight,
            out int firstRow,
            out int lastRowExclusive);
        return new DetailsGridZoomRowRange(
            firstRow,
            lastRowExclusive,
            paintedFirstRow,
            paintedLastRowExclusive);
    }

    private void CompleteZoom(int requestVersion, ThrottlerContext context)
    {
        if (ShouldDropZoomWork(requestVersion, context) || !_isZoomActive) return;

        DetailsGridZoomRowRange rowRange = ResolveZoomRowRange(includeRetainedOverscan: true);
        CommitDetailsGridRetainedRange(rowRange.FirstRow, rowRange.LastRowExclusive);
        _isZoomActive = false;
        OnDetailsGridZoomCompleted();
        InvalidateDetailsGridRows();
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
        OnDetailsGridViewportChanged();
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
        GridRowSpacingRequested = null;
        GridRowSpacingResetRequested = null;
        DisposeDetailsGridResources();
        GC.SuppressFinalize(this);
    }

    private readonly record struct DetailsGridZoomRowRange(
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
