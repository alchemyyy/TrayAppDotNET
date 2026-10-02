using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TrayAppDotNETCommon.AxamlPropertyLinker;

/// <summary>
/// Adds the typed node ids of a control map to its x:Class owner: one id per node, nested the way the AXAML nests.
/// Structural errors become build diagnostics. The Avalonia XAML compiler builds the runtime node tree from the same
/// file, and the hand-written owner supplies the base type and the constructor that loads it.
/// </summary>
[Generator]
public sealed class ControlMapGenerator : IIncrementalGenerator
{
    // MSBuild marks the common map with this metadata when an app compiles against its templates
    private const string ReferenceMetadataKey = "build_metadata.AdditionalFiles.TrayAppDotNETControlMapReference";
    private const string ControlMapTypeName = "TrayAppDotNETCommon.UI.ControlMapping.ControlMap";
    private const string NodeIDType = "global::TrayAppDotNETCommon.UI.ControlMapping.ControlMapNodeID";
    private const string CatalogType = "global::TrayAppDotNETCommon.UI.ControlMapping.ControlMapCatalog";
    private const string AXAMLExtension = ".axaml";
    private const string ScopeIDMember = "ID";
    private const string IndentUnit = "    ";
    private const int ClassMemberDepth = 2;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ControlMapDocument> documents = context.AdditionalTextsProvider
            .Where(static text => text.Path.EndsWith(AXAMLExtension, StringComparison.OrdinalIgnoreCase))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, cancellationToken) =>
                ControlMapParser.Parse(pair.Left, IsReference(pair.Right, pair.Left), cancellationToken))
            .Where(static document => document != null)
            .Select(static (document, _) => document!);

        context.RegisterSourceOutput(documents.Collect().Combine(context.CompilationProvider),
            static (sourceContext, value) => Emit(sourceContext, value.Left, value.Right));
    }

    private static bool IsReference(AnalyzerConfigOptionsProvider optionsProvider, AdditionalText text) =>
        optionsProvider.GetOptions(text).TryGetValue(ReferenceMetadataKey, out string? value)
        && string.Equals(value, b: "true", StringComparison.OrdinalIgnoreCase);

    private static void Emit(
        SourceProductionContext context,
        ImmutableArray<ControlMapDocument> documents,
        Compilation compilation)
    {
        Dictionary<string, ControlMapTemplateInfo> externalTemplates = CollectExternalTemplates(documents);
        HashSet<string> emittedClasses = new(StringComparer.Ordinal);
        foreach (ControlMapDocument document in documents.OrderBy(static value => value.Path,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (document.IsReference) continue;

            List<Diagnostic> diagnostics = [..document.Diagnostics];
            if (document.HasClass)
            {
                ValidateOwner(document, compilation, diagnostics);
                ControlMapValidator.Validate(document, externalTemplates, diagnostics);
            }

            foreach (Diagnostic diagnostic in diagnostics)
                context.ReportDiagnostic(diagnostic);

            // Every control map diagnostic is an error, so the build already fails without the class
            if (diagnostics.Count > 0 || !document.HasClass) continue;

            if (!emittedClasses.Add(document.FullClassName))
            {
                context.ReportDiagnostic(ControlMapDiagnostics.At(
                    ControlMapDiagnostics.InvalidDocument,
                    document.Path,
                    document.RootLine,
                    document.RootColumn,
                    $"x:Class '{document.FullClassName}' is already declared by another control map"));
                continue;
            }

            context.AddSource(HintName(document.FullClassName), GenerateClass(document));
        }
    }

    // Avalonia's name generator cannot see generated types, so the owner must be hand-written like every x:Class owner
    private static void ValidateOwner(ControlMapDocument document, Compilation compilation, List<Diagnostic> diagnostics)
    {
        INamedTypeSymbol? owner = compilation.GetTypeByMetadataName(document.FullClassName);
        if (owner?.BaseType != null
            && string.Equals(owner.BaseType.ToDisplayString(), ControlMapTypeName, StringComparison.Ordinal))
            return;

        diagnostics.Add(ControlMapDiagnostics.At(
            ControlMapDiagnostics.MissingOwner,
            document.Path,
            document.RootLine,
            document.RootColumn,
            document.FullClassName));
    }

    private static Dictionary<string, ControlMapTemplateInfo> CollectExternalTemplates(
        ImmutableArray<ControlMapDocument> documents)
    {
        Dictionary<string, ControlMapTemplateInfo> templates = new(StringComparer.Ordinal);
        foreach (ControlMapDocument document in documents)
        {
            if (!document.IsReference) continue;

            foreach (ControlMapElement element in document.RootElements)
            {
                if (!string.Equals(element.Name, ControlMapValidator.TemplateElement, StringComparison.Ordinal))
                    continue;
                if (element.ID.Length == 0 || templates.ContainsKey(element.ID)) continue;

                templates.Add(element.ID, new ControlMapTemplateInfo(
                    element.ID,
                    document.FullClassName,
                    ControlMapValidator.CollectSlotNames(element)));
            }
        }

        return templates;
    }

    private static string GenerateClass(ControlMapDocument document)
    {
        StringBuilder builder = new();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.Append("namespace ").AppendLine(document.ClassNamespace);
        builder.AppendLine("{");
        // The owner declares accessibility, base type, and constructor, so this part declares none of them
        builder.Append(IndentUnit).Append("partial class ").AppendLine(document.ClassName);
        builder.Append(IndentUnit).AppendLine("{");
        AppendLine(builder, ClassMemberDepth, "/// <summary>Map name carried by every node id in this map.</summary>");
        AppendLine(builder, ClassMemberDepth, $"public const string MapName = {StringLiteral(document.FullClassName)};");
        builder.AppendLine();
        AppendLine(builder, ClassMemberDepth,
            "/// <summary>Registers this map with the catalog when its assembly loads; it builds on first use.</summary>");
        AppendLine(builder, ClassMemberDepth, "#pragma warning disable CA2255");
        AppendLine(builder, ClassMemberDepth, "[global::System.Runtime.CompilerServices.ModuleInitializer]");
        AppendLine(builder, ClassMemberDepth, "internal static void RegisterControlMap() =>");
        AppendLine(builder, ClassMemberDepth + 1,
            $"{CatalogType}.Register(MapName, static () => new {document.ClassName}());");
        AppendLine(builder, ClassMemberDepth, "#pragma warning restore CA2255");
        AppendVariants(builder, ControlMapValidator.CollectVariantNames(document));
        AppendNodes(builder, document.RootElements);
        builder.Append(IndentUnit).AppendLine("}");
        builder.AppendLine("}");
        return builder.ToString();
    }

    // Typed names for MapVariant, so code cannot activate a variant the map does not declare
    private static void AppendVariants(StringBuilder builder, List<string> variantNames)
    {
        if (variantNames.Count == 0) return;

        builder.AppendLine();
        AppendLine(builder, ClassMemberDepth, "/// <summary>Layout variants this map declares, for MapVariant.</summary>");
        AppendLine(builder, ClassMemberDepth, "public static class Variants");
        AppendLine(builder, ClassMemberDepth, "{");
        foreach (string variantName in variantNames)
            AppendLine(builder, ClassMemberDepth + 1, $"public const string {variantName} = {StringLiteral(variantName)};");

        AppendLine(builder, ClassMemberDepth, "}");
    }

    // Emits one static class per container and one field per leaf, iteratively in document order
    private static void AppendNodes(StringBuilder builder, List<ControlMapElement> rootElements)
    {
        Stack<(ControlMapElement? Element, int Depth)> pending = new();
        PushChildren(pending, rootElements, ClassMemberDepth);
        while (pending.Count > 0)
        {
            (ControlMapElement? element, int depth) = pending.Pop();
            if (element == null)
            {
                AppendLine(builder, depth, "}");
                continue;
            }

            switch (element.Name)
            {
                case ControlMapValidator.SlotElement:
                case ControlMapValidator.VariantElement:
                    break;
                case ControlMapValidator.LeafElement:
                    builder.AppendLine();
                    AppendSummary(builder, depth, element);
                    AppendLine(builder, depth, $"public static readonly {NodeIDType} {element.ID} =");
                    AppendLine(builder, depth + 1,
                        $"new(MapName, {StringLiteral(ControlMapValidator.PathOf(element))});");
                    break;
                default:
                    builder.AppendLine();
                    AppendSummary(builder, depth, element);
                    AppendLine(builder, depth, $"public static class {element.ID}");
                    AppendLine(builder, depth, "{");
                    AppendLine(builder, depth + 1, $"public static readonly {NodeIDType} {ScopeIDMember} =");
                    AppendLine(builder, depth + 2,
                        $"new(MapName, {StringLiteral(ControlMapValidator.PathOf(element))});");
                    pending.Push((null, depth));
                    PushChildren(pending, element.Children, depth + 1);
                    break;
            }
        }
    }

    private static void PushChildren(
        Stack<(ControlMapElement? Element, int Depth)> pending,
        List<ControlMapElement> children,
        int depth)
    {
        for (int childIndex = children.Count - 1; childIndex >= 0; childIndex--)
            pending.Push((children[childIndex], depth));
    }

    private static void AppendSummary(StringBuilder builder, int depth, ControlMapElement element)
    {
        string kind = element.GetValue(attributeName: "Kind") ?? string.Empty;
        string description = element.GetValue(attributeName: "Description") ?? string.Empty;
        StringBuilder summary = new();
        summary.Append(kind.Length > 0 ? kind + " " + element.Name.ToLowerInvariant() : element.Name);
        if (description.Length > 0)
            summary.Append(": ").Append(EscapeXML(description));

        AppendLine(builder, depth, $"/// <summary>{summary}</summary>");
    }

    private static void AppendLine(StringBuilder builder, int depth, string text)
    {
        for (int level = 0; level < depth; level++)
            builder.Append(IndentUnit);

        builder.AppendLine(text);
    }

    private static string HintName(string fullClassName)
    {
        StringBuilder builder = new();
        foreach (char character in fullClassName)
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');

        builder.Append(".ControlMap.g.cs");
        return builder.ToString();
    }

    private static string EscapeXML(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string StringLiteral(string value) =>
        "\"" + value.Replace("\\", @"\\").Replace("\"", "\\\"") + "\"";
}
