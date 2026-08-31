using Avalonia.Media;

namespace TrayAppDotNETCommon.Visuals;

/// <summary>
/// Describes glyph text and intrinsic optical rendering metadata.
/// </summary>
public sealed class Glyph(string text, TADNFont font)
{
    public string Text { get; } = text ?? throw new ArgumentNullException(nameof(text));

    public TADNFont Font { get; } = font;

    public FontWeight? FontWeight { get; init; }

    public double? ScaleX { get; init; }

    public double? ScaleY { get; init; }

    public double? TranslateX { get; init; }

    public double? TranslateY { get; init; }

    /// <summary>
    /// Returns the raw glyph text for diagnostics.
    /// </summary>
    public override string ToString() => Text;

    /// <summary>
    /// Creates a Segoe Fluent Icons glyph with no fallback font.
    /// </summary>
    public static Glyph SegoeFluent(
        string text,
        FontWeight? fontWeight = null,
        double? scaleX = null,
        double? scaleY = null,
        double? translateX = null,
        double? translateY = null) =>
        Create(text, TADNFont.SegoeFluentIcons, fontWeight, scaleX, scaleY, translateX, translateY);

    /// <summary>
    /// Creates a Segoe Fluent Icons glyph with MDL2 fallback.
    /// </summary>
    public static Glyph Fluent(
        string text,
        FontWeight? fontWeight = null,
        double? scaleX = null,
        double? scaleY = null,
        double? translateX = null,
        double? translateY = null) =>
        Create(text, TADNFont.SegoeFluentIconsThenMDL2Assets, fontWeight, scaleX, scaleY, translateX, translateY);

    /// <summary>
    /// Copies a glyph text and correction data into the Fluent fallback font.
    /// </summary>
    public static Glyph Fluent(Glyph glyph)
    {
        ArgumentNullException.ThrowIfNull(glyph);

        return Create(glyph.Text, TADNFont.SegoeFluentIconsThenMDL2Assets, glyph.FontWeight, glyph.ScaleX, glyph.ScaleY,
            glyph.TranslateX, glyph.TranslateY);
    }

    /// <summary>
    /// Creates a Segoe MDL2 Assets glyph.
    /// </summary>
    public static Glyph MDL2(
        string text,
        FontWeight? fontWeight = null,
        double? scaleX = null,
        double? scaleY = null,
        double? translateX = null,
        double? translateY = null) =>
        Create(text, TADNFont.SegoeMDL2Assets, fontWeight, scaleX, scaleY, translateX, translateY);

    /// <summary>
    /// Creates a glyph from the Fan app's custom icon font.
    /// </summary>
    public static Glyph FanFont(
        string text,
        FontWeight? fontWeight = null,
        double? scaleX = null,
        double? scaleY = null,
        double? translateX = null,
        double? translateY = null) =>
        Create(text, TADNFont.FanFont, fontWeight, scaleX, scaleY, translateX, translateY);

    private static Glyph Create(
        string text,
        TADNFont font,
        FontWeight? fontWeight,
        double? scaleX,
        double? scaleY,
        double? translateX,
        double? translateY) =>
        new(text, font)
        {
            FontWeight = fontWeight,
            ScaleX = scaleX,
            ScaleY = scaleY,
            TranslateX = translateX,
            TranslateY = translateY
        };
}
