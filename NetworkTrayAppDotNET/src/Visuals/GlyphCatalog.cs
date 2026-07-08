using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;
using TrayAppDotNETCommon.Visuals;

namespace NetworkTrayAppDotNET.Visuals;

/// <summary>
/// Glyph objects shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    private static readonly Lazy<GlyphCatalogResources> Resources = new(static () => new GlyphCatalogResources());

    public new static Glyph WARNING => Glyph("Warning");

    public new const string SEGOE_FLUENT_ICONS = TADNFontResolver.SegoeFluentIconsFamilyName;
    public new const string SEGOE_MDL2_ASSETS = TADNFontResolver.SegoeMDL2AssetsFamilyName;

    public static Glyph NETWORK_ETHERNET => Glyph("NetworkEthernet");
    public static Glyph NETWORK_WIFI_0 => Glyph("NetworkWifi0");
    public static Glyph NETWORK_WIFI_1 => Glyph("NetworkWifi1");
    public static Glyph NETWORK_WIFI_2 => Glyph("NetworkWifi2");
    public static Glyph NETWORK_WIFI_3 => Glyph("NetworkWifi3");
    public static Glyph NETWORK_WIFI_4 => Glyph("NetworkWifi4");
    public static Glyph NETWORK_NONE => Glyph("NetworkNone");

    private static Glyph Glyph(string name) => Resources.Value.Glyph(name);
}
