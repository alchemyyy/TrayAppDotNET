using Avalonia.Controls;
using TrayAppDotNETCommon.UI;

namespace TrayAppDotNETCommon.Visuals;

/// <summary>The glyphs every app shares; <see cref="GlyphCatalog"/> reads them through the generated getters.</summary>
[AxamlResourceStore(AxamlReloadMode.GlyphCatalog, "Common glyph catalog")]
public sealed partial class GlyphCatalogResources : ResourceDictionary;
