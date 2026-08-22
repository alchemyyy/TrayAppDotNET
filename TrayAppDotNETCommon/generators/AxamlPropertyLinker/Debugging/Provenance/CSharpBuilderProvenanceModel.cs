using System.Collections.Immutable;

namespace TrayAppDotNETCommon.AxamlPropertyLinker;

internal sealed record CSharpBuilderBoundary(
    string SourcePath,
    int BoundaryLine,
    ImmutableArray<CSharpBuilderAssignment> Assignments);

internal readonly record struct CSharpBuilderAssignment(
    string PropertyReference,
    string Operation,
    string ValueExpression,
    int SourceLine,
    int SourceColumn,
    string SourceMember,
    string? ResourceKey);

internal readonly record struct CSharpBuilderProvenanceSettings(
    string RootNamespace,
    string ProjectDirectory);
