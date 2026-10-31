using TrayAppDotNETCommon.Visuals;
using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;

namespace NetworkTrayAppDotNET.Visuals;

/// <summary>
/// Glyph objects shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    public static Glyph NETWORK_ETHERNET => Glyphs.NetworkEthernet;
    public static Glyph NETWORK_WIFI_0 => Glyphs.NetworkWifi0;
    public static Glyph NETWORK_WIFI_1 => Glyphs.NetworkWifi1;
    public static Glyph NETWORK_WIFI_2 => Glyphs.NetworkWifi2;
    public static Glyph NETWORK_WIFI_3 => Glyphs.NetworkWifi3;
    public static Glyph NETWORK_WIFI_4 => Glyphs.NetworkWifi4;
    public static Glyph NETWORK_NONE => Glyphs.NetworkNone;

    // Each read goes through the current dictionary, so a Debug catalog reload reaches the next read
    private static GlyphCatalogResources.GlyphCatalogAxamlProperties Glyphs =>
        GlyphCatalogResources.Current.AxamlGlyphCatalog;
}
