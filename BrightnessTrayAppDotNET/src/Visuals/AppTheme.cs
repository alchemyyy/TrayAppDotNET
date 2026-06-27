using System.Xml.Serialization;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Tray;
using TrayAppDotNETCommon.Visuals;
using CommonAppTheme = TrayAppDotNETCommon.Visuals.AppTheme;

namespace BrightnessTrayAppDotNET.Visuals;

public sealed class ProfileButtonSettings
{
    [XmlAttribute]
    public int ButtonCount { get; set; } = 4;

    [XmlArrayItem("Glyph")]
    public List<string> DefaultGlyphs { get; set; } =
    [
        "1", "2", "3", "4", "5", "6", "7", "8", "9",
    ];

    public string GetGlyph(int index, string? customGlyph = null)
    {
        if (!string.IsNullOrEmpty(customGlyph)) return customGlyph;
        if (index >= 0 && index < DefaultGlyphs.Count) return DefaultGlyphs[index];
        return (index + 1).ToString();
    }
}

[XmlRoot("Theme")]
public sealed class AppTheme : TrayAppDotNETCommon.Visuals.AppTheme
{
    public new static AppTheme Default { get; } = new();

    public ThemeColor DisplayIdentifierBackground { get; set; } = new("E6202020");
    public ThemeColor DisplayIdentifierBorder { get; set; } = new("33FFFFFF");
    public ThemeColor DisplayIdentifierShadow { get; set; } = new("000000");
    public ThemeColor DisplayIdentifierForeground { get; set; } = new("FFFFFF");

    public ThemeColor EnvironmentalBrightnessCurve { get; set; } = new("7B949B", "DCF3FA");
    public ThemeColor EnvironmentalNightLightCurve { get; set; } = new("FDCB43", "fedb7c");
    public ThemeColor EnvironmentalCurrentTime { get; set; } = new("4E4E4E", "ffffff");
    public ThemeColor EnvironmentalTwilightBackdrop { get; set; } = new("40FF8C00");
    public ThemeColor EnvironmentalNightBackdrop { get; set; } = new("404C5A78");
    public ThemeColor EnvironmentalGridLine { get; set; } = new("929292", "939393");
    public ThemeColor CurveDisabledBandOverlay { get; set; } = new("08FFFFFF");
    public ThemeColor EnvironmentalPreviewTint { get; set; } = new("10FFFFFF");
    public ThemeColor EnvironmentalMapPin { get; set; } = new("E03A3A");

    [XmlAttribute]
    public int EnvironmentalMapHudBackdropAlpha { get; set; } = 232;

    public string GlyphMonitor { get; set; } = GlyphCatalog.MONITOR;
    public string GlyphDisplaySettings { get; set; } = GlyphCatalog.DISPLAY_SETTINGS;
    public string GlyphProfileSave { get; set; } = GlyphCatalog.PROFILE_SAVE;
    public string GlyphProfileIndicator { get; set; } = GlyphCatalog.PROFILE_INDICATOR;

    public ProfileButtonSettings ProfileButtons { get; set; } = new();

    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, "theme.xml");
    }

    public static AppTheme LoadOrDefault(string filePath) =>
        CommonAppTheme.LoadOrDefault<AppTheme>(filePath);

    public static AppTheme Load(string filePath) =>
        CommonAppTheme.Load<AppTheme>(filePath);

    public void SaveToDefaultPath() => Save(GetDefaultPath());

    public static WindowIcon? LoadAppIcon()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, Constants.AppIconFileName);
        if (File.Exists(filePath)) return new WindowIcon(filePath);

        Uri uri = new(Constants.AppIconResourceUri);
        if (AssetLoader.Exists(uri))
        {
            using Stream stream = AssetLoader.Open(uri);
            return new WindowIcon(stream);
        }

        return null;
    }

    public static NativeIcon? LoadAppNativeIcon()
    {
        try
        {
            int size = TrayAppDotNETTrayIconMetrics.GetTaskbarSmallIconSize();
            string filePath = Path.Combine(AppContext.BaseDirectory, Constants.AppIconFileName);
            if (File.Exists(filePath)) return NativeIcon.FromIco(File.ReadAllBytes(filePath), size);

            Uri uri = new(Constants.AppIconResourceUri);
            if (AssetLoader.Exists(uri))
            {
                using Stream stream = AssetLoader.Open(uri);
                using MemoryStream memory = new();
                stream.CopyTo(memory);
                return NativeIcon.FromIco(memory.ToArray(), size);
            }
        }
        catch (Exception ex)
        {
            WPFLog.Log($"AppTheme.LoadAppNativeIcon: {ex.Message}");
        }

        return null;
    }

    public static bool ResolveEffectiveIsLightTheme(AppSettings? settings)
    {
        bool systemIsLight = AppServices.Theme?.IsLightTheme ?? false;
        if (settings == null) return systemIsLight;
        return settings.ThemeMode switch
        {
            ThemeMode.Light => true,
            ThemeMode.Dark => false,
            _ => systemIsLight,
        };
    }

    public Color ResolveForeground(AppSettings? settings, bool isLightTheme)
    {
        if (settings?.TextColor.Resolve(isLightTheme) is { } color) return color;
        return Foreground.For(isLightTheme);
    }

    public Color ResolveBackground(AppSettings? settings, bool isLightTheme)
    {
        if (settings?.BackgroundColor.Resolve(isLightTheme) is { } color) return color;
        return Background.For(isLightTheme);
    }

    public Color ResolveFooterBackground(AppSettings? settings, bool isLightTheme)
    {
        if (settings?.FooterBackgroundColor.Resolve(isLightTheme) is { } color) return color;
        return FooterBackground.For(isLightTheme);
    }

    public Color ResolveEnvironmentalBrightnessCurve(AppSettings? settings, bool isLightTheme)
    {
        if (settings?.EnvironmentalBrightnessCurveColor.Resolve(isLightTheme) is { } color) return color;
        return EnvironmentalBrightnessCurve.For(isLightTheme);
    }

    public Color ResolveEnvironmentalNightLightCurve(AppSettings? settings, bool isLightTheme)
    {
        if (settings?.EnvironmentalNightLightCurveColor.Resolve(isLightTheme) is { } color) return color;
        return EnvironmentalNightLightCurve.For(isLightTheme);
    }

    public Color ResolveEnvironmentalCurrentTime(AppSettings? settings, bool isLightTheme)
    {
        if (settings?.EnvironmentalCurrentTimeColor.Resolve(isLightTheme) is { } color) return color;
        return EnvironmentalCurrentTime.For(isLightTheme);
    }

    public Color ResolveEnvironmentalTwilightBackdrop(AppSettings? settings, bool isLightTheme) =>
        settings?.EnvironmentalTwilightBackdropColor.Resolve(isLightTheme)
        ?? EnvironmentalTwilightBackdrop.For(isLightTheme);

    public Color ResolveEnvironmentalNightBackdrop(AppSettings? settings, bool isLightTheme) =>
        settings?.EnvironmentalNightBackdropColor.Resolve(isLightTheme)
        ?? EnvironmentalNightBackdrop.For(isLightTheme);

    public Color ResolveEnvironmentalGridLine(AppSettings? settings, bool isLightTheme)
    {
        if (settings?.EnvironmentalGridLineColor.Resolve(isLightTheme) is { } color) return color;
        return EnvironmentalGridLine.For(isLightTheme);
    }

    public Color ResolveEnvironmentalMapHudBackdrop(AppSettings? settings, bool isLightTheme)
    {
        Color background = ResolveBackground(settings, isLightTheme);
        byte alpha = (byte)Math.Clamp(EnvironmentalMapHudBackdropAlpha, 0, 255);
        return Color.FromArgb(alpha, background.R, background.G, background.B);
    }

}
