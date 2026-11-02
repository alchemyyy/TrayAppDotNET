using System.Xml.Serialization;
using TrayAppDotNETCommon.Visuals;

namespace NetworkTrayAppDotNET.Visuals;

[XmlRoot("Theme")]
public sealed class AppTheme : TrayAppDotNETCommon.Visuals.AppTheme
{
    public new static AppTheme Default { get; } = new();

    // Each default reads the current dictionary; Debug reloads update its ThemeColor instances in place
    private static AppThemeResources.NetworkAppThemeAxamlProperties ThemeDefaults =>
        AppThemeResources.Current.AxamlNetworkAppTheme;

    public ThemeColor NetworkConnectedTrayIconColor { get; set; } =
        ThemeDefaults.NetworkConnectedTrayIconColor;

    public ThemeColor NetworkNoInternetTrayIconColor { get; set; } =
        ThemeDefaults.NetworkNoInternetTrayIconColor;

    public ThemeColor NetworkDisconnectedTrayIconColor { get; set; } =
        ThemeDefaults.NetworkDisconnectedTrayIconColor;

    public string GlyphNetworkEthernet { get; set; } = GlyphCatalog.NETWORK_ETHERNET.Text;
    public string GlyphNetworkWifi0 { get; set; } = GlyphCatalog.NETWORK_WIFI_0.Text;
    public string GlyphNetworkWifi1 { get; set; } = GlyphCatalog.NETWORK_WIFI_1.Text;
    public string GlyphNetworkWifi2 { get; set; } = GlyphCatalog.NETWORK_WIFI_2.Text;
    public string GlyphNetworkWifi3 { get; set; } = GlyphCatalog.NETWORK_WIFI_3.Text;
    public string GlyphNetworkWifi4 { get; set; } = GlyphCatalog.NETWORK_WIFI_4.Text;
    public string GlyphNetworkNone { get; set; } = GlyphCatalog.NETWORK_NONE.Text;

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
}
