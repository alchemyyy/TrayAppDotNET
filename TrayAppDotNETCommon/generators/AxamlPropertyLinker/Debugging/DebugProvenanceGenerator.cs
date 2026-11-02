#if DEBUG
using Microsoft.CodeAnalysis;

namespace TrayAppDotNETCommon.AxamlPropertyLinker;

/// <summary>Registers the debug-only AXAML and C# provenance generators.</summary>
internal static class DebugProvenanceGenerator
{
    public static void Initialize(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<AxamlDocument> documents)
    {
        AXAMLProvenanceGenerator.Initialize(context, documents);
        CSharpBuilderProvenanceGenerator.Initialize(context);
    }
}
#endif
