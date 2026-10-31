namespace TrayAppDotNETCommon.AxamlPropertyLinker;

internal enum AXAMLProvenanceItemKind
{
    ResourceDefinition,
    PropertyAssignment,
    ResourceReference,
    Style,
    StyleSetter,
    ControlTheme,
    Template,
    Binding
}

internal sealed record AXAMLProvenanceDocument(
    string SourcePath,
    string OwnerTypeName,
    EquatableArray<AXAMLProvenanceItem> Entries);

internal sealed record AXAMLProvenanceItem(
    AXAMLProvenanceItemKind Kind,
    int Line,
    int Column,
    string ElementTypeName,
    string ElementPath,
    string? ControlName,
    string? PropertyName,
    string? ResourceKey,
    string? ValueExpression,
    string? Selector);
