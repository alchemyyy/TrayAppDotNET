using Avalonia.Controls;
using TrayAppDotNETCommon.UI;

namespace BrightnessTrayAppDotNET.Visuals;

/// <summary>The brightness glyphs; <see cref="GlyphCatalog"/> reads them through the generated getters.</summary>
[AxamlResourceStore(AxamlReloadMode.GlyphCatalog, "Brightness glyph catalog")]
public sealed partial class GlyphCatalogResources : ResourceDictionary;
