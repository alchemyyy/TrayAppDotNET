#if DEBUG
using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TrayAppDotNETCommon.AxamlPropertyLinker;

/// <summary>Generates debug-only source metadata from AXAML documents.</summary>
internal static class AXAMLProvenanceGenerator
{
    private const string DefaultRootNamespace = "AxamlPropertyLinkerGenerated";
    private const string GeneratedNamespaceSuffix = ".GeneratedAxaml";
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<AXAMLProvenanceSettings> settings =
            context.AnalyzerConfigOptionsProvider.Select(static (provider, _) => ReadSettings(provider));
        IncrementalValuesProvider<AXAMLProvenanceDocument> documents = context.AdditionalTextsProvider
            .Where(static text => text.Path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .Combine(settings)
            .Select(static (value, cancellationToken) =>
                ParseAdditionalText(value.Left, value.Right, cancellationToken))
            .Where(static document => document != null)
            .Select(static (document, _) => document!);

        context.RegisterSourceOutput(
            documents.Collect().Combine(settings),
            static (sourceContext, value) => Emit(sourceContext, value.Left, value.Right));
    }

    private static AXAMLProvenanceDocument? ParseAdditionalText(
        AdditionalText text,
        AXAMLProvenanceSettings settings,
        CancellationToken cancellationToken)
    {
        Microsoft.CodeAnalysis.Text.SourceText? sourceText = text.GetText(cancellationToken);
        if (sourceText == null) return null;

        XDocument document;
        try
        {
            document = XDocument.Parse(
                sourceText.ToString(),
                LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        catch
        {
            return null;
        }

        XElement? root = document.Root;
        if (root == null) return null;

        XName classAttributeName = XName.Get("Class", XamlNamespace);
        string? ownerTypeName = root.Attribute(classAttributeName)?.Value;
        if (!IsQualifiedNamespace(ownerTypeName)) return null;

        string sourcePath = AXAMLSourcePath.Normalize(text.Path, settings.ProjectDirectory);
        return AXAMLProvenanceParser.Parse(document, sourcePath, ownerTypeName!);
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
                settings.RootNamespace + GeneratedNamespaceSuffix));
    }

    private static AXAMLProvenanceSettings ReadSettings(
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        AnalyzerConfigOptions options = optionsProvider.GlobalOptions;
        string rootNamespace = ReadRootNamespace(options);
        _ = options.TryGetValue("build_property.ProjectDir", out string? projectDirectory);
        return new AXAMLProvenanceSettings(rootNamespace, projectDirectory ?? string.Empty);
    }

    private static string ReadRootNamespace(AnalyzerConfigOptions options)
    {
        if (options.TryGetValue("build_property.RootNamespace", out string? rootNamespace)
            && IsQualifiedNamespace(rootNamespace))
        {
            return rootNamespace!;
        }

        if (options.TryGetValue("build_property.MSBuildProjectName", out string? projectName)
            && IsIdentifier(projectName))
        {
            return projectName!;
        }

        return DefaultRootNamespace;
    }

    private static bool IsQualifiedNamespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        string[] parts = value.Split('.');
        if (parts.Length < 2) return false;

        foreach (string part in parts)
            if (!IsIdentifier(part)) return false;

        return true;
    }

    private static bool IsIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value[0] != '_' && !char.IsLetter(value[0])) return false;

        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            if (character != '_' && !char.IsLetterOrDigit(character)) return false;
        }

        return true;
    }

    private readonly record struct AXAMLProvenanceSettings(
        string RootNamespace,
        string ProjectDirectory);
}
#endif
