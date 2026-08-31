using TrayAppDotNETCommon.Visuals;
using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;

namespace NetworkTrayAppDotNET.Visuals;

/// <summary>
/// Glyph objects shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
#if DEBUG
    private static readonly GlyphCatalogHotReloadStore<GlyphCatalogResources> Resources =
        GlyphCatalogHotReloadStore<GlyphCatalogResources>.Create(
            catalogName: "Network",
            static () => new GlyphCatalogResources());
#else
    private static readonly Lazy<GlyphCatalogResources> Resources = new(static () => new GlyphCatalogResources());
#endif

    public new static Glyph WARNING => Glyph("Warning");
    public static Glyph CLOSE => CHROME_CLOSE;

    public new const string SEGOE_FLUENT_ICONS = TADNFontResolver.SegoeFluentIconsFamilyName;
    public new const string SEGOE_MDL2_ASSETS = TADNFontResolver.SegoeMDL2AssetsFamilyName;

    public static Glyph NETWORK_ETHERNET => Glyph("NetworkEthernet");
    public static Glyph NETWORK_WIFI_0 => Glyph("NetworkWifi0");
    public static Glyph NETWORK_WIFI_1 => Glyph("NetworkWifi1");
    public static Glyph NETWORK_WIFI_2 => Glyph("NetworkWifi2");
    public static Glyph NETWORK_WIFI_3 => Glyph("NetworkWifi3");
    public static Glyph NETWORK_WIFI_4 => Glyph("NetworkWifi4");
    public static Glyph NETWORK_NONE => Glyph("NetworkNone");

    private static Glyph Glyph(string name)
    {
#if DEBUG
        return Resources.Current.Glyph(name);
#else
        return Resources.Value.Glyph(name);
#endif
    }
}
