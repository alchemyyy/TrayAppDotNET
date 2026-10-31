using Avalonia.Controls;

namespace NetworkTrayAppDotNET.Visuals;

/// <summary>The network glyphs; <see cref="GlyphCatalog"/> reads them through the generated getters.</summary>
[AxamlResourceStore(AxamlReloadMode.GlyphCatalog, "Network glyph catalog")]
public sealed partial class GlyphCatalogResources : ResourceDictionary;
