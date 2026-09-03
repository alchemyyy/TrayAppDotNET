using SkiaSharp;

namespace TaskManagerTrayAppDotNET.UI.Tray;

internal sealed record TaskManagerTrayIconRenderInput(
    TrayGraphStyle Style,
    TrayGraphDataSource DataSource,
    double[] Values,
    double[]? CPUHighestCoreValues = null);

internal sealed class TaskManagerTrayIcon : IDisposable
{
    public const int HistoryCapacity = 16;

    // AXAML hot-reload exception: Tray icon rendering runs on the background render queue and
    // cannot safely read mutable Avalonia resource dictionaries. Keep these values aligned with
    // the Performance graph resources in TaskManagerWindow.axaml
    private const float CornerRadiusScale = 0.1f;
    // Scales the complete icon inside the Windows-provided canvas while keeping it centered
    private const float RenderedIconScale = 0.88f;
    // Applies an additional horizontal-only scale after the complete icon scale
    private const float RenderedIconWidthScale = 0.96f;
    // Applies an optical vertical adjustment in output pixels
    // this seems to be what Windows does to the official Task Manager icon.
    // The reasoning for it is the more intense bottom border makes the icon overall bottom-heavy
    // So the icon appears to be lower than it actually is.
    // Setting this to fractional values can possibly ruin blending
    private const float RenderedIconVerticalOffset = -0.0f;
    private const int BorderSupersamplingScale = 4;
    private const float GraphLineThickness = 2f;
    private const float GraphUnderfillOpacity = 1f;
    private const int GraphUnderfillDarkenAmount = 20;
    private const float GridLineThickness = 0.75f;
    private const float GridOpacity = 92.0f / byte.MaxValue;

    private static readonly SKColor BackgroundColor = new(red: 28, green: 28, blue: 28, alpha: 255);
    private static readonly SKColor CPUGraphLineColor = new(red: 87, green: 192, blue: 255, alpha: 220);
    private static readonly SKColor CPUHighestCoreGraphLineColor = new(red: 87, green: 192, blue: 255, alpha: 200);

    private static readonly SKColor GridColor = new(red: 177, green: 180, blue: 178,
        (byte)Math.Round(byte.MaxValue * GridOpacity));

    private static readonly SKColor TopAndLeftBorderColor = new(red: 100, green: 100, blue: 100);
    private static readonly SKColor RightAndBottomBorderColor = new(red: 185, green: 185, blue: 185);

    private readonly Lock _gate = new();
    private readonly SystemPerformanceSample[] _history = new SystemPerformanceSample[HistoryCapacity];
    private int _historyStart;
    private int _historyCount;
    private bool _disposed;

    /// <summary>Adds one sample to the fixed-size chronological history.</summary>
    public void AddSample(SystemPerformanceSample sample)
    {
        if (_historyCount < _history.Length)
        {
            int insertIndex = (_historyStart + _historyCount) % _history.Length;
            _history[insertIndex] = sample;
            _historyCount++;
            return;
        }

        _history[_historyStart] = sample;
        _historyStart = (_historyStart + 1) % _history.Length;
    }

    /// <summary>Creates an immutable background-render input from the selected metric.</summary>
    public TaskManagerTrayIconRenderInput CreateRenderInput(
        TrayGraphStyle style,
        TrayGraphDataSource dataSource,
        bool showCPUHighestCoreTrace)
    {
        int valueCount = Math.Max(val1: 1, _historyCount);
        double[] values = new double[valueCount];
        bool includeCPUHighestCoreValues = showCPUHighestCoreTrace
                                           && style == TrayGraphStyle.Marquee
                                           && dataSource == TrayGraphDataSource.CPUAverage;
        double[]? cpuHighestCoreValues = includeCPUHighestCoreValues
            ? new double[valueCount]
            : null;
        for (int valueIndex = 0; valueIndex < _historyCount; valueIndex++)
        {
            int historyIndex = (_historyStart + valueIndex) % _history.Length;
            SystemPerformanceSample sample = _history[historyIndex];
            values[valueIndex] = sample.Select(dataSource);
            if (cpuHighestCoreValues != null)
                cpuHighestCoreValues[valueIndex] = sample.CPUHighestCorePercent;
        }

        return new TaskManagerTrayIconRenderInput(
            style,
            dataSource,
            values,
            cpuHighestCoreValues);
    }

    /// <summary>Renders a caller-owned native icon for the shared background render queue.</summary>
    public NativeIcon? RenderIcon(TaskManagerTrayIconRenderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            lock (_gate)
            {
                if (_disposed) return null;

                int iconSize = TrayAppDotNETTrayIconMetrics.GetTaskbarSmallIconSize();
                byte[] png = RenderPng(iconSize, input);
                return NativeIcon.FromIconImage(png, iconSize);
            }
        }
        catch (Exception exception)
        {
            TADNLog.Log($"TaskManagerTrayIcon.RenderIcon: {exception.Message}");
            return AppThemeStore.LoadAppNativeIcon();
        }
    }

    internal static byte[] RenderPng(int size, TaskManagerTrayIconRenderInput input)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        ArgumentNullException.ThrowIfNull(input);

        SKImageInfo imageInfo = new(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKBitmap bitmap = new(imageInfo);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        float renderedIconScaleX = RenderedIconScale * RenderedIconWidthScale;
        float renderedIconInsetX = size * (1.0f - renderedIconScaleX) / 2.0f;
        float renderedIconInsetY = size * (1.0f - RenderedIconScale) / 2.0f
                                   + RenderedIconVerticalOffset;
        canvas.Save();
        canvas.Translate(renderedIconInsetX, renderedIconInsetY);
        canvas.Scale(renderedIconScaleX, RenderedIconScale);

        float borderWidth = Math.Max(val1: 1.0f, size / 14.0f);
        float cornerRadius = Math.Max(val1: borderWidth * 2, size * CornerRadiusScale);
        SKRect surfaceBounds = new(left: 0, top: 0, right: size, bottom: size);
        using (SKPaint backgroundPaint = new()
        {
            Color = BackgroundColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        })
        {
            canvas.DrawRoundRect(
                surfaceBounds,
                cornerRadius,
                cornerRadius,
                backgroundPaint);
        }

        SKRect graphBounds = new(
            borderWidth,
            borderWidth,
            size - borderWidth,
            size - borderWidth);
        float graphCornerRadius = Math.Max(val1: 0, cornerRadius - borderWidth);

        canvas.Save();
        using SKPath graphClipPath = new();
        graphClipPath.AddRoundRect(
            graphBounds,
            graphCornerRadius,
            graphCornerRadius);
        canvas.ClipPath(graphClipPath, SKClipOperation.Intersect, antialias: true);
        DrawGraph(canvas, graphBounds, input);
        canvas.Restore();
        canvas.Restore();
        DrawBorder(canvas, size, borderWidth, cornerRadius);

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        return data.ToArray();
    }

    private static void DrawGraph(
        SKCanvas canvas,
        SKRect graphBounds,
        TaskManagerTrayIconRenderInput input)
    {
        using SKPath underfillPath = new();
        using SKPath linePath = new();
        switch (input.Style)
        {
            case TrayGraphStyle.Current:
                BuildCurrentGraphPaths(
                    graphBounds,
                    input.Values,
                    underfillPath,
                    linePath);
                break;

            case TrayGraphStyle.Marquee:
            default:
                BuildMarqueeGraphPaths(
                    graphBounds,
                    input.Values,
                    underfillPath,
                    linePath);
                break;
        }

        SKColor lineColor = GetGraphLineColor(input.DataSource);
        using (SKPaint underfillPaint = new()
        {
            Color = CreateUnderfillColor(lineColor),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        })
        {
            canvas.DrawPath(underfillPath, underfillPaint);
        }

        DrawGrid(canvas, graphBounds);
        DrawCPUHighestCoreMarqueeLine(canvas, graphBounds, input);
        using SKPaint linePaint = CreateGraphLinePaint(lineColor);
        canvas.DrawPath(linePath, linePaint);
    }

    /// <summary>Draws highest-core utilization behind the overall CPU marquee trace.</summary>
    private static void DrawCPUHighestCoreMarqueeLine(
        SKCanvas canvas,
        SKRect graphBounds,
        TaskManagerTrayIconRenderInput input)
    {
        if (input.Style != TrayGraphStyle.Marquee
            || input.CPUHighestCoreValues is not { Length: > 0 } cpuHighestCoreValues)
            return;

        using SKPath unusedUnderfillPath = new();
        using SKPath linePath = new();
        BuildMarqueeGraphPaths(
            graphBounds,
            cpuHighestCoreValues,
            unusedUnderfillPath,
            linePath);
        using SKPaint linePaint = CreateGraphLinePaint(CPUHighestCoreGraphLineColor);
        canvas.DrawPath(linePath, linePaint);
    }

    private static void BuildCurrentGraphPaths(
        SKRect graphBounds,
        IReadOnlyList<double> values,
        SKPath underfillPath,
        SKPath linePath)
    {
        double currentPercent = values.Count > 0 ? NormalizePercent(values[^1]) : 0;
        float graphTop = PercentToY(graphBounds, currentPercent);
        underfillPath.MoveTo(graphBounds.Left, graphBounds.Bottom);
        underfillPath.LineTo(graphBounds.Left, graphTop);
        underfillPath.LineTo(graphBounds.Right, graphTop);
        underfillPath.LineTo(graphBounds.Right, graphBounds.Bottom);
        underfillPath.Close();
        linePath.MoveTo(graphBounds.Left, graphTop);
        linePath.LineTo(graphBounds.Right, graphTop);
    }

    private static void BuildMarqueeGraphPaths(
        SKRect graphBounds,
        IReadOnlyList<double> values,
        SKPath underfillPath,
        SKPath linePath)
    {
        if (values.Count == 0) return;
        if (values.Count == 1)
        {
            BuildCurrentGraphPaths(graphBounds, values, underfillPath, linePath);
            return;
        }

        double[] samplePositions = TaskManagerTrayGraphSampler.CreateSamplePositions(values.Count);
        underfillPath.MoveTo(graphBounds.Left, graphBounds.Bottom);
        for (int sampleIndex = 0; sampleIndex < values.Count; sampleIndex++)
        {
            float graphX = graphBounds.Left
                           + (float)samplePositions[sampleIndex] * graphBounds.Width;
            float graphY = PercentToY(graphBounds, values[sampleIndex]);
            if (sampleIndex == 0)
            {
                underfillPath.LineTo(graphX, graphY);
                linePath.MoveTo(graphX, graphY);
                continue;
            }

            underfillPath.LineTo(graphX, graphY);
            linePath.LineTo(graphX, graphY);
        }

        underfillPath.LineTo(graphBounds.Right, graphBounds.Bottom);
        underfillPath.Close();
    }

    private static void DrawGrid(SKCanvas canvas, SKRect graphBounds)
    {
        using SKPaint gridPaint = new()
        {
            Color = GridColor,
            IsAntialias = true,
            StrokeWidth = GridLineThickness,
            Style = SKPaintStyle.Stroke
        };
        for (int gridIndex = 1; gridIndex < 4; gridIndex++)
        {
            float fraction = gridIndex / 4.0f;
            float gridX = graphBounds.Left + fraction * graphBounds.Width;
            float gridY = graphBounds.Top + fraction * graphBounds.Height;
            canvas.DrawLine(gridX, graphBounds.Top, gridX, graphBounds.Bottom, gridPaint);
            canvas.DrawLine(graphBounds.Left, gridY, graphBounds.Right, gridY, gridPaint);
        }
    }

    private static void DrawBorder(
        SKCanvas canvas,
        int size,
        float borderWidth,
        float cornerRadius)
    {
        int supersampledSize = checked(size * BorderSupersamplingScale);
        SKImageInfo imageInfo = new(
            supersampledSize,
            supersampledSize,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
        using SKBitmap bitmap = new(imageInfo);
        using (SKCanvas borderCanvas = new(bitmap))
        {
            borderCanvas.Clear(SKColors.Transparent);
            borderCanvas.Scale(BorderSupersamplingScale);
            float renderedIconScaleX = RenderedIconScale * RenderedIconWidthScale;
            float renderedIconInsetX = size * (1.0f - renderedIconScaleX) / 2.0f;
            float renderedIconInsetY = size * (1.0f - RenderedIconScale) / 2.0f
                                       + RenderedIconVerticalOffset;
            borderCanvas.Translate(renderedIconInsetX, renderedIconInsetY);
            borderCanvas.Scale(renderedIconScaleX, RenderedIconScale);

            SKRect outerBounds = new(left: 0, top: 0, right: size, bottom: size);
            // Counter the horizontal icon scaling so every border edge has equal device thickness
            float borderWidthX = borderWidth / RenderedIconWidthScale;
            float borderWidthY = borderWidth;
            SKRect innerBounds = new(
                borderWidthX,
                borderWidthY,
                size - borderWidthX,
                size - borderWidthY);
            float innerCornerRadiusX = Math.Max(val1: 0, cornerRadius - borderWidthX);
            float innerCornerRadiusY = Math.Max(val1: 0, cornerRadius - borderWidthY);
            using SKPath borderPath = new() { FillType = SKPathFillType.EvenOdd };
            borderPath.AddRoundRect(outerBounds, cornerRadius, cornerRadius);
            borderPath.AddRoundRect(innerBounds, innerCornerRadiusX, innerCornerRadiusY);
            using (SKPaint topAndLeftBorderPaint = new()
            {
                Color = TopAndLeftBorderColor,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            })
            {
                borderCanvas.DrawPath(borderPath, topAndLeftBorderPaint);
            }

            // Keep the right and bottom edges plus their adjoining corner arcs
            using SKPath visibleBorderPath = new();
            visibleBorderPath.AddRect(new SKRect(
                left: size - cornerRadius,
                top: 0,
                right: size,
                bottom: size));
            visibleBorderPath.AddRect(new SKRect(
                left: 0,
                top: size - cornerRadius,
                right: size,
                bottom: size));
            borderCanvas.Save();
            borderCanvas.ClipPath(
                visibleBorderPath,
                SKClipOperation.Intersect,
                antialias: false);
            using SKPaint rightAndBottomBorderPaint = new()
            {
                BlendMode = SKBlendMode.SrcATop,
                Color = RightAndBottomBorderColor,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            borderCanvas.DrawPaint(rightAndBottomBorderPaint);
            borderCanvas.Restore();

            DrawBorderCornerBlend(
                borderCanvas,
                new SKRect(size - cornerRadius, 0, size, cornerRadius),
                new SKPoint(size - cornerRadius, cornerRadius),
                startAngle: 270,
                endAngle: 360,
                TopAndLeftBorderColor,
                RightAndBottomBorderColor);
            DrawBorderCornerBlend(
                borderCanvas,
                new SKRect(0, size - cornerRadius, cornerRadius, size),
                new SKPoint(cornerRadius, size - cornerRadius),
                startAngle: 90,
                endAngle: 180,
                RightAndBottomBorderColor,
                TopAndLeftBorderColor);
        }

        using SKImage borderImage = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(
            borderImage,
            new SKRect(left: 0, top: 0, right: size, bottom: size),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
    }

    /// <summary>Blends border colors around a corner without changing the border's alpha coverage.</summary>
    private static void DrawBorderCornerBlend(
        SKCanvas canvas,
        SKRect cornerBounds,
        SKPoint cornerCenter,
        float startAngle,
        float endAngle,
        SKColor startColor,
        SKColor endColor)
    {
        canvas.Save();
        canvas.ClipRect(cornerBounds, SKClipOperation.Intersect, antialias: false);
        using SKShader cornerShader = SKShader.CreateSweepGradient(
            cornerCenter,
            [startColor, endColor],
            SKShaderTileMode.Clamp,
            startAngle,
            endAngle);
        using SKPaint cornerPaint = new()
        {
            BlendMode = SKBlendMode.SrcATop,
            IsAntialias = true,
            IsDither = true,
            Shader = cornerShader,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawPaint(cornerPaint);
        canvas.Restore();
    }

    private static SKPaint CreateGraphLinePaint(SKColor lineColor) =>
        new()
        {
            Color = lineColor,
            IsAntialias = true,
            StrokeWidth = GraphLineThickness,
            StrokeCap = SKStrokeCap.Square,
            StrokeJoin = SKStrokeJoin.Round,
            Style = SKPaintStyle.Stroke
        };

    private static SKColor GetGraphLineColor(TrayGraphDataSource dataSource) =>
        dataSource switch
        {
            TrayGraphDataSource.CPUHighestCore => CPUHighestCoreGraphLineColor,
            TrayGraphDataSource.CPUAverage => CPUGraphLineColor,
            _ => CPUGraphLineColor
        };

    private static SKColor CreateUnderfillColor(SKColor lineColor)
    {
        byte alpha = (byte)Math.Round(
            lineColor.Alpha * GraphUnderfillOpacity,
            MidpointRounding.AwayFromZero);
        return new SKColor(
            Darken(lineColor.Red),
            Darken(lineColor.Green),
            Darken(lineColor.Blue),
            alpha);
    }

    private static byte Darken(byte component) =>
        (byte)Math.Max(val1: 0, component - GraphUnderfillDarkenAmount);

    private static float PercentToY(SKRect graphBounds, double percent) =>
        graphBounds.Bottom - (float)(NormalizePercent(percent) / 100.0) * graphBounds.Height;

    private static double NormalizePercent(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, min: 0, max: 100) : 0;

    public void Dispose()
    {
        lock (_gate)
            _disposed = true;
    }
}
