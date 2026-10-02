using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TrayAppDotNETCommon.AxamlPropertyLinker;

/// <summary>
/// Reports control map nodes that no code in the compilation tags. A node counts as tagged once code reads its
/// generated id, which MapTo and MapCommand calls do. Only nodes the runtime needs a control or handler for are
/// required: window and popup surfaces, scopes whose navigation needs a container, keyboard-reachable leaves,
/// commands with accelerators, and template instances the runtime could not tell apart without an instance id.
/// Nothing under a tray or global surface is required: Win32 delivers that input, not a focused control.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ControlMapTagAnalyzer : DiagnosticAnalyzer
{
    private const string NodeIDTypeName = "TrayAppDotNETCommon.UI.ControlMapping.ControlMapNodeID";
    private const string ControlMapTypeName = "TrayAppDotNETCommon.UI.ControlMapping.ControlMap";
    private const string ReferenceMetadataKey = "build_metadata.AdditionalFiles.TrayAppDotNETControlMapReference";
    private const string ScopeIDMember = "ID";
    private const string AXAMLExtension = ".axaml";
    private const char PathSeparator = '.';
    private const char KeySeparator = ':';

    private static readonly string[] KeyboardLeafKinds =
        ["Button", "Toggle", "Option", "Select", "Slider", "Number", "Text", "Navigation", "ListItem"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [ControlMapDiagnostics.UntaggedNode];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();

        // Generated code declares the ids; only hand-written references count as tagging
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        List<RequiredNode> requiredNodes = CollectRequiredNodes(context.Options, context.CancellationToken);
        if (requiredNodes.Count == 0) return;

        ConcurrentDictionary<string, byte> referencedKeys = new(StringComparer.Ordinal);
        context.RegisterOperationAction(operationContext =>
        {
            IFieldSymbol field = ((IFieldReferenceOperation)operationContext.Operation).Field;
            if (!string.Equals(field.Type.ToDisplayString(), NodeIDTypeName, StringComparison.Ordinal)) return;

            string? key = KeyOf(field);
            if (key != null)
                referencedKeys.TryAdd(key, 0);
        }, OperationKind.FieldReference);

        context.RegisterCompilationEndAction(endContext =>
        {
            foreach (RequiredNode node in requiredNodes)
            {
                if (referencedKeys.ContainsKey(node.Key)) continue;

                endContext.ReportDiagnostic(ControlMapDiagnostics.At(
                    ControlMapDiagnostics.UntaggedNode,
                    node.FilePath,
                    node.Line,
                    node.Column,
                    node.ElementName,
                    node.Path));
            }
        });
    }

    // Full map name and path from the generated nesting: BatteryTrayAppDotNET.UI.ControlMap.Flyout.Header.Settings is
    // "BatteryTrayAppDotNET.UI.ControlMap:Flyout.Header.Settings", and a scope's own ID member names the scope itself.
    // Every map is named ControlMap, so the namespace keeps a common map id from tagging an app node of the same path
    private static string? KeyOf(IFieldSymbol field)
    {
        List<string> segments = [];
        if (!string.Equals(field.Name, ScopeIDMember, StringComparison.Ordinal))
            segments.Add(field.Name);

        for (INamedTypeSymbol? type = field.ContainingType; type != null; type = type.ContainingType)
        {
            if (string.Equals(type.BaseType?.ToDisplayString(), ControlMapTypeName, StringComparison.Ordinal))
            {
                segments.Reverse();
                return type.ToDisplayString() + KeySeparator + string.Join(PathSeparator.ToString(), segments);
            }

            segments.Add(type.Name);
        }

        return null;
    }

    private static List<RequiredNode> CollectRequiredNodes(AnalyzerOptions options, CancellationToken cancellationToken)
    {
        List<RequiredNode> requiredNodes = [];
        foreach (AdditionalText text in options.AdditionalFiles)
        {
            if (!text.Path.EndsWith(AXAMLExtension, StringComparison.OrdinalIgnoreCase)) continue;
            if (options.AnalyzerConfigOptionsProvider.GetOptions(text).TryGetValue(ReferenceMetadataKey, out string? value)
                && string.Equals(value, b: "true", StringComparison.OrdinalIgnoreCase)) continue;

            ControlMapDocument? document = ControlMapParser.Parse(text, isReference: false, cancellationToken);
            if (document == null || !document.HasClass || document.Diagnostics.Count > 0) continue;

            AddRequiredNodes(document, requiredNodes);
        }

        return requiredNodes;
    }

    private static void AddRequiredNodes(ControlMapDocument document, List<RequiredNode> requiredNodes)
    {
        Dictionary<string, int> instanceCounts = CountTemplateInstances(document);
        Stack<ControlMapElement> pending = new();
        PushChildren(pending, document.RootElements);
        while (pending.Count > 0)
        {
            ControlMapElement element = pending.Pop();
            if (IsWin32Surface(element)) continue;

            PushChildren(pending, element.Children);
            if (!IsRequired(element, instanceCounts)) continue;

            string path = ControlMapValidator.PathOf(element);
            requiredNodes.Add(new RequiredNode(
                document.FullClassName + KeySeparator + path,
                path,
                element.Name,
                document.Path,
                element.Line,
                element.Column));
        }
    }

    private static bool IsRequired(ControlMapElement element, Dictionary<string, int> instanceCounts)
    {
        string? template = element.GetValue(attributeName: "Template");
        if (template != null)
            return instanceCounts.TryGetValue(template, out int count) && count > 1;

        switch (element.Name)
        {
            case ControlMapValidator.SurfaceElement:
                return element.GetValue(attributeName: "Kind") is null or "Window" or "Popup";
            case ControlMapValidator.ScopeElement:
                return IsTrue(element.GetValue(attributeName: "IsRepeated"))
                       || IsTrue(element.GetValue(attributeName: "IsArranged"))
                       || IsTrue(element.GetValue(attributeName: "IsFocusable"))
                       || element.GetValue(attributeName: "Tab") is { } tab && tab != "Local"
                       || element.GetValue(attributeName: "Arrows") is { } arrows && arrows != "None"
                       || element.GetValue(attributeName: "Entry") == "Enter";
            case ControlMapValidator.LeafElement:
                string kind = element.GetValue(attributeName: "Kind") ?? string.Empty;
                if (string.Equals(kind, b: "Command", StringComparison.Ordinal))
                    return element.GetValue(attributeName: "Keys") != null;

                return Array.IndexOf(KeyboardLeafKinds, kind) >= 0
                       && !string.Equals(element.GetValue(attributeName: "IsTabStop"), b: "False",
                           StringComparison.OrdinalIgnoreCase);
            default:
                return false;
        }
    }

    // A template instantiated once resolves without an instance id; several instances need one each
    private static Dictionary<string, int> CountTemplateInstances(ControlMapDocument document)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        Stack<ControlMapElement> pending = new();
        PushChildren(pending, document.RootElements);
        while (pending.Count > 0)
        {
            ControlMapElement element = pending.Pop();
            PushChildren(pending, element.Children);
            if (element.GetValue(attributeName: "Template") is not { } template) continue;

            counts[template] = counts.TryGetValue(template, out int count) ? count + 1 : 1;
        }

        return counts;
    }

    // Tray icon messages and global hotkeys arrive through Win32, so no Avalonia control stands for their nodes
    private static bool IsWin32Surface(ControlMapElement element) =>
        string.Equals(element.Name, ControlMapValidator.SurfaceElement, StringComparison.Ordinal)
        && element.GetValue(attributeName: "Kind") is "Tray" or "Global";

    private static bool IsTrue(string? value) => string.Equals(value, b: "True", StringComparison.OrdinalIgnoreCase);

    private static void PushChildren(Stack<ControlMapElement> pending, List<ControlMapElement> children)
    {
        for (int childIndex = children.Count - 1; childIndex >= 0; childIndex--)
            pending.Push(children[childIndex]);
    }

    private sealed class RequiredNode(
        string key,
        string path,
        string elementName,
        string filePath,
        int line,
        int column)
    {
        public readonly string Key = key;
        public readonly string Path = path;
        public readonly string ElementName = elementName;
        public readonly string FilePath = filePath;
        public readonly int Line = line;
        public readonly int Column = column;
    }
}
