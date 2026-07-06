using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Models;

public enum SliderThumbShape
{
    Glyph,
    Capsule
}

/// <summary>
/// A selectable slider thumb glyph with display metrics persisted beside the selected option.
/// </summary>
public class SliderThumbGlyphOption
{
    public string Name { get; set; } = "Circle";
    public string Glyph { get; set; } = GlyphCatalog.SLIDER_THUMB_CIRCLE.Text;
    public string FontFamily { get; set; } = TADNFontResolver.SegoeFluentIconsFamilyName;
    public double FontSize { get; set; } = 18;
    public double Width { get; set; } = 18;
    public double Height { get; set; } = 18;

    /// <summary>
    /// Horizontal layout scale applied to the rendered glyph.
    /// </summary>
    public double XScale { get; set; } = 1.0;

    public SliderThumbShape Shape { get; set; } = SliderThumbShape.Glyph;

    public bool IsGlyph => Shape == SliderThumbShape.Glyph;
    public bool IsCapsule => Shape == SliderThumbShape.Capsule;

    public static List<SliderThumbGlyphOption> CreateDefaults() =>
    [
        new() { Name = "Capsule", Shape = SliderThumbShape.Capsule, Width = 10, Height = 22 },
        new() { Name = "Circle", Glyph = GlyphCatalog.SLIDER_THUMB_CIRCLE.Text, FontSize = 18 },
        new() { Name = "Diamond", Glyph = GlyphCatalog.SLIDER_THUMB_DIAMOND.Text, FontSize = 16 },
        new() { Name = "Star", Glyph = GlyphCatalog.SLIDER_THUMB_STAR.Text, FontSize = 18 },
        new() { Name = "Square", Glyph = GlyphCatalog.SLIDER_THUMB_SQUARE.Text, FontSize = 16 },
        new() { Name = "Heart", Glyph = GlyphCatalog.SLIDER_THUMB_HEART.Text, FontSize = 16 }
    ];
}
