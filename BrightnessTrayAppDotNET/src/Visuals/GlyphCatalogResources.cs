using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TrayAppDotNETCommon.Visuals;

namespace BrightnessTrayAppDotNET.Visuals;

public sealed partial class GlyphCatalogResources : ResourceDictionary
{
    /// <summary>
    /// Initializes the compiled brightness glyph catalog dictionary.
    /// </summary>
    public GlyphCatalogResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Reads a glyph from this dictionary.
    /// </summary>
    public Glyph Glyph(string name) => GlyphCatalogResourceReader.Glyph(this, "GlyphCatalog", name);
}
