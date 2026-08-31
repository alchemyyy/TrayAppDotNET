#if DEBUG
using Microsoft.CodeAnalysis;

// Debugging is an implementation grouping, not a namespace boundary
// ReSharper disable once CheckNamespace
namespace TrayAppDotNETCommon.AxamlPropertyLinker;

/// <summary>Registers the debug-only AXAML and C# provenance generators.</summary>
internal static class DebugProvenanceGenerator
{
    public static void Initialize(IncrementalGeneratorInitializationContext context)
    {
        AXAMLProvenanceGenerator.Initialize(context);
        CSharpBuilderProvenanceGenerator.Initialize(context);
    }
}
#endif
