using Avalonia.Controls;

namespace BatteryTrayAppDotNET.Visuals;

/// <summary>The battery glyphs; <see cref="GlyphCatalog"/> reads them through the generated getters.</summary>
[AxamlResourceStore(AxamlReloadMode.GlyphCatalog, "Battery glyph catalog")]
public sealed partial class GlyphCatalogResources : ResourceDictionary;
