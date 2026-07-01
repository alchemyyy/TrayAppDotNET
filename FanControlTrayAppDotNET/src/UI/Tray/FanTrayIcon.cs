using Avalonia.Media;
using Avalonia.Platform;
using SkiaSharp;

namespace FanControlTrayAppDotNET.UI;

internal sealed class FanTrayIcon : IDisposable
{
    private readonly Lock _gate = new();
    private readonly AppTheme _theme;
    private SKTypeface? _fanTypeface;
    private NativeIcon? _currentIcon;
    private bool _isDirty = true;

    public FanTrayIcon(AppTheme? theme) => _theme = theme ?? AppTheme.Default;

    public bool IsLightTheme
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            _isDirty = true;
        }
    }

    public Color? TrayIconColorOverride
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            _isDirty = true;
        }
    }

    public void InvalidateCache() => _isDirty = true;

    public NativeIcon? CreateIcon()
    {
        if (!TryCreateRenderInput(out FanTrayIconRenderInput? input) || input == null) return null;

        NativeIcon? icon = RenderIcon(input);
        if (icon == null) return null;

        lock (_gate)
        {
            NativeIcon? old = _currentIcon;
            _currentIcon = icon;
            old?.Dispose();
            return _currentIcon;
        }
    }

    public bool TryCreateRenderInput(out FanTrayIconRenderInput? input)
    {
        input = null;
        if (!_isDirty) return false;

        _isDirty = false;
        input = new FanTrayIconRenderInput(ResolveColor());
        return true;
    }

    public NativeIcon? RenderIcon(FanTrayIconRenderInput input)
    {
        try
        {
            lock (_gate)
            {
                int iconSize = TrayAppDotNETTrayIconMetrics.GetTaskbarSmallIconSize();
                byte[] png = RenderPng(iconSize, input.ForegroundColor);
                return NativeIcon.FromIconImage(png, iconSize);
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"FanTrayIcon.CreateIcon: {ex.Message}");
            lock (_gate)
                return _currentIcon?.Clone() ?? AppTheme.LoadAppNativeIcon();
        }
    }

    private byte[] RenderPng(int size, Color foregroundColor)
    {
        SKImageInfo info = new(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKBitmap bitmap = new(info);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        using SKFont font = new(FanTypeface, size) { Edging = SKFontEdging.Antialias, Subpixel = false, };
        using SKPaint paint = new()
        {
            IsAntialias = true,
            Color = new SKColor(foregroundColor.R, foregroundColor.G, foregroundColor.B, foregroundColor.A),
        };

        string glyph = GlyphCatalog.FAN;
        float advance = font.MeasureText(glyph, out SKRect bounds, paint);
        font.GetFontMetrics(out SKFontMetrics metrics);
        float lineHeight = metrics.Descent - metrics.Ascent;
        float x = (size - advance) / 2f;
        float y = (size - lineHeight) / 2f - metrics.Ascent;
        canvas.DrawText(glyph, x, y, font, paint);

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private SKTypeface FanTypeface => _fanTypeface ??= LoadFanTypeface();

    private static SKTypeface LoadFanTypeface()
    {
        try
        {
            string filePath = Path.Combine(AppContext.BaseDirectory, Constants.FanFontRelativePath);
            if (File.Exists(filePath))
                return SKTypeface.FromFile(filePath);

            using Stream stream = AssetLoader.Open(new Uri(Constants.FanFontResourceUri));
            return SKTypeface.FromStream(stream);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"FanTrayIcon.LoadFanTypeface: {ex.Message}");
            return SKTypeface.Default;
        }
    }

    private Color ResolveColor() =>
        TrayIconColorOverride ?? _theme.Foreground.For(IsLightTheme);

    public void Dispose()
    {
        lock (_gate)
        {
            _currentIcon?.Dispose();
            _currentIcon = null;
            _fanTypeface?.Dispose();
            _fanTypeface = null;
        }
    }
}

internal sealed record FanTrayIconRenderInput(Color ForegroundColor);
