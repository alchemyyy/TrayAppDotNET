using Avalonia.Media;

namespace TrayAppDotNETCommon.Visuals;

/// <summary>
/// XAML-friendly glyph definition that can be converted into runtime glyph metadata.
/// </summary>
public sealed class GlyphDefinition
{
    public string Text { get; set; } = string.Empty;

    public TADNFont Font { get; set; } = TADNFont.SegoeFluentIcons;

    public FontWeight? FontWeight { get; set; }

    public double? ScaleX { get; set; }

    public double? ScaleY { get; set; }

    public double? TranslateX { get; set; }

    public double? TranslateY { get; set; }

    /// <summary>
    /// Converts this definition into the glyph consumed by controls and renderers.
    /// </summary>
    public Glyph ToGlyph() => new(Text, Font)
    {
        FontWeight = FontWeight,
        ScaleX = ScaleX,
        ScaleY = ScaleY,
        TranslateX = TranslateX,
        TranslateY = TranslateY
    };
}
