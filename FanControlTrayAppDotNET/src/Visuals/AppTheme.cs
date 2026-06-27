using System.Xml.Serialization;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
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

    public ThemeColor FlyoutBackground { get; set; } = new("CECECE", "323232");
    public ThemeColor FlyoutTitleBarBackground { get; set; } = new("E8E8E8", "202020");
    public ThemeColor FanCardBackground { get; set; } = new("FFFFFF", "1a1a1a");
    public ThemeColor GroupCardBackground { get; set; } = new("FFFFFF", "1a1a1a");
    public ThemeColor FlyoutCardBorder { get; set; } = new("4E4E4E", "3A3A3A");
    public ThemeColor CurveEditorGridLine { get; set; } = new("939393");
    public ThemeColor CurveEditorEffectiveCurve { get; set; } = new("EBC051");
    public ThemeColor CurveEditorDisabledBand { get; set; } = new("2AFFFFFF");

    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, "theme.xml");
    }

    public static AppTheme LoadOrDefault(string filePath) =>
        LoadOrDefault<AppTheme>(filePath);

    public static AppTheme Load(string filePath) =>
        Load<AppTheme>(filePath);

    public void SaveToDefaultPath() => Save(GetDefaultPath());

    public static WindowIcon? LoadAppIcon()
    {
        try
        {
            string filePath = Path.Combine(AppContext.BaseDirectory, Constants.AppIconRelativePath);
            if (File.Exists(filePath)) return new WindowIcon(filePath);

            Uri resource = new(Constants.AppIconResourceUri);
            return new WindowIcon(AssetLoader.Open(resource));
        }
        catch (Exception ex)
        {
            TADNLog.Log($"Fan AppTheme.LoadAppIcon: {ex.Message}");
            return null;
        }
    }

    public static NativeIcon? LoadAppNativeIcon()
    {
        try
        {
            string filePath = Path.Combine(AppContext.BaseDirectory, Constants.AppIconRelativePath);
            if (File.Exists(filePath))
                return NativeIcon.FromIco(File.ReadAllBytes(filePath), 32);

            Uri resource = new(Constants.AppIconResourceUri);
            using Stream stream = AssetLoader.Open(resource);
            using MemoryStream memory = new();
            stream.CopyTo(memory);
            return NativeIcon.FromIco(memory.ToArray(), 32);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"Fan AppTheme.LoadAppNativeIcon: {ex.Message}");
            return null;
        }
    }

    public static bool ResolveEffectiveIsLightTheme(FanSettings? settings)
    {
        bool systemIsLight = AppServices.Theme?.IsLightTheme ?? Default.IsLightTheme;
        if (settings == null) return systemIsLight;
        return settings.ThemeMode switch
        {
            ThemeMode.Light => true,
            ThemeMode.Dark => false,
            _ => systemIsLight,
        };
    }

    public Color ResolveForeground(FanSettings? settings, bool isLightTheme)
    {
        if (settings?.TextColor.Resolve(isLightTheme) is { } color) return color;
        return Foreground.For(isLightTheme);
    }

    public Color ResolveBackground(FanSettings? settings, bool isLightTheme)
    {
        if (settings?.BackgroundColor.Resolve(isLightTheme) is { } color) return color;
        return Background.For(isLightTheme);
    }

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
