using SkiaSharp;

namespace TaskManagerTrayAppDotNET.UI.Tray;

internal sealed record TaskManagerTrayIconRenderInput(
    TrayGraphStyle Style,
    double[] Values);

/// <summary>Renders the official-style system utilization graph used by the tray icon.</summary>
internal sealed class TaskManagerTrayIcon : IDisposable
{
    public const int HistoryCapacity = 16;

    // AXAML hot-reload exception: Tray icon rendering runs on the background render queue and
    // cannot safely read mutable Avalonia resource dictionaries
    private const int CurveSamplesPerPixel = 4;
    private const float GridOpacity = 0.27f;

    private static readonly SKColor BackgroundColor = new(red: 46, green: 48, blue: 47);
    private static readonly SKColor GraphFillColor = new(red: 215, green: 216, blue: 212);
    private static readonly SKColor GraphLineColor = new(red: 239, green: 239, blue: 235);

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

        return new TaskManagerTrayIconRenderInput(style, values);
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
        canvas.Clear(BackgroundColor);

        float borderWidth = Math.Max(val1: 1.0f, size / 24.0f);
        SKRect graphBounds = new(
            borderWidth,
            borderWidth,
            size - borderWidth,
            size - borderWidth);

        canvas.Save();
        canvas.ClipRect(graphBounds, antialias: false);
        switch (input.Style)
        {
            case TrayGraphStyle.Current:
                DrawCurrentGraph(canvas, graphBounds, input.Values);
                break;

            case TrayGraphStyle.Marquee:
            default:
                DrawMarqueeGraph(canvas, graphBounds, input.Values);
                break;
        }

        DrawGrid(canvas, graphBounds);
        canvas.Restore();
        DrawBorder(canvas, size, borderWidth);

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        return data.ToArray();
    }

    private static void DrawCurrentGraph(
        SKCanvas canvas,
        SKRect graphBounds,
        IReadOnlyList<double> values)
    {
        double currentPercent = values.Count > 0 ? NormalizePercent(values[^1]) : 0;
        float graphTop = PercentToY(graphBounds, currentPercent);
        using SKPaint fillPaint = new() { Color = GraphFillColor, IsAntialias = false, Style = SKPaintStyle.Fill };
        canvas.DrawRect(
            new SKRect(graphBounds.Left, graphTop, graphBounds.Right, graphBounds.Bottom),
            fillPaint);

        if (currentPercent <= 0) return;
        using SKPaint linePaint = CreateGraphLinePaint();
        canvas.DrawLine(graphBounds.Left, graphTop, graphBounds.Right, graphTop, linePaint);
    }

    private static void DrawMarqueeGraph(
        SKCanvas canvas,
        SKRect graphBounds,
        IReadOnlyList<double> values)
    {
        int curveSampleCount = Math.Max(
            val1: 2,
            (int)Math.Ceiling(graphBounds.Width * CurveSamplesPerPixel) + 1);
        double[] curveSamples = TaskManagerTrayGraphSampler.SampleMarquee(values, curveSampleCount);

        using SKPath areaPath = new();
        using SKPath linePath = new();
        areaPath.MoveTo(graphBounds.Left, graphBounds.Bottom);
        for (int sampleIndex = 0; sampleIndex < curveSamples.Length; sampleIndex++)
        {
            float normalizedX = sampleIndex / (float)(curveSamples.Length - 1);
            float graphX = graphBounds.Left + normalizedX * graphBounds.Width;
            float graphY = PercentToY(graphBounds, curveSamples[sampleIndex]);
            if (sampleIndex == 0)
            {
                areaPath.LineTo(graphX, graphY);
                linePath.MoveTo(graphX, graphY);
                continue;
            }

            areaPath.LineTo(graphX, graphY);
            linePath.LineTo(graphX, graphY);
        }

        areaPath.LineTo(graphBounds.Right, graphBounds.Bottom);
        areaPath.Close();

        using SKPaint fillPaint = new() { Color = GraphFillColor, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawPath(areaPath, fillPaint);

        using SKPaint linePaint = CreateGraphLinePaint();
        canvas.DrawPath(linePath, linePaint);
    }

    private static void DrawGrid(SKCanvas canvas, SKRect graphBounds)
    {
        using SKPaint gridPaint = new()
        {
            Color = GridColor, IsAntialias = false, StrokeWidth = 1, Style = SKPaintStyle.Stroke
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

    private static void DrawBorder(SKCanvas canvas, int size, float borderWidth)
    {
        using SKPaint borderPaint = new()
        {
            Color = BorderColor, IsAntialias = false, StrokeWidth = borderWidth, Style = SKPaintStyle.Stroke
        };
        float inset = borderWidth / 2.0f;
        canvas.DrawRect(new SKRect(inset, inset, size - inset, size - inset), borderPaint);
    }

    private static SKPaint CreateGraphLinePaint() =>
        new()
        {
            Color = GraphLineColor,
            IsAntialias = true,
            StrokeWidth = 1,
            StrokeCap = SKStrokeCap.Square,
            StrokeJoin = SKStrokeJoin.Round,
            Style = SKPaintStyle.Stroke
        };

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
