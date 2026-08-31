#if DEBUG
using System.Runtime.CompilerServices;
using Avalonia;

namespace TrayAppDotNETCommon.UI.Debugging;

/// <summary>Weakly associates controls and Avalonia properties with instrumented assignment history.</summary>
internal static class DebugPropertyProvenanceRegistry
{
    private const int MaximumRetainedAssignmentsPerProperty = 256;
    private const int MaximumExpressionLength = 512;
    private const int MaximumMemberNameLength = 128;
    private const int MaximumResourceKeyLength = 256;

    private static readonly ConditionalWeakTable<AvaloniaObject, ObjectAssignments> AssignmentsByObject = new();
    private static long _nextSequence;

    public static void Record(
        AvaloniaObject target,
        AvaloniaProperty property,
        object? value,
        DebugPropertyAssignmentOperation operation,
        string valueExpression,
        string sourceFilePath,
        int sourceLine,
        int sourceColumn,
        string sourceMember,
        string? resourceKey)
    {
        DebugValueSnapshot valueSnapshot = DebugValueSnapshot.Create(value);
        DebugPropertyAssignment assignment = new(
            Interlocked.Increment(ref _nextSequence),
            DateTimeOffset.UtcNow,
            Environment.CurrentManagedThreadId,
            operation,
            valueSnapshot.TypeName,
            valueSnapshot.Display,
            Bound(valueExpression, MaximumExpressionLength),
            DebugSourcePath.Normalize(sourceFilePath),
            sourceLine,
            sourceColumn,
            Bound(sourceMember, MaximumMemberNameLength),
            resourceKey == null ? null : Bound(resourceKey, MaximumResourceKeyLength));

        ObjectAssignments objectAssignments = AssignmentsByObject.GetValue(
            target,
            static _ => new ObjectAssignments());
        objectAssignments.Record(property, assignment);
    }

    private static string Bound(string value, int maximumLength) =>
        value.Length <= maximumLength
            ? value
            : value[..(maximumLength - 3)] + "...";

    public static DebugPropertyAssignmentHistory GetHistory(AvaloniaObject target, AvaloniaProperty property)
    {
        return AssignmentsByObject.TryGetValue(target, out ObjectAssignments? assignments)
            ? assignments.GetHistory(property)
            : new DebugPropertyAssignmentHistory([], DiscardedAssignmentCount: 0);
    }

    public static DebugPropertyAssignmentHistory GetRecentHistory(
        AvaloniaObject target,
        AvaloniaProperty property,
        int maximumAssignments)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAssignments);
        return AssignmentsByObject.TryGetValue(target, out ObjectAssignments? assignments)
            ? assignments.GetHistory(property, maximumAssignments)
            : new DebugPropertyAssignmentHistory([], DiscardedAssignmentCount: 0);
    }

    private sealed class ObjectAssignments
    {
        private readonly Lock _sync = new();
        private readonly Dictionary<AvaloniaProperty, PropertyAssignments> _assignmentsByProperty = [];

        public void Record(AvaloniaProperty property, DebugPropertyAssignment assignment)
        {
            lock (_sync)
            {
                if (!_assignmentsByProperty.TryGetValue(property, out PropertyAssignments? assignments))
                {
                    assignments = new PropertyAssignments();
                    _assignmentsByProperty.Add(property, assignments);
                }

                assignments.Record(assignment);
            }
        }

        public DebugPropertyAssignmentHistory GetHistory(AvaloniaProperty property)
        {
            lock (_sync)
            {
                return _assignmentsByProperty.TryGetValue(property, out PropertyAssignments? assignments)
                    ? assignments.Snapshot()
                    : new DebugPropertyAssignmentHistory([], DiscardedAssignmentCount: 0);
            }
        }

        public DebugPropertyAssignmentHistory GetHistory(
            AvaloniaProperty property,
            int maximumAssignments)
        {
            lock (_sync)
            {
                return _assignmentsByProperty.TryGetValue(property, out PropertyAssignments? assignments)
                    ? assignments.Snapshot(maximumAssignments)
                    : new DebugPropertyAssignmentHistory([], DiscardedAssignmentCount: 0);
            }
        }
    }

    private sealed class PropertyAssignments
    {
        private readonly Queue<DebugPropertyAssignment> _assignments = new();
        private long _discardedAssignmentCount;

        public void Record(DebugPropertyAssignment assignment)
        {
            if (_assignments.Count == MaximumRetainedAssignmentsPerProperty)
            {
                _ = _assignments.Dequeue();
                _discardedAssignmentCount++;
            }

            _assignments.Enqueue(assignment);
        }

        public DebugPropertyAssignmentHistory Snapshot() =>
            new(_assignments.ToArray(), _discardedAssignmentCount);

        public DebugPropertyAssignmentHistory Snapshot(int maximumAssignments)
        {
            int returnedAssignmentCount = Math.Min(_assignments.Count, maximumAssignments);
            int skippedAssignmentCount = _assignments.Count - returnedAssignmentCount;
            List<DebugPropertyAssignment> recentAssignments = new(returnedAssignmentCount);
            int assignmentIndex = 0;
            foreach (DebugPropertyAssignment assignment in _assignments)
            {
                if (assignmentIndex >= skippedAssignmentCount)
                    recentAssignments.Add(assignment);

                assignmentIndex++;
            }

            return new DebugPropertyAssignmentHistory(
                recentAssignments,
                _discardedAssignmentCount + skippedAssignmentCount);
        }
    }
}
#endif
