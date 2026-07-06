using Avalonia.Media;

namespace TrayAppDotNETCommon.Visuals;

/// <summary>
/// Resolves TrayAppDotNET font identities into Avalonia font families.
/// </summary>
public static class TADNFontResolver
{
    public const string SegoeUIFamilyName = "Segoe UI Variable, Segoe UI";
    public const string SegoeFluentIconsFamilyName = "Segoe Fluent Icons";
    public const string SegoeMDL2AssetsFamilyName = "Segoe MDL2 Assets";
    public const string FanFontFamilyName = "avares://FanControlTrayAppDotNET/Visuals/FanFont.ttf#Untitled1";

    /// <summary>
    /// Returns the font-family string for a glyph font identity.
    /// </summary>
    public static string ResolveFontFamilyName(TADNFont font) =>
        font switch
        {
            TADNFont.SegoeUI => SegoeUIFamilyName,
            TADNFont.SegoeFluentIcons => SegoeFluentIconsFamilyName,
            TADNFont.SegoeMDL2Assets => SegoeMDL2AssetsFamilyName,
            TADNFont.SegoeFluentIconsThenMDL2Assets =>
                $"{SegoeFluentIconsFamilyName}, {SegoeMDL2AssetsFamilyName}",
            TADNFont.FanFont => FanFontFamilyName,
            _ => throw new ArgumentOutOfRangeException(nameof(font), font, null)
        };

    /// <summary>
    /// Returns the Avalonia font family for a glyph font identity.
    /// </summary>
    public static FontFamily ResolveFontFamily(TADNFont font) =>
        new(ResolveFontFamilyName(font));
}
