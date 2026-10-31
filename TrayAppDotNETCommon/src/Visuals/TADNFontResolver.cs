using Avalonia.Media;

namespace TrayAppDotNETCommon.Visuals;

/// <summary>
/// Resolves TrayAppDotNET font identities into Avalonia font families.
/// </summary>
public static class TADNFontResolver
{
    // NOTE: Static Segoe UI on purpose. Through Avalonia's Skia font manager, "Segoe UI Variable"
    // only matches 400 and renders 800 and 900 lighter than Bold, and a family list never
    // matches "Segoe UI Variable Text"
    public const string SegoeUIFamilyName = "Segoe UI";
    public const string SegoeFluentIconsFamilyName = "Segoe Fluent Icons";
    public const string SegoeMDL2AssetsFamilyName = "Segoe MDL2 Assets";

    private static string? _appFontFamilyName;

    /// <summary>
    /// Registers the font family behind <see cref="TADNFont.AppFont"/>, such as an app's embedded icon font. An app
    /// whose glyph catalog uses that font registers it before its first glyph is resolved.
    /// </summary>
    public static void RegisterAppFont(string familyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(familyName);
        _appFontFamilyName = familyName;
    }

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
            TADNFont.AppFont => _appFontFamilyName
                                ?? throw new InvalidOperationException("No app font has been registered."),
            _ => throw new ArgumentOutOfRangeException(nameof(font), font, message: null)
        };

    /// <summary>
    /// Returns the Avalonia font family for a glyph font identity.
    /// </summary>
    public static FontFamily ResolveFontFamily(TADNFont font) =>
        new(ResolveFontFamilyName(font));
}
