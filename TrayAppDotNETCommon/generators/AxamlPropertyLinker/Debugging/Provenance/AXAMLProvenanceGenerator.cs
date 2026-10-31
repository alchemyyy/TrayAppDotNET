#if DEBUG
using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TrayAppDotNETCommon.AxamlPropertyLinker;

/// <summary>Generates debug-only source metadata from AXAML documents.</summary>
internal static class AXAMLProvenanceGenerator
{
    internal const string DocumentsTrackingName = "AXAMLProvenanceDocuments";
    private const string GeneratedNamespaceSuffix = ".GeneratedAxaml";

    public static void Initialize(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<AxamlDocument> parsedDocuments)
    {
        IncrementalValueProvider<AXAMLProvenanceSettings> settings =
            context.AnalyzerConfigOptionsProvider.Select(static (provider, _) => ReadSettings(provider));
        IncrementalValuesProvider<AXAMLProvenanceDocument> documents = parsedDocuments
            .Combine(settings)
            .Select(static (value, _) => Parse(value.Left, value.Right))
            .Where(static document => document != null)
            .Select(static (document, _) => document!)
            .WithTrackingName(DocumentsTrackingName);

        context.RegisterSourceOutput(
            documents.Collect().Combine(settings),
            static (sourceContext, value) => Emit(sourceContext, value.Left, value.Right));
    }

    private static AXAMLProvenanceDocument? Parse(AxamlDocument axamlDocument, AXAMLProvenanceSettings settings)
    {
        XDocument? document = axamlDocument.Document;
        XElement? root = document?.Root;
        if (root == null) return null;

        // Control maps describe focus structure, not visuals, so the hover inspector has nothing to trace to them
        if (string.Equals(root.Name.NamespaceName, ControlMapParser.ControlMapNamespace, StringComparison.Ordinal))
            return null;

        string? ownerTypeName = root.Attribute(XName.Get(localName: "Class", CSharpNames.XamlNamespace))?.Value;
        if (!CSharpNames.TrySplitClassName(ownerTypeName, out _, out _)) return null;

        string sourcePath = AXAMLSourcePath.Normalize(axamlDocument.Path, settings.ProjectDirectory);
        return AXAMLProvenanceParser.Parse(document!, sourcePath, ownerTypeName!);
    }

    private static void Emit(
        SourceProductionContext context,
        ImmutableArray<AXAMLProvenanceDocument> documents,
        AXAMLProvenanceSettings settings)
    {
        if (documents.Length == 0) return;

        context.AddSource(
            AXAMLProvenanceEmitter.HintName,
            AXAMLProvenanceEmitter.Generate(
                documents,
                CSharpNames.EscapeQualifiedName(settings.RootNamespace + GeneratedNamespaceSuffix)));
    }

    private static AXAMLProvenanceSettings ReadSettings(
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        AnalyzerConfigOptions options = optionsProvider.GlobalOptions;
        _ = options.TryGetValue(key: "build_property.ProjectDir", out string? projectDirectory);
        return new AXAMLProvenanceSettings(CSharpNames.ReadRootNamespace(options), projectDirectory ?? string.Empty);
    }

    private readonly record struct AXAMLProvenanceSettings(
        string RootNamespace,
        string ProjectDirectory);
}
#endif
