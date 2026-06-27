using System.Xml.Serialization;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using NetworkTrayAppDotNET.Models;
using TrayAppDotNETCommon.Visuals;

namespace NetworkTrayAppDotNET.Visuals;

[XmlRoot("Theme")]
public sealed class AppTheme : TrayAppDotNETCommon.Visuals.AppTheme
{
    public new static AppTheme Default { get; } = new();

    public ThemeColor NetworkConnectedTrayIconColor { get; set; } = new("000000", "FFFFFF");
    public ThemeColor NetworkNoInternetTrayIconColor { get; set; } = new("996600", "FFB900");
    public ThemeColor NetworkDisconnectedTrayIconColor { get; set; } = new("666666", "808080");

    public string GlyphNetworkEthernet { get; set; } = GlyphCatalog.NETWORK_ETHERNET;
    public string GlyphNetworkWifi0 { get; set; } = GlyphCatalog.NETWORK_WIFI_0;
    public string GlyphNetworkWifi1 { get; set; } = GlyphCatalog.NETWORK_WIFI_1;
    public string GlyphNetworkWifi2 { get; set; } = GlyphCatalog.NETWORK_WIFI_2;
    public string GlyphNetworkWifi3 { get; set; } = GlyphCatalog.NETWORK_WIFI_3;
    public string GlyphNetworkWifi4 { get; set; } = GlyphCatalog.NETWORK_WIFI_4;
    public string GlyphNetworkNone { get; set; } = GlyphCatalog.NETWORK_NONE;

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
            TADNLog.Log($"AppTheme.LoadAppNativeIcon: {ex.Message}");
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
}
