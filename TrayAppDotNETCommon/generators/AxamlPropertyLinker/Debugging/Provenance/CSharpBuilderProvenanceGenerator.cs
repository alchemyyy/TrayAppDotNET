using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TrayAppDotNETCommon.AxamlPropertyLinker;

internal static class CSharpBuilderProvenanceGenerator
{
    private const string DefaultRootNamespace = "AxamlPropertyLinkerGenerated";

    public static void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<CSharpBuilderProvenanceSettings> settings =
            context.AnalyzerConfigOptionsProvider.Select(static (provider, _) => ReadSettings(provider));
        IncrementalValuesProvider<CSharpBuilderBoundary> boundaries = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => CSharpBuilderProvenanceParser.IsCandidate(node),
                static (syntaxContext, cancellationToken) =>
                    CSharpBuilderProvenanceParser.Parse(syntaxContext, cancellationToken))
            .Where(static boundary => boundary != null)
            .Select(static (boundary, _) => boundary!);
        IncrementalValuesProvider<CSharpBuilderBoundary> normalizedBoundaries = boundaries
            .Combine(settings)
            .Select(static (value, _) => Normalize(value.Left, value.Right));

        context.RegisterSourceOutput(
            normalizedBoundaries.Collect().Combine(settings),
            static (sourceContext, value) => Emit(sourceContext, value.Left, value.Right));
    }

    private static void Emit(
        SourceProductionContext context,
        ImmutableArray<CSharpBuilderBoundary> boundaries,
        CSharpBuilderProvenanceSettings settings)
    {
        if (boundaries.Length == 0) return;

        context.AddSource(
            CSharpBuilderProvenanceEmitter.HintName,
            CSharpBuilderProvenanceEmitter.Generate(boundaries, settings.RootNamespace));
    }

    private static CSharpBuilderBoundary Normalize(
        CSharpBuilderBoundary boundary,
        CSharpBuilderProvenanceSettings settings) =>
        boundary with { SourcePath = AXAMLSourcePath.Normalize(boundary.SourcePath, settings.ProjectDirectory) };

    private static CSharpBuilderProvenanceSettings ReadSettings(
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        AnalyzerConfigOptions options = optionsProvider.GlobalOptions;
        _ = options.TryGetValue(key: "build_property.RootNamespace", out string? rootNamespace);
        _ = options.TryGetValue(key: "build_property.ProjectDir", out string? projectDirectory);
        return new CSharpBuilderProvenanceSettings(
            string.IsNullOrWhiteSpace(rootNamespace) ? DefaultRootNamespace : rootNamespace,
            projectDirectory ?? string.Empty);
    }
}
