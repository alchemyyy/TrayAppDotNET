namespace TrayAppDotNETCommon.Visuals;

/// <summary>
/// Identifies a TrayAppDotNET glyph font family.
/// </summary>
public enum TADNFont
{
    SegoeUI,
    SegoeFluentIcons,
    SegoeMDL2Assets,
    SegoeFluentIconsThenMDL2Assets,

    /// <summary>The app's own icon font, registered with <see cref="TADNFontResolver.RegisterAppFont"/>.</summary>
    AppFont
}
