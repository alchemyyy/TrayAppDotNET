#if DEBUG
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Debugging;

/// <summary>Preserves a glyph's AXAML resource identity until it is applied to a text control.</summary>
internal static class DebugGlyphProvenanceRegistry
{
    private static readonly ConditionalWeakTable<Glyph, GlyphResource> ResourcesByGlyph = new();

    public static void Register(Glyph glyph, string resourceKey)
    {
        ResourcesByGlyph.Remove(glyph);
        ResourcesByGlyph.Add(glyph, new GlyphResource(resourceKey));
    }

    public static void RecordApplication(
        TextBlock textBlock,
        Glyph glyph,
        string sourceFilePath,
        int sourceLine,
        string sourceMember)
    {
        if (!ResourcesByGlyph.TryGetValue(glyph, out GlyphResource? resource)) return;

        DebugPropertyProvenanceRegistry.Record(
            textBlock,
            TextBlock.TextProperty,
            textBlock.Text,
            DebugPropertyAssignmentOperation.Builder,
            resource.ResourceKey,
            sourceFilePath,
            sourceLine,
            0,
            sourceMember,
            resource.ResourceKey);
    }

    private sealed record GlyphResource(string ResourceKey);
}
#endif
