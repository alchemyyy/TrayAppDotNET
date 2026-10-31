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
        "1", "2", "3", "4", "5", "6", "7", "8", "9"
    ];

    public string GetGlyph(int index, string? customGlyph = null)
    {
        if (!string.IsNullOrEmpty(customGlyph)) return customGlyph;
        if (index >= 0 && index < DefaultGlyphs.Count) return DefaultGlyphs[index];
        return (index + 1).ToString();
    }
}

[XmlRoot("Theme")]
public sealed class AppTheme : CommonAppTheme
{
    public new static AppTheme Default { get; } = new();

    // Each default reads the current dictionary; Debug reloads update its ThemeColor instances in place
    private static AppThemeResources.BrightnessAppThemeAxamlProperties ThemeDefaults =>
        AppThemeResources.Current.AxamlBrightnessAppTheme;

    public ThemeColor DisplayIdentifierBackground { get; set; } =
        ThemeDefaults.DisplayIdentifierBackground;

    public ThemeColor DisplayIdentifierBorder { get; set; } =
        ThemeDefaults.DisplayIdentifierBorder;

    public ThemeColor DisplayIdentifierShadow { get; set; } =
        ThemeDefaults.DisplayIdentifierShadow;

    public ThemeColor DisplayIdentifierForeground { get; set; } =
        ThemeDefaults.DisplayIdentifierForeground;

    public ThemeColor EnvironmentalBrightnessCurve { get; set; } =
        ThemeDefaults.EnvironmentalBrightnessCurve;

    public ThemeColor EnvironmentalNightLightCurve { get; set; } =
        ThemeDefaults.EnvironmentalNightLightCurve;

    public ThemeColor EnvironmentalCurrentTime { get; set; } =
        ThemeDefaults.EnvironmentalCurrentTime;

    public ThemeColor EnvironmentalTwilightBackdrop { get; set; } =
        ThemeDefaults.EnvironmentalTwilightBackdrop;

    public ThemeColor EnvironmentalNightBackdrop { get; set; } =
        ThemeDefaults.EnvironmentalNightBackdrop;

    public ThemeColor EnvironmentalGridLine { get; set; } =
        ThemeDefaults.EnvironmentalGridLine;

    public ThemeColor CurveDisabledBandOverlay { get; set; } =
        ThemeDefaults.CurveDisabledBandOverlay;

    public ThemeColor EnvironmentalPreviewTint { get; set; } =
        ThemeDefaults.EnvironmentalPreviewTint;

    public ThemeColor EnvironmentalMapPin { get; set; } =
        ThemeDefaults.EnvironmentalMapPin;

    private int? _environmentalMapHudBackdropAlphaOverride;

    [XmlAttribute]
    public int EnvironmentalMapHudBackdropAlpha
    {
        get => _environmentalMapHudBackdropAlphaOverride
               ?? ThemeDefaults.EnvironmentalMapHudBackdropAlpha.A;
        set
        {
            int defaultAlpha = ThemeDefaults.EnvironmentalMapHudBackdropAlpha.A;
            _environmentalMapHudBackdropAlphaOverride = value == defaultAlpha ? null : value;
        }
    }

    public string GlyphMonitor { get; set; } = GlyphCatalog.MONITOR.Text;
    public string GlyphDisplaySettings { get; set; } = GlyphCatalog.DISPLAY_SETTINGS.Text;
    public string GlyphProfileSave { get; set; } = GlyphCatalog.PROFILE_SAVE.Text;
    public string GlyphProfileIndicator { get; set; } = GlyphCatalog.PROFILE_INDICATOR.Text;

    public ProfileButtonSettings ProfileButtons { get; set; } = new();

    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, path2: "theme.xml");
    }

    public static AppTheme LoadOrDefault(string filePath) =>
        CommonAppTheme.LoadOrDefault<AppTheme>(
            filePath,
            static message => TADNLog.Log($"AppTheme.LoadOrDefault: {message}"));

    public static AppTheme Load(string filePath) =>
        CommonAppTheme.Load<AppTheme>(filePath);

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
        byte alpha = (byte)Math.Clamp(EnvironmentalMapHudBackdropAlpha, min: 0, max: 255);
        return Color.FromArgb(alpha, background.R, background.G, background.B);
    }
}
