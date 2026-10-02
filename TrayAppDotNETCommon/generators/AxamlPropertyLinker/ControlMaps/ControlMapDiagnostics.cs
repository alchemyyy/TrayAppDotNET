using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace TrayAppDotNETCommon.AxamlPropertyLinker;

/// <summary>Errors the control map generator reports against control map AXAML files.</summary>
internal static class ControlMapDiagnostics
{
    private const string Category = "TrayAppDotNET.ControlMap";

    public static readonly DiagnosticDescriptor InvalidDocument = Create(
        id: "TADNCM001",
        title: "Invalid control map document",
        messageFormat: "{0}");

    public static readonly DiagnosticDescriptor UnexpectedElement = Create(
        id: "TADNCM002",
        title: "Unexpected control map element",
        messageFormat: "<{0}> is not allowed {1}");

    public static readonly DiagnosticDescriptor UnknownAttribute = Create(
        id: "TADNCM003",
        title: "Unknown control map attribute",
        messageFormat: "Attribute '{0}' is not valid on <{1}>; allowed: {2}");

    public static readonly DiagnosticDescriptor InvalidID = Create(
        id: "TADNCM004",
        title: "Invalid control map id",
        messageFormat: "ID '{0}' {1}");

    public static readonly DiagnosticDescriptor DuplicateID = Create(
        id: "TADNCM005",
        title: "Duplicate control map id",
        messageFormat: "ID '{0}' is already used by a sibling on line {1}");

    public static readonly DiagnosticDescriptor InvalidValue = Create(
        id: "TADNCM006",
        title: "Invalid control map attribute value",
        messageFormat: "'{0}' is not a valid {1} value; allowed: {2}");

    public static readonly DiagnosticDescriptor InvalidGesture = Create(
        id: "TADNCM007",
        title: "Invalid control map gesture",
        messageFormat: "{0} '{1}': {2}");

    public static readonly DiagnosticDescriptor InvalidKeyScope = Create(
        id: "TADNCM008",
        title: "Invalid control map key scope",
        messageFormat: "{0}");

    public static readonly DiagnosticDescriptor UnknownTemplate = Create(
        id: "TADNCM009",
        title: "Unknown control map template",
        messageFormat: "Template '{0}' is not defined in this map{1}");

    public static readonly DiagnosticDescriptor InvalidSlot = Create(
        id: "TADNCM010",
        title: "Invalid control map slot",
        messageFormat: "{0}");

    public static readonly DiagnosticDescriptor MissingAttribute = Create(
        id: "TADNCM011",
        title: "Missing control map attribute",
        messageFormat: "<{0}> requires the '{1}' attribute");

    public static readonly DiagnosticDescriptor InvalidCombination = Create(
        id: "TADNCM012",
        title: "Invalid control map attribute combination",
        messageFormat: "{0}");

    public static readonly DiagnosticDescriptor DuplicateGesture = Create(
        id: "TADNCM013",
        title: "Duplicate control map accelerator",
        messageFormat: "Keys gesture '{0}' is already claimed by '{1}' in key scope '{2}'");

    public static readonly DiagnosticDescriptor EmptyContainer = Create(
        id: "TADNCM014",
        title: "Empty control map container",
        messageFormat: "<{0} ID=\"{1}\"> has no children{2}");

    public static readonly DiagnosticDescriptor TemplateCycle = Create(
        id: "TADNCM015",
        title: "Recursive control map template",
        messageFormat: "Template '{0}' instantiates itself through {1}");

    public static readonly DiagnosticDescriptor MissingOwner = Create(
        id: "TADNCM016",
        title: "Missing control map owner",
        messageFormat: "x:Class '{0}' needs a hand-written partial class deriving from " +
                       "TrayAppDotNETCommon.UI.ControlMapping.ControlMap whose constructor passes MapName to the base " +
                       "and calls AvaloniaXamlLoader.Load(this)");

    public static readonly DiagnosticDescriptor InvalidVariant = Create(
        id: "TADNCM017",
        title: "Invalid control map variant",
        messageFormat: "{0}");

    // A warning rather than an error: it measures how far code has caught up with the map
    public static readonly DiagnosticDescriptor UntaggedNode = new(
        id: "TADNCM100",
        title: "Control map node is never tagged",
        messageFormat: "{0} '{1}' is never tagged in code; call MapTo, or MapCommand for a command, with its id",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>Creates a diagnostic at a one-based line and column of a control map file.</summary>
    public static Diagnostic At(
        DiagnosticDescriptor descriptor,
        string path,
        int line,
        int column,
        params object[] arguments)
    {
        LinePosition position = new(Math.Max(val1: 0, line - 1), Math.Max(val1: 0, column - 1));
        Location location = Location.Create(path, textSpan: default, new LinePositionSpan(position, position));
        return Diagnostic.Create(descriptor, location, arguments);
    }

    private static DiagnosticDescriptor Create(string id, string title, string messageFormat) =>
        new(id, title, messageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
