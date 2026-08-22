using System.Collections.Immutable;

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

internal sealed class AXAMLProvenanceDocument(
    string sourcePath,
    string ownerTypeName,
    ImmutableArray<AXAMLProvenanceItem> entries)
{
    public readonly string SourcePath = sourcePath;
    public readonly string OwnerTypeName = ownerTypeName;
    public readonly ImmutableArray<AXAMLProvenanceItem> Entries = entries;
}

internal sealed class AXAMLProvenanceItem(
    AXAMLProvenanceItemKind kind,
    int line,
    int column,
    string elementTypeName,
    string elementPath,
    string? controlName,
    string? propertyName,
    string? resourceKey,
    string? valueExpression,
    string? selector)
{
    public readonly AXAMLProvenanceItemKind Kind = kind;
    public readonly int Line = line;
    public readonly int Column = column;
    public readonly string ElementTypeName = elementTypeName;
    public readonly string ElementPath = elementPath;
    public readonly string? ControlName = controlName;
    public readonly string? PropertyName = propertyName;
    public readonly string? ResourceKey = resourceKey;
    public readonly string? ValueExpression = valueExpression;
    public readonly string? Selector = selector;
}
