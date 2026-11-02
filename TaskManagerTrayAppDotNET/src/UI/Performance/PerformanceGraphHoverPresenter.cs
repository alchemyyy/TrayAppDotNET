using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Rendering.Composition;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>Drawing resources shared by one graph's hover presenter and its render-thread handler.</summary>
/// <param name="Metrics">Layout rules for the line and label.</param>
/// <param name="LinePen">Dashed pen for the hover line.</param>
/// <param name="LineTerminalPen">Solid pen that ends a snapped line above the label, or null for none.</param>
/// <param name="TextColor">Live palette color for the label text, read on the UI thread.</param>
/// <param name="Typeface">Label typeface.</param>
/// <param name="FontSize">Label font size.</param>
internal sealed record PerformanceGraphHoverStyle(
    PerformanceGraphHoverMetrics Metrics,
    ImmutablePen LinePen,
    ImmutablePen? LineTerminalPen,
    SettingsPaletteColor TextColor,
    Typeface Typeface,
    double FontSize);

/// <summary>
/// Draws one performance graph's hover line and metric label on the render thread, which samples the Win32 cursor
/// every animation frame while the pointer is over the graph. The UI thread supplies the sample timeline, the label
/// for its own latest pointer sample, and the pointer-over state that starts and stops sampling.
/// </summary>
/// <remarks>
/// A label can trail the line by the UI thread's latency during fast movement; the line, the label position and the
/// gap around the label always follow the latest cursor. Labels shaped here pass to the handler, which disposes them.
/// </remarks>
internal sealed class PerformanceGraphHoverPresenter(Control host, PerformanceGraphHoverStyle style)
{
    private CompositionCustomVisual? _compositionVisual;
    private TopLevel? _topLevel;
    private PerformanceGraphHoverContent? _sentContent;
    private string? _sentLabelText;
    private double _sentLabelMaximumWidth;
    private int _sentLabelMaximumLines;
    private PerformanceGraphHoverMap _sentMap;
    private bool _hasSentMap;
    private bool _isPointerOver;

    /// <summary>Whether the render thread is sampling the cursor for this graph and accepts content.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Creates the render-thread visual once the host joins a window.</summary>
    public void Attach()
    {
        if (_compositionVisual != null || !OperatingSystem.IsWindows()) return;

        CompositionVisual? hostVisual = ElementComposition.GetElementVisual(host);
        TopLevel? topLevel = TopLevel.GetTopLevel(host);
        IntPtr windowHandle = topLevel?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hostVisual == null || topLevel == null || windowHandle == IntPtr.Zero) return;

        CompositionCustomVisual compositionVisual = hostVisual.Compositor.CreateCustomVisual(
            new PerformanceGraphHoverHandler(
                style.Metrics,
                style.LinePen,
                style.LineTerminalPen,
                windowHandle));
        compositionVisual.Size = new Vector(host.Bounds.Width, host.Bounds.Height);
        _compositionVisual = compositionVisual;
        _topLevel = topLevel;
        ElementComposition.SetElementChildVisual(host, compositionVisual);
    }

    /// <summary>Stops sampling and releases the render-thread visual when the host leaves its window.</summary>
    public void Detach()
    {
        // A detached host receives no PointerExited, so a later attach must wait for a fresh PointerEntered
        _isPointerOver = false;
        UpdateActiveState();
        _topLevel = null;
        if (_compositionVisual == null) return;

        ElementComposition.SetElementChildVisual(host, compositionVisual: null);
        _compositionVisual = null;
    }

    /// <summary>Keeps the render-thread visual sized to the host.</summary>
    public void SetSize(Size size) => _compositionVisual?.Size = new Vector(size.Width, size.Height);

    /// <summary>Starts or stops render-thread sampling as the pointer enters or leaves the host's hover area.</summary>
    public void SetPointerOver(bool isPointerOver)
    {
        _isPointerOver = isPointerOver;
        UpdateActiveState();
        // Pointer events follow layout moves, so they also refresh the cursor coordinate map
        if (IsActive) SendCoordinateMap();
    }

    /// <summary>
    /// Sends the hover content for the latest UI pointer sample. Ignored while inactive; an unchanged request sends
    /// nothing, and an unchanged label is reused rather than shaped again.
    /// </summary>
    /// <param name="graphSize">Graph size the content was resolved for.</param>
    /// <param name="hoverBounds">Graph-local area where the pointer shows the hover.</param>
    /// <param name="sampleFractions">Timeline snapshot for snapping; never mutated after it is passed.</param>
    /// <param name="hasIndicator">Whether the graph has anything to indicate.</param>
    /// <param name="labelText">Label text, or null to draw only the line.</param>
    /// <param name="labelMaximumWidth">Width beyond which the label is trimmed.</param>
    /// <param name="labelMaximumLines">Line limit for the label, or 0 for none.</param>
    public void Update(
        Size graphSize,
        Rect hoverBounds,
        double[] sampleFractions,
        bool hasIndicator,
        string? labelText,
        double labelMaximumWidth,
        int labelMaximumLines)
    {
        if (!IsActive || _compositionVisual == null) return;

        PerformanceGraphHoverContent? sentContent = _sentContent;
        Color textColor = style.TextColor.Value;
        bool canReuseLabel = labelText != null
                             && sentContent?.Label != null
                             && labelText == _sentLabelText
                             && labelMaximumWidth == _sentLabelMaximumWidth
                             && labelMaximumLines == _sentLabelMaximumLines;
        if (sentContent != null
            && (canReuseLabel || (labelText == null && sentContent.Label == null))
            && sentContent.GraphSize == graphSize
            && sentContent.HoverBounds == hoverBounds
            && ReferenceEquals(sentContent.SampleFractions, sampleFractions)
            && sentContent.HasIndicator == hasIndicator
            && sentContent.TextBrush.Color == textColor)
            return;

        PerformanceGraphHoverLabel? label = labelText == null
            ? null
            : canReuseLabel
                ? sentContent!.Label
                : PerformanceGraphHoverLabel.Create(
                    labelText,
                    style.Typeface,
                    style.FontSize,
                    labelMaximumWidth,
                    labelMaximumLines);
        ImmutableSolidColorBrush textBrush = sentContent != null && sentContent.TextBrush.Color == textColor
            ? sentContent.TextBrush
            : new ImmutableSolidColorBrush(textColor);
        _sentContent = new PerformanceGraphHoverContent(
            graphSize,
            hoverBounds,
            sampleFractions,
            hasIndicator,
            label,
            textBrush);
        _sentLabelText = labelText;
        _sentLabelMaximumWidth = labelMaximumWidth;
        _sentLabelMaximumLines = labelMaximumLines;
        _compositionVisual.SendHandlerMessage(_sentContent);
    }

    private void UpdateActiveState()
    {
        bool shouldBeActive = _compositionVisual != null
                              && _isPointerOver
                              && _topLevel is { IsVisible: true }
                                  and not Window { WindowState: WindowState.Minimized };
        if (IsActive == shouldBeActive) return;

        IsActive = shouldBeActive;
        if (shouldBeActive)
        {
            _topLevel!.PropertyChanged += OnTopLevelPropertyChanged;
            _topLevel.ScalingChanged += OnTopLevelScalingChanged;
            host.LayoutUpdated += OnHostLayoutUpdated;
            SendCoordinateMap();
            _compositionVisual!.SendHandlerMessage(PerformanceGraphHoverHandler.StartMessage);
            return;
        }

        if (_topLevel != null)
        {
            _topLevel.PropertyChanged -= OnTopLevelPropertyChanged;
            _topLevel.ScalingChanged -= OnTopLevelScalingChanged;
        }

        host.LayoutUpdated -= OnHostLayoutUpdated;
        // The handler disposes the last label it received when it stops
        _compositionVisual?.SendHandlerMessage(PerformanceGraphHoverHandler.StopMessage);
        _sentContent = null;
        _sentLabelText = null;
        _hasSentMap = false;
    }

    private void OnTopLevelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.Property == Visual.IsVisibleProperty
            || eventArgs.Property == Window.WindowStateProperty)
            UpdateActiveState();
    }

    private void OnTopLevelScalingChanged(object? sender, EventArgs eventArgs) => SendCoordinateMap();

    private void OnHostLayoutUpdated(object? sender, EventArgs eventArgs) => SendCoordinateMap();

    private void SendCoordinateMap()
    {
        if (_compositionVisual == null || _topLevel == null) return;

        Point? clientOrigin = host.TranslatePoint(point: default, _topLevel);
        double renderScaling = _topLevel.RenderScaling;
        if (clientOrigin is not { } origin
            || !double.IsFinite(origin.X)
            || !double.IsFinite(origin.Y)
            || !double.IsFinite(renderScaling)
            || renderScaling <= 0)
            return;

        PerformanceGraphHoverMap map = new(origin, renderScaling);
        if (_hasSentMap && _sentMap == map) return;

        _sentMap = map;
        _hasSentMap = true;
        _compositionVisual.SendHandlerMessage(map);
    }

    /// <summary>Maps physical client pixels to host-local DIPs on the render thread.</summary>
    private readonly record struct PerformanceGraphHoverMap(Point ClientOrigin, double RenderScaling);

    /// <summary>Immutable hover content; the handler owns its label once the content is sent.</summary>
    private sealed class PerformanceGraphHoverContent(
        Size graphSize,
        Rect hoverBounds,
        double[] sampleFractions,
        bool hasIndicator,
        PerformanceGraphHoverLabel? label,
        ImmutableSolidColorBrush textBrush)
    {
        public Size GraphSize { get; } = graphSize;
        public Rect HoverBounds { get; } = hoverBounds;
        public double[] SampleFractions { get; } = sampleFractions;
        public bool HasIndicator { get; } = hasIndicator;
        public PerformanceGraphHoverLabel? Label { get; } = label;
        public ImmutableSolidColorBrush TextBrush { get; } = textBrush;
    }

    /// <summary>
    /// Samples the cursor and paints the hover line and label on the render thread. It holds only immutable drawing
    /// state; the live palette color stays with the presenter.
    /// </summary>
    private sealed class PerformanceGraphHoverHandler(
        PerformanceGraphHoverMetrics metrics,
        ImmutablePen linePen,
        ImmutablePen? lineTerminalPen,
        IntPtr windowHandle) : CompositionCustomVisualHandler
    {
        public static readonly object StartMessage = new();
        public static readonly object StopMessage = new();

        private PerformanceGraphHoverContent? _content;
        private PerformanceGraphHoverMap _map;
        private bool _hasMap;
        private bool _isRunning;
        private PerformanceGraphHoverPlacement _placement;
        private bool _hasPlacement;

        public override void OnMessage(object message)
        {
            switch (message)
            {
                case PerformanceGraphHoverContent content:
                    ReplaceContent(content);
                    return;
                case PerformanceGraphHoverMap map:
                    _map = map;
                    _hasMap = true;
                    UpdatePlacement(forceInvalidate: false);
                    return;
            }

            if (ReferenceEquals(message, StartMessage))
            {
                if (_isRunning) return;

                _isRunning = true;
                UpdatePlacement(forceInvalidate: false);
                RegisterForNextAnimationFrameUpdate();
                return;
            }

            if (!ReferenceEquals(message, StopMessage)) return;

            _isRunning = false;
            ReplaceContent(content: null);
        }

        public override void OnAnimationFrameUpdate()
        {
            if (!_isRunning) return;

            UpdatePlacement(forceInvalidate: false);
            RegisterForNextAnimationFrameUpdate();
        }

        public override void OnRender(ImmediateDrawingContext drawingContext)
        {
            // Paint the placement this frame's update resolved, so every painted line falls inside a repaint the
            // handler requested
            if (!_hasPlacement || _content is not { } content) return;

            PerformanceGraphHoverPlacement placement = _placement;
            double lineX = placement.LineX;
            if (!placement.HasLineGap)
            {
                drawingContext.DrawLine(
                    linePen,
                    new Point(lineX, y: 0),
                    new Point(lineX, placement.Height));
            }
            else
            {
                if (placement.UpperLineEnd > 0)
                {
                    drawingContext.DrawLine(
                        linePen,
                        new Point(lineX, y: 0),
                        new Point(lineX, placement.UpperLineEnd));
                    if (lineTerminalPen != null)
                    {
                        drawingContext.DrawLine(
                            lineTerminalPen,
                            new Point(lineX, placement.LineTerminalStart),
                            new Point(lineX, placement.UpperLineEnd));
                    }
                }

                if (placement.LowerLineStart < placement.Height)
                {
                    drawingContext.DrawLine(
                        linePen,
                        new Point(lineX, placement.LowerLineStart),
                        new Point(lineX, placement.Height));
                }
            }

            if (placement.HasLabel)
                content.Label?.Draw(drawingContext, content.TextBrush, placement.LabelOrigin);
        }

        private void ReplaceContent(PerformanceGraphHoverContent? content)
        {
            PerformanceGraphHoverLabel? previousLabel = _content?.Label;
            _content = content;
            // New content can change the label text without moving it, so repaint even an unchanged placement
            UpdatePlacement(forceInvalidate: true);
            if (previousLabel != null && !ReferenceEquals(previousLabel, content?.Label))
                previousLabel.Dispose();
        }

        private void UpdatePlacement(bool forceInvalidate)
        {
            bool hasPlacement = TrySamplePlacement(out PerformanceGraphHoverPlacement placement);
            bool changed = hasPlacement != _hasPlacement
                           || (hasPlacement && (forceInvalidate || placement != _placement));
            _hasPlacement = hasPlacement;
            _placement = placement;
            // Repaint the whole graph: invalidating only the old and new line rectangles left stale lines and labels
            // behind during fast pointer movement
            if (changed) Invalidate();
        }

        private bool TrySamplePlacement(out PerformanceGraphHoverPlacement placement)
        {
            placement = default;
            return _isRunning
                   && _hasMap
                   && _content is { } content
                   && RenderThreadCursor.TryGetLocalPosition(
                       windowHandle,
                       _map.ClientOrigin,
                       _map.RenderScaling,
                       out Point pointerPosition)
                   && PerformanceGraphHoverLayout.TryPlace(
                       metrics,
                       content.GraphSize,
                       content.HoverBounds,
                       content.SampleFractions,
                       content.HasIndicator,
                       content.Label?.Metrics,
                       pointerPosition,
                       out placement);
        }
    }
}

/// <summary>A hover label shaped on the UI thread whose glyph runs are drawn on the render thread.</summary>
internal sealed class PerformanceGraphHoverLabel : IDisposable
{
    private readonly PlacedGlyphRun[] _glyphRuns;

    private PerformanceGraphHoverLabel(PlacedGlyphRun[] glyphRuns, PerformanceGraphHoverLabelMetrics metrics)
    {
        _glyphRuns = glyphRuns;
        Metrics = metrics;
    }

    public PerformanceGraphHoverLabelMetrics Metrics { get; }

    /// <summary>
    /// Lays out one trimmed label and keeps render-thread references to its shaped glyph runs, placed the way
    /// <see cref="TextLine.Draw"/> places baseline-aligned runs.
    /// </summary>
    public static PerformanceGraphHoverLabel Create(
        string text,
        Typeface typeface,
        double fontSize,
        double maximumWidth,
        int maximumLines)
    {
        using TextLayout layout = new(
            text,
            typeface,
            fontSize,
            foreground: null,
            textWrapping: TextWrapping.NoWrap,
            textTrimming: TextTrimming.CharacterEllipsis,
            maxWidth: maximumWidth,
            maxLines: maximumLines);
        IReadOnlyList<TextLine> lines = layout.TextLines;
        int glyphRunCount = 0;
        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            IReadOnlyList<TextRun> textRuns = lines[lineIndex].TextRuns;
            for (int runIndex = 0; runIndex < textRuns.Count; runIndex++)
            {
                if (textRuns[runIndex] is ShapedTextRun { GlyphRun.GlyphInfos.Count: > 0 })
                    glyphRunCount++;
            }
        }

        PlacedGlyphRun[] glyphRuns = new PlacedGlyphRun[glyphRunCount];
        int glyphRunIndex = 0;
        try
        {
            double lineTop = 0;
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                TextLine line = lines[lineIndex];
                IReadOnlyList<TextRun> textRuns = line.TextRuns;
                double runLeft = line.Start;
                for (int runIndex = 0; runIndex < textRuns.Count; runIndex++)
                {
                    if (textRuns[runIndex] is not DrawableTextRun drawableRun) continue;

                    if (drawableRun is ShapedTextRun { GlyphRun.GlyphInfos.Count: > 0 } shapedRun)
                    {
                        IImmutableGlyphRunReference glyphRun =
                            shapedRun.GlyphRun.TryCreateImmutableGlyphRunReference()
                            ?? throw new InvalidOperationException("A shaped hover label has no glyph run.");
                        glyphRuns[glyphRunIndex++] = new PlacedGlyphRun(
                            glyphRun,
                            new Point(runLeft, lineTop + line.Baseline - shapedRun.Baseline));
                    }

                    runLeft += drawableRun.Size.Width;
                }

                lineTop += line.Height;
            }
        }
        catch
        {
            for (int createdIndex = 0; createdIndex < glyphRunIndex; createdIndex++)
                glyphRuns[createdIndex].GlyphRun.Dispose();
            throw;
        }

        return new PerformanceGraphHoverLabel(
            glyphRuns,
            new PerformanceGraphHoverLabelMetrics(
                layout.Width,
                layout.Height,
                layout.Extent,
                layout.OverhangAfter));
    }

    /// <summary>Draws the label with its layout box's top-left corner at the origin.</summary>
    public void Draw(ImmediateDrawingContext drawingContext, IImmutableBrush brush, Point origin)
    {
        for (int glyphRunIndex = 0; glyphRunIndex < _glyphRuns.Length; glyphRunIndex++)
        {
            PlacedGlyphRun glyphRun = _glyphRuns[glyphRunIndex];
            using (drawingContext.PushPreTransform(Matrix.CreateTranslation(
                       origin.X + glyphRun.Origin.X,
                       origin.Y + glyphRun.Origin.Y)))
                drawingContext.DrawGlyphRun(brush, glyphRun.GlyphRun);
        }
    }

    public void Dispose()
    {
        for (int glyphRunIndex = 0; glyphRunIndex < _glyphRuns.Length; glyphRunIndex++)
            _glyphRuns[glyphRunIndex].GlyphRun.Dispose();
    }

    private readonly record struct PlacedGlyphRun(IImmutableGlyphRunReference GlyphRun, Point Origin);
}
