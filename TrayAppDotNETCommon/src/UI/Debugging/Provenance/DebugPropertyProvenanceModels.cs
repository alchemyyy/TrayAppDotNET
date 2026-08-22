#if DEBUG
namespace TrayAppDotNETCommon.UI.Debugging;

/// <summary>Describes one instrumented assignment to an Avalonia property.</summary>
internal readonly record struct DebugPropertyAssignment(
    long Sequence,
    DateTimeOffset Timestamp,
    int ManagedThreadID,
    DebugPropertyAssignmentOperation Operation,
    string ValueTypeName,
    string ValueDisplay,
    string ValueExpression,
    string SourcePath,
    int SourceLine,
    int SourceColumn,
    string SourceMember,
    string? ResourceKey);

/// <summary>Contains retained assignment history and its truncation count.</summary>
internal readonly record struct DebugPropertyAssignmentHistory(
    IReadOnlyList<DebugPropertyAssignment> Assignments,
    long DiscardedAssignmentCount)
{
    public long TotalAssignmentCount => DiscardedAssignmentCount + Assignments.Count;
}
#endif
