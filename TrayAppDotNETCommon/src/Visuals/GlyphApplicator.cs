using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Debugging;

namespace TrayAppDotNETCommon.Visuals;

/// <summary>
/// Applies glyph metadata to Avalonia text glyph hosts.
/// </summary>
public static class GlyphApplicator
{
    /// <summary>
    /// Applies glyph text, font, and intrinsic optical corrections to a text block.
    /// </summary>
    public static void ApplyTo(TextBlock textBlock, Glyph glyph, bool applyFontFamily = true)
    {
        ArgumentNullException.ThrowIfNull(textBlock);
        ArgumentNullException.ThrowIfNull(glyph);

        textBlock.Text = glyph.Text;
        if (applyFontFamily)
            textBlock.FontFamily = TADNFontResolver.ResolveFontFamily(glyph.Font);

        if (glyph.FontWeight.HasValue)
            textBlock.FontWeight = glyph.FontWeight.Value;

        DebugUIProvenance.RecordGlyphApplication(textBlock, glyph);

        if (!HasRenderTransformMetadata(glyph)) return;

        TransformGroup transformGroup = new();
        if (glyph.ScaleX.HasValue || glyph.ScaleY.HasValue)
        {
            transformGroup.Children.Add(new ScaleTransform(
                glyph.ScaleX ?? 1.0,
                glyph.ScaleY ?? 1.0));
        }

        if (glyph.TranslateX.HasValue || glyph.TranslateY.HasValue)
        {
            transformGroup.Children.Add(new TranslateTransform(
                glyph.TranslateX ?? 0.0,
                glyph.TranslateY ?? 0.0));
        }

        textBlock.RenderTransform = transformGroup;
    }

    private static bool HasRenderTransformMetadata(Glyph glyph) =>
        glyph.ScaleX.HasValue
        || glyph.ScaleY.HasValue
        || glyph.TranslateX.HasValue
        || glyph.TranslateY.HasValue;
}
