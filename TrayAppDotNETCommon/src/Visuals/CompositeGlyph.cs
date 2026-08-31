namespace TrayAppDotNETCommon.Visuals;

/// <summary>
/// Describes a path-based glyph assembled from transformed font glyph layers.
/// </summary>
public sealed class CompositeGlyph
{
    public CompositeGlyph(
        int designCanvasSize,
        double outerMarginFraction,
        IReadOnlyList<CompositeGlyphLayer> layers)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(designCanvasSize, other: 1);
        if (outerMarginFraction is < 0.0 or >= 0.5)
            throw new ArgumentOutOfRangeException(nameof(outerMarginFraction));

        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0)
            throw new ArgumentException(message: "A composite glyph requires at least one layer.", nameof(layers));

        CompositeGlyphLayer[] copiedLayers = new CompositeGlyphLayer[layers.Count];
        for (int index = 0; index < layers.Count; index++)
        {
            CompositeGlyphLayer layer = layers[index]
                                        ?? throw new ArgumentException(
                                            message: "Composite glyph layers cannot contain null entries.",
                                            nameof(layers));
            ArgumentNullException.ThrowIfNull(layer.Glyph);
            copiedLayers[index] = layer;
        }

        DesignCanvasSize = designCanvasSize;
        OuterMarginFraction = outerMarginFraction;
        Layers = Array.AsReadOnly(copiedLayers);
    }

    public int DesignCanvasSize { get; }

    public double OuterMarginFraction { get; }

    public IReadOnlyList<CompositeGlyphLayer> Layers { get; }

    internal int StateHash
    {
        get
        {
            HashCode hashCode = new();
            hashCode.Add(DesignCanvasSize);
            hashCode.Add(OuterMarginFraction);
            foreach (CompositeGlyphLayer layer in Layers)
            {
                hashCode.Add(layer.Glyph.Text, StringComparer.Ordinal);
                hashCode.Add(layer.Glyph.Font);
                hashCode.Add(layer.Glyph.FontWeight);
                hashCode.Add(layer.ScaleX);
                hashCode.Add(layer.ScaleY);
                hashCode.Add(layer.TranslateX);
                hashCode.Add(layer.TranslateY);
            }

            return hashCode.ToHashCode();
        }
    }
}

/// <summary>
/// Places one font glyph in a composite design canvas using direct scale and translation values.
/// </summary>
public sealed record CompositeGlyphLayer(
    Glyph Glyph,
    double ScaleX = 1.0,
    double ScaleY = 1.0,
    double TranslateX = 0.0,
    double TranslateY = 0.0);
