using Avalonia.Controls;

namespace VolumeTrayAppDotNET.Visuals;

/// <summary>The volume glyphs; <see cref="GlyphCatalog"/> reads them through the generated getters.</summary>
[AxamlResourceStore(AxamlReloadMode.GlyphCatalog, "Volume glyph catalog")]
public sealed partial class GlyphCatalogResources : ResourceDictionary;
