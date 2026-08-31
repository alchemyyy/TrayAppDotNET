using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.Interop;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Hosts one GPU-composited highlight without invalidating retained table drawings.</summary>
internal abstract class ProcessTableHighlightVisual : Control, IDisposable
{
    private readonly Color _color;
    private CompositionSolidColorVisual? _compositionVisual;
    private bool _disposed;

    protected ProcessTableHighlightVisual(Color color)
    {
        _color = color;
        ClipToBounds = true;
        IsHitTestVisible = false;
    }

    protected bool IsDisposed => _disposed;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        if (_disposed) return;

        CompositionVisual? hostVisual = ElementComposition.GetElementVisual(this);
        if (hostVisual == null) return;

        CompositionSolidColorVisual compositionVisual = hostVisual.Compositor.CreateSolidColorVisual();
        compositionVisual.Color = _color;
        compositionVisual.Visible = false;
        _compositionVisual = compositionVisual;
        ElementComposition.SetElementChildVisual(this, compositionVisual);
        ApplyHighlight(Bounds.Size);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        ElementComposition.SetElementChildVisual(this, null);
        _compositionVisual = null;
        base.OnDetachedFromVisualTree(eventArgs);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arrangedSize = base.ArrangeOverride(finalSize);
        ApplyHighlight(arrangedSize);
        return arrangedSize;
    }

    protected void UpdateHighlight()
    {
        if (!_disposed) ApplyHighlight(Bounds.Size);
    }

    protected abstract Rect? ResolveHighlightBounds(Size hostSize);

    private void ApplyHighlight(Size hostSize)
    {
        if (_disposed || _compositionVisual == null) return;

        Rect? highlightBounds = ResolveHighlightBounds(hostSize);
        if (!highlightBounds.HasValue
            || highlightBounds.Value.Width <= 0
            || highlightBounds.Value.Height <= 0)
        {
            _compositionVisual.Visible = false;
            return;
        }

        Rect bounds = highlightBounds.Value;
        _compositionVisual.Offset = new Vector3D(bounds.X, bounds.Y, 0);
        _compositionVisual.Size = new Vector(bounds.Width, bounds.Height);
        _compositionVisual.Visible = true;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        ElementComposition.SetElementChildVisual(this, null);
        _compositionVisual = null;
    }
}

/// <summary>Immutable geometry used to resolve a hovered process row on the render thread.</summary>
internal readonly record struct ProcessRowHoverGeometry(
    Rect Viewport,
    int VisibleRowCount,
    double HeaderHeight,
    double RowHeight,
    double StickyHeaderTop,
    bool IsEnabled)
{
    /// <summary>Maps a table-local point to a visible row while excluding the sticky header.</summary>
    public int HitTest(Point position)
    {
        if (!IsEnabled
            || VisibleRowCount <= 0
            || !double.IsFinite(Viewport.X)
            || !double.IsFinite(Viewport.Y)
            || !double.IsFinite(Viewport.Width)
            || !double.IsFinite(Viewport.Height)
            || !double.IsFinite(HeaderHeight)
            || !double.IsFinite(RowHeight)
            || !double.IsFinite(StickyHeaderTop)
            || HeaderHeight <= 0
            || RowHeight <= 0
            || Viewport.Width <= 0
            || Viewport.Height <= 0
            || !double.IsFinite(position.X)
            || !double.IsFinite(position.Y))
        {
            return -1;
        }

        if (position.X < Viewport.X
            || position.X >= Viewport.Right
            || position.Y < Viewport.Y
            || position.Y >= Viewport.Bottom)
        {
            return -1;
        }

        if (position.Y >= StickyHeaderTop
            && position.Y < StickyHeaderTop + HeaderHeight)
        {
            return -1;
        }

        double rowPosition = position.Y - HeaderHeight;
        if (rowPosition < 0) return -1;

        int visibleIndex = (int)Math.Floor(rowPosition / RowHeight);
        return visibleIndex >= 0 && visibleIndex < VisibleRowCount ? visibleIndex : -1;
    }

    /// <summary>Returns one full-width row rectangle in table-local coordinates.</summary>
    public Rect GetRowBounds(int visibleIndex, double hostWidth)
    {
        if ((uint)visibleIndex >= (uint)VisibleRowCount
            || !double.IsFinite(hostWidth)
            || !double.IsFinite(HeaderHeight)
            || !double.IsFinite(RowHeight)
            || hostWidth <= 0)
        {
            return default;
        }

        return new Rect(
            0,
            HeaderHeight + visibleIndex * RowHeight,
            hostWidth,
            RowHeight);
    }
}

/// <summary>Samples the Win32 cursor and paints the process-row hover on the render thread.</summary>
internal sealed class ProcessRowHoverVisual : Control, IDisposable
{
    private readonly Color _color;
    private ProcessRowHoverGeometry _geometry;
    private CompositionCustomVisual? _compositionVisual;
    private TopLevel? _topLevel;
    private ProcessRowHoverRenderState _lastSentState;
    private bool _hasLastSentState;
    private bool _isSamplingEnabled = true;
    private bool _isHandlerRunning;
    private bool _disposed;

    public ProcessRowHoverVisual(Color color, ProcessRowHoverGeometry geometry)
    {
        _color = color;
        _geometry = geometry;
        ClipToBounds = true;
        IsHitTestVisible = false;
    }

    /// <summary>Sends structural table changes to the render-thread hover sampler.</summary>
    public void SetGeometry(ProcessRowHoverGeometry geometry)
    {
        if (_disposed || _geometry == geometry) return;

        _geometry = geometry;
        SendRenderState();
    }

    /// <summary>Starts or stops cursor sampling for modal interaction boundaries.</summary>
    public void SetSamplingEnabled(bool isEnabled)
    {
        if (_disposed || _isSamplingEnabled == isEnabled) return;

        _isSamplingEnabled = isEnabled;
        UpdateHandlerRunningState();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        if (_disposed || !OperatingSystem.IsWindows()) return;

        CompositionVisual? hostVisual = ElementComposition.GetElementVisual(this);
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        IntPtr windowHandle = topLevel?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hostVisual == null || topLevel == null || windowHandle == IntPtr.Zero) return;

        Point? clientOrigin = this.TranslatePoint(default, topLevel);
        ProcessRowHoverRenderState state = clientOrigin.HasValue
                                             && TryCreateRenderState(clientOrigin.Value, topLevel, out ProcessRowHoverRenderState initialState)
            ? initialState
            : new ProcessRowHoverRenderState(
                _geometry,
                default,
                RenderScaling: 1,
                HasCoordinateMap: false);

        ProcessRowHoverHandler handler = new(_color, windowHandle, state);
        CompositionCustomVisual compositionVisual = hostVisual.Compositor.CreateCustomVisual(handler);
        compositionVisual.Size = new Vector(Bounds.Width, Bounds.Height);
        _compositionVisual = compositionVisual;
        _topLevel = topLevel;
        _lastSentState = state;
        _hasLastSentState = true;
        _topLevel.PropertyChanged += OnTopLevelPropertyChanged;
        _topLevel.ScalingChanged += OnTopLevelScalingChanged;
        ElementComposition.SetElementChildVisual(this, compositionVisual);
        UpdateHandlerRunningState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        DetachCompositionVisual();
        base.OnDetachedFromVisualTree(eventArgs);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arrangedSize = base.ArrangeOverride(finalSize);
        if (_compositionVisual != null)
            _compositionVisual.Size = new Vector(arrangedSize.Width, arrangedSize.Height);
        SendRenderState();

        return arrangedSize;
    }

    private void OnTopLevelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.Property == Visual.IsVisibleProperty
            || eventArgs.Property == Window.WindowStateProperty)
        {
            UpdateHandlerRunningState();
        }
    }

    private void OnTopLevelScalingChanged(object? sender, EventArgs eventArgs) => SendRenderState();

    private void SendRenderState()
    {
        if (_compositionVisual == null || _topLevel == null) return;

        Point? clientOrigin = this.TranslatePoint(default, _topLevel);
        if (!clientOrigin.HasValue
            || !TryCreateRenderState(clientOrigin.Value, _topLevel, out ProcessRowHoverRenderState state)
            || _hasLastSentState && _lastSentState == state)
        {
            return;
        }

        _lastSentState = state;
        _hasLastSentState = true;
        _compositionVisual.SendHandlerMessage(state);
    }

    private bool TryCreateRenderState(
        Point clientOrigin,
        TopLevel topLevel,
        out ProcessRowHoverRenderState state)
    {
        double renderScaling = topLevel.RenderScaling;
        if (!double.IsFinite(clientOrigin.X)
            || !double.IsFinite(clientOrigin.Y)
            || !double.IsFinite(renderScaling)
            || renderScaling <= 0)
        {
            state = default;
            return false;
        }

        state = new ProcessRowHoverRenderState(
            _geometry,
            clientOrigin,
            renderScaling,
            HasCoordinateMap: true);
        return true;
    }

    private void UpdateHandlerRunningState()
    {
        bool shouldRun = !_disposed
                         && _isSamplingEnabled
                         && _compositionVisual != null
                         && _topLevel is { IsVisible: true }
                         && (_topLevel is not Window window || window.WindowState != WindowState.Minimized);
        if (_isHandlerRunning == shouldRun) return;

        _isHandlerRunning = shouldRun;
        _compositionVisual?.SendHandlerMessage(
            shouldRun ? ProcessRowHoverHandler.StartMessage : ProcessRowHoverHandler.StopMessage);
    }

    private void DetachCompositionVisual()
    {
        if (_topLevel != null)
        {
            _topLevel.PropertyChanged -= OnTopLevelPropertyChanged;
            _topLevel.ScalingChanged -= OnTopLevelScalingChanged;
        }

        if (_isHandlerRunning)
            _compositionVisual?.SendHandlerMessage(ProcessRowHoverHandler.StopMessage);

        _isHandlerRunning = false;
        _hasLastSentState = false;
        _topLevel = null;
        ElementComposition.SetElementChildVisual(this, null);
        _compositionVisual = null;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        DetachCompositionVisual();
    }

    private readonly record struct ProcessRowHoverRenderState(
        ProcessRowHoverGeometry Geometry,
        Point ClientOrigin,
        double RenderScaling,
        bool HasCoordinateMap);

    private sealed class ProcessRowHoverHandler : CompositionCustomVisualHandler
    {
        public static readonly object StartMessage = new();
        public static readonly object StopMessage = new();

        private readonly ImmutableSolidColorBrush _brush;
        private readonly IntPtr _windowHandle;
        private ProcessRowHoverRenderState _state;
        private int _hoveredVisibleIndex = -1;
        private bool _isRunning;

        public ProcessRowHoverHandler(
            Color color,
            IntPtr windowHandle,
            ProcessRowHoverRenderState state)
        {
            _brush = new ImmutableSolidColorBrush(color);
            _windowHandle = windowHandle;
            _state = state;
        }

        public override void OnMessage(object message)
        {
            switch (message)
            {
                case ProcessRowHoverRenderState state:
                    ApplyState(state);
                    return;
                default:
                    if (ReferenceEquals(message, StartMessage))
                    {
                        if (_isRunning) return;

                        _isRunning = true;
                        Invalidate();
                        RegisterForNextAnimationFrameUpdate();
                        return;
                    }

                    if (!ReferenceEquals(message, StopMessage) || !_isRunning) return;

                    _isRunning = false;
                    SetHoveredVisibleIndex(-1);
                    return;
            }
        }

        public override void OnAnimationFrameUpdate()
        {
            if (!_isRunning) return;

            SetHoveredVisibleIndex(SampleHoveredVisibleIndex());
            RegisterForNextAnimationFrameUpdate();
        }

        public override void OnRender(ImmediateDrawingContext drawingContext)
        {
            int latestVisibleIndex = SampleHoveredVisibleIndex();
            SetHoveredVisibleIndex(latestVisibleIndex);
            if (latestVisibleIndex < 0) return;

            Rect rowBounds = _state.Geometry.GetRowBounds(latestVisibleIndex, EffectiveSize.X);
            if (rowBounds.Width <= 0 || rowBounds.Height <= 0) return;

            drawingContext.DrawRectangle(_brush, null, rowBounds);
        }

        private void ApplyState(ProcessRowHoverRenderState state)
        {
            if (_state == state) return;

            ProcessRowHoverGeometry previousGeometry = _state.Geometry;
            int previousVisibleIndex = _hoveredVisibleIndex;
            _state = state;
            _hoveredVisibleIndex = SampleHoveredVisibleIndex();
            InvalidateRow(previousGeometry, previousVisibleIndex);
            InvalidateRow(_state.Geometry, _hoveredVisibleIndex);
        }

        private int SampleHoveredVisibleIndex()
        {
            if (!_isRunning
                || !_state.HasCoordinateMap
                || !User32.GetCursorPos(out User32.POINT screenPosition))
            {
                return -1;
            }

            IntPtr pointedWindow = User32.WindowFromPoint(screenPosition);
            if (pointedWindow == IntPtr.Zero
                || User32.GetAncestor(pointedWindow, User32.GA_ROOT) != _windowHandle)
            {
                return -1;
            }

            User32.POINT clientPosition = screenPosition;
            if (!User32.ScreenToClient(_windowHandle, ref clientPosition)) return -1;

            Point localPosition = new(
                clientPosition.X / _state.RenderScaling - _state.ClientOrigin.X,
                clientPosition.Y / _state.RenderScaling - _state.ClientOrigin.Y);
            return _state.Geometry.HitTest(localPosition);
        }

        private void SetHoveredVisibleIndex(int visibleIndex)
        {
            if (_hoveredVisibleIndex == visibleIndex) return;

            int previousVisibleIndex = _hoveredVisibleIndex;
            _hoveredVisibleIndex = visibleIndex;
            InvalidateRow(_state.Geometry, previousVisibleIndex);
            InvalidateRow(_state.Geometry, visibleIndex);
        }

        private void InvalidateRow(ProcessRowHoverGeometry geometry, int visibleIndex)
        {
            Rect bounds = geometry.GetRowBounds(visibleIndex, EffectiveSize.X);
            if (bounds.Width > 0 && bounds.Height > 0)
                Invalidate(bounds);
        }
    }
}

/// <summary>Moves the hovered header-cell rectangle without invalidating header or row drawings.</summary>
internal sealed class ProcessHeaderHoverVisual(Color color) : ProcessTableHighlightVisual(color)
{
    private Rect? _highlightBounds;

    /// <summary>Applies the current header cell bounds directly to the compositor.</summary>
    public void SetHighlightBounds(Rect? highlightBounds)
    {
        if (IsDisposed || _highlightBounds == highlightBounds) return;
        if (highlightBounds.HasValue)
        {
            Rect bounds = highlightBounds.Value;
            if (!double.IsFinite(bounds.X)
                || !double.IsFinite(bounds.Y)
                || !double.IsFinite(bounds.Width)
                || !double.IsFinite(bounds.Height)
                || bounds.Width < 0
                || bounds.Height < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(highlightBounds));
            }
        }

        _highlightBounds = highlightBounds;
        UpdateHighlight();
    }

    protected override Rect? ResolveHighlightBounds(Size hostSize) => _highlightBounds;
}
