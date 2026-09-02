using SkiaSharp;

namespace TaskManagerTrayAppDotNET.UI.Tray;

internal sealed record TaskManagerTrayIconRenderInput(
    TrayGraphStyle Style,
    TrayGraphDataSource DataSource,
    double[] Values);

/// <summary>Renders the official-style system utilization graph used by the tray icon.</summary>
internal sealed class TaskManagerTrayIcon : IDisposable
{
    public const int HistoryCapacity = 16;

    // AXAML hot-reload exception: Tray icon rendering runs on the background render queue and
    // cannot safely read mutable Avalonia resource dictionaries. Keep these values aligned with
    // the Performance graph resources in TaskManagerWindow.axaml
    private const float CornerRadiusScale = 3.0f / 16.0f;
    private const float GraphLineThickness = 0.85f;
    private const float GraphUnderfillOpacity = 0.12f;
    private const int GraphUnderfillDarkenAmount = 20;
    private const float GridLineThickness = 0.75f;
    private const float GridOpacity = 92.0f / byte.MaxValue;

    private static readonly SKColor BackgroundColor = new(red: 25, green: 25, blue: 25);
    private static readonly SKColor CPUGraphLineColor = new(red: 50, green: 181, blue: 229);
    private static readonly SKColor MemoryGraphLineColor = new(red: 88, green: 131, blue: 208);

    private static readonly SKColor GridColor = new(red: 177, green: 180, blue: 178,
        (byte)Math.Round(byte.MaxValue * GridOpacity));

    private static readonly SKColor BorderColor = new(red: 137, green: 140, blue: 138);

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
        TrayGraphDataSource dataSource)
    {
        int valueCount = Math.Max(val1: 1, _historyCount);
        double[] values = new double[valueCount];
        for (int valueIndex = 0; valueIndex < _historyCount; valueIndex++)
        {
            int historyIndex = (_historyStart + valueIndex) % _history.Length;
            values[valueIndex] = _history[historyIndex].Select(dataSource);
        }

        return new TaskManagerTrayIconRenderInput(style, dataSource, values);
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

        float borderWidth = Math.Max(val1: 1.0f, size / 24.0f);
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
        using SKPaint linePaint = CreateGraphLinePaint(lineColor);
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
        using SKPaint borderPaint = new()
        {
            Color = BorderColor,
            IsAntialias = true,
            StrokeWidth = borderWidth,
            StrokeJoin = SKStrokeJoin.Round,
            Style = SKPaintStyle.Stroke
        };
        float inset = borderWidth / 2.0f;
        float borderCornerRadius = Math.Max(val1: borderWidth, cornerRadius - inset);
        canvas.DrawRoundRect(
            new SKRect(inset, inset, size - inset, size - inset),
            borderCornerRadius,
            borderCornerRadius,
            borderPaint);
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
            TrayGraphDataSource.Memory => MemoryGraphLineColor,
            TrayGraphDataSource.CPUAverage or TrayGraphDataSource.CPUHighestCore =>
                CPUGraphLineColor,
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
