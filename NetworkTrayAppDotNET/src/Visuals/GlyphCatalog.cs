using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;
using TrayAppDotNETCommon.Visuals;

namespace NetworkTrayAppDotNET.Visuals;

/// <summary>
/// Glyph objects shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    // ===========================================================================
    // Generic UI glyphs
    // ===========================================================================

    public new static readonly Glyph WARNING = Glyph.Fluent(CommonGlyphCatalog.WARNING);

    public new const string SEGOE_FLUENT_ICONS = TADNFontResolver.SegoeFluentIconsFamilyName;
    public new const string SEGOE_MDL2_ASSETS = TADNFontResolver.SegoeMDL2AssetsFamilyName;

    // ===========================================================================
    // Network tray glyphs
    // ===========================================================================

    public static readonly Glyph NETWORK_ETHERNET = Glyph.SegoeFluent("\uE839"); // Fluent, Ethernet
    public static readonly Glyph NETWORK_WIFI_0 = Glyph.SegoeFluent("\uE871"); // Fluent, SignalNotConnected
    public static readonly Glyph NETWORK_WIFI_1 = Glyph.SegoeFluent("\uE872"); // Fluent, Wifi1
    public static readonly Glyph NETWORK_WIFI_2 = Glyph.SegoeFluent("\uE873"); // Fluent, Wifi2
    public static readonly Glyph NETWORK_WIFI_3 = Glyph.SegoeFluent("\uE874"); // Fluent, Wifi3
    public static readonly Glyph NETWORK_WIFI_4 = Glyph.SegoeFluent("\uE701"); // Fluent, Wifi
    public static readonly Glyph NETWORK_NONE = Glyph.SegoeFluent("\uF384"); // Fluent, NetworkOffline
}
