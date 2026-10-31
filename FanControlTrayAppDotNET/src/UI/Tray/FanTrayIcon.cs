using Avalonia.Media;
using Avalonia.Platform;
using SkiaSharp;

namespace FanControlTrayAppDotNET.UI.Tray;

/// <summary>Fan's tray glyph, drawn from the app's own icon font through the Common glyph renderer.</summary>
internal sealed class FanTrayIcon(AppTheme? theme) : ITrayIconRenderer<TrayIconRenderInput>
{
    private readonly TrayIconRenderer _renderer = new(new TrayIconRenderOptions
    {
        TypefaceFactory = LoadFanTypeface,
        FontEdging = SKFontEdging.Antialias,
        Subpixel = false,
        Log = TADNLog.Log
    });

    private readonly AppTheme _theme = theme ?? AppTheme.Default;

    public bool IsLightTheme { get; set; }
    public Color? TrayIconColorOverride { get; set; }

    public TrayIconRenderInput CreateRenderInput() =>
        new(
            new TrayIconGlyphLayer(BackdropGlyph: null, GlyphCatalog.FAN.Text),
            TrayIconColorOverride ?? _theme.Foreground.For(IsLightTheme),
            BackdropOpacity: 0);

    public NativeIcon RenderIcon(TrayIconRenderInput input, int iconSize) => _renderer.Render(input, iconSize);

    private static SKTypeface LoadFanTypeface()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, Constants.FanFontRelativePath);
        if (File.Exists(filePath)) return SKTypeface.FromFile(filePath);

        using Stream stream = AssetLoader.Open(new Uri(Constants.FanFontResourceUri));
        return SKTypeface.FromStream(stream);
    }

    public void Dispose() => _renderer.Dispose();
}
