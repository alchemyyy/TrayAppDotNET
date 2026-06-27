using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;

namespace NetworkTrayAppDotNET.Visuals;

/// <summary>
/// Segoe Fluent Icons codepoint strings shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : TrayAppDotNETCommon.Visuals.GlyphCatalog
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

    public new const string NETWORK_ETHERNET = CommonGlyphCatalog.NETWORK_ETHERNET;
    public new const string NETWORK_WIFI_0 = CommonGlyphCatalog.NETWORK_WIFI_0;
    public new const string NETWORK_WIFI_1 = CommonGlyphCatalog.NETWORK_WIFI_1;
    public new const string NETWORK_WIFI_2 = CommonGlyphCatalog.NETWORK_WIFI_2;
    public new const string NETWORK_WIFI_3 = CommonGlyphCatalog.NETWORK_WIFI_3;
    public new const string NETWORK_WIFI_4 = CommonGlyphCatalog.NETWORK_WIFI_4;
    public new const string NETWORK_NONE = CommonGlyphCatalog.NETWORK_NONE;
}
