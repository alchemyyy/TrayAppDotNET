using System.Xml.Serialization;
using Avalonia.Media;
using TrayAppDotNETCommon.Visuals;
using FanSettings = FanControlTrayAppDotNET.Models.AppSettings;

namespace FanControlTrayAppDotNET.Visuals;

/// <summary>
/// Fan-specific theme defaults layered on top of the shared TrayAppDotNET theme.
/// </summary>
[XmlRoot("Theme")]
public sealed class AppTheme : TrayAppDotNETCommon.Visuals.AppTheme
{
    public new static AppTheme Default { get; } = new();

    // Each default reads the current dictionary; Debug reloads update its ThemeColor instances in place
    private static AppThemeResources.FanAppThemeAxamlProperties ThemeDefaults =>
        AppThemeResources.Current.AxamlFanAppTheme;

    public ThemeColor FlyoutBackground { get; set; } = ThemeDefaults.FlyoutBackground;

    public ThemeColor FlyoutTitleBarBackground { get; set; } =
        ThemeDefaults.FlyoutTitleBarBackground;

    public ThemeColor FanCardBackground { get; set; } =
        ThemeDefaults.FanCardBackground;

    public ThemeColor GroupCardBackground { get; set; } =
        ThemeDefaults.GroupCardBackground;

    public ThemeColor FlyoutCardBorder { get; set; } = ThemeDefaults.FlyoutCardBorder;

    public ThemeColor CurveEditorGridLine { get; set; } =
        ThemeDefaults.CurveEditorGridLine;

    public ThemeColor CurveEditorEffectiveCurve { get; set; } =
        ThemeDefaults.CurveEditorEffectiveCurve;

    public ThemeColor CurveEditorDisabledBand { get; set; } =
        ThemeDefaults.CurveEditorDisabledBand;

    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, path2: "theme.xml");
    }

    public static AppTheme LoadOrDefault(string filePath) =>
        LoadOrDefault<AppTheme>(filePath, static message => TADNLog.Log($"AppTheme.LoadOrDefault: {message}"));

    public static AppTheme Load(string filePath) =>
        Load<AppTheme>(filePath);

    public Color ResolveFlyoutBackground(FanSettings? settings, bool isLightTheme)
    {
        if (settings?.FlyoutBackgroundColor.Resolve(isLightTheme) is { } color) return color;
        return FlyoutBackground.For(isLightTheme);
    }

    public Color ResolveFlyoutTitleBarBackground(FanSettings? settings, bool isLightTheme)
    {
        if (settings?.FlyoutTitleBarBackgroundColor.Resolve(isLightTheme) is { } color) return color;
        return FlyoutTitleBarBackground.For(isLightTheme);
    }

    public Color ResolveFanCardBackground(FanSettings? settings, bool isLightTheme)
    {
        if (settings?.FanCardBackgroundColor.Resolve(isLightTheme) is { } color) return color;
        return FanCardBackground.For(isLightTheme);
    }

    public Color ResolveGroupCardBackground(FanSettings? settings, bool isLightTheme)
    {
        if (settings?.GroupCardBackgroundColor.Resolve(isLightTheme) is { } color) return color;
        return GroupCardBackground.For(isLightTheme);
    }

    public Color ResolveFlyoutCardBorder(FanSettings? settings, bool isLightTheme)
    {
        if (settings?.CardBorderColor.Resolve(isLightTheme) is { } color) return color;
        return FlyoutCardBorder.For(isLightTheme);
    }
}
