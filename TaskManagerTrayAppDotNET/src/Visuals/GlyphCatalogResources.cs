using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.Visuals;

public sealed partial class GlyphCatalogResources : ResourceDictionary
{
    /// <summary>
    /// Initializes the compiled Task Manager glyph catalog dictionary.
    /// </summary>
    public GlyphCatalogResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Reads a glyph from this dictionary.
    /// </summary>
    public Glyph Glyph(string name) => GlyphCatalogResourceReader.Glyph(this, "GlyphCatalog", name);
}
