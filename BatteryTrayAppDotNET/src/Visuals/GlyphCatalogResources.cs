using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TrayAppDotNETCommon.Visuals;

namespace BatteryTrayAppDotNET.Visuals;

public sealed class GlyphCatalogResources : ResourceDictionary
{
    /// <summary>
    /// Initializes the compiled battery glyph catalog dictionary.
    /// </summary>
    public GlyphCatalogResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Reads a glyph from this dictionary.
    /// </summary>
    public Glyph Glyph(string name) => GlyphCatalogResourceReader.Glyph(this, prefix: "GlyphCatalog", name);
}
