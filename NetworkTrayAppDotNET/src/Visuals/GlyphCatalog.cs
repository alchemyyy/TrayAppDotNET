using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;

namespace NetworkTrayAppDotNET.Visuals;

/// <summary>
/// Segoe Fluent Icons codepoint strings shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    // ===========================================================================
    // Generic UI glyphs
    // ===========================================================================

    public new const string WARNING = CommonGlyphCatalog.WARNING;

    public new const string SEGOE_FLUENT_ICONS = CommonGlyphCatalog.SEGOE_FLUENT_ICONS;
    public new const string SEGOE_MDL2_ASSETS = CommonGlyphCatalog.SEGOE_MDL2_ASSETS;

    // ===========================================================================
    // Network tray glyphs
    // ===========================================================================

    public const string NETWORK_ETHERNET = "\uE839"; // Fluent, Ethernet
    public const string NETWORK_WIFI_0 = "\uE871"; // Fluent, SignalNotConnected
    public const string NETWORK_WIFI_1 = "\uE872"; // Fluent, Wifi1
    public const string NETWORK_WIFI_2 = "\uE873"; // Fluent, Wifi2
    public const string NETWORK_WIFI_3 = "\uE874"; // Fluent, Wifi3
    public const string NETWORK_WIFI_4 = "\uE701"; // Fluent, Wifi
    public const string NETWORK_NONE = "\uF384"; // Fluent, NetworkOffline
}
