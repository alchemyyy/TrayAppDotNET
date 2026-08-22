using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Debugging;

namespace TrayAppDotNETCommon.Visuals;

/// <summary>
/// Reads live glyph definitions from AXAML resource dictionaries.
/// </summary>
public static class GlyphCatalogResourceReader
{
    /// <summary>
    /// Resolves a glyph definition by prefix and name.
    /// </summary>
    public static Glyph Glyph(ResourceDictionary resources, string prefix, string name)
    {
        string normalizedPrefix = prefix.EndsWith('.') ? prefix : prefix + ".";
        string key = normalizedPrefix + name;
        object? value = resources[key];
        if (value is GlyphDefinition definition)
        {
            Glyph glyph = definition.ToGlyph();
            DebugUIProvenance.RegisterGlyphResource(glyph, key);
            return glyph;
        }

        throw new InvalidOperationException($"Glyph resource '{key}' is missing or not a GlyphDefinition.");
    }
}
