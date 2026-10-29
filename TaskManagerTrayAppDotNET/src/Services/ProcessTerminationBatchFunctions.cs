namespace TaskManagerTrayAppDotNET.Services;

/// <summary>
/// One process that could not be ended, and whether standard rights were refused. A descendant failure names a
/// child of a selected process tree, identified by its own PID and creation time.
/// </summary>
internal readonly record struct ProcessTerminationFailure(
    ProcessEndTaskItem Process,
    string ErrorMessage,
    bool AccessDenied,
    bool IsDescendant = false);

/// <summary>The outcome of ending a selection; messages are formatted against the original selection size.</summary>
internal sealed record ProcessTerminationBatchResult(
    bool RefreshNeeded,
    IReadOnlyList<ProcessTerminationFailure> Failures,
    bool IsMultiple)
{
    public string ErrorMessage { get; } = ProcessTerminationBatchFunctions.FormatFailures(Failures, IsMultiple);
}

internal delegate bool TryTerminateDescendantsAction(
    ProcessTerminationTarget target,
    TryTerminateProcessAction terminateProcess,
    out string errorMessage);

/// <summary>Terminates a process selection while treating vanished identities as completed work.</summary>
internal static class ProcessTerminationBatchFunctions
{
    public static ProcessTerminationBatchResult Execute(
        IReadOnlyList<ProcessEndTaskItem> processes,
        TryTerminateProcessAction terminateProcess,
        Func<ProcessTerminationTarget, bool> isTargetGone,
        Func<ProcessTerminationTarget, bool>? isAccessDenied = null)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(terminateProcess);
        ArgumentNullException.ThrowIfNull(isTargetGone);
        if (processes.Count == 0)
            throw new ArgumentException("At least one process is required.", nameof(processes));

        List<ProcessTerminationFailure> failures = [];
        bool refreshNeeded = AddRootFailures(processes, terminateProcess, isTargetGone, isAccessDenied, failures);
        return new ProcessTerminationBatchResult(refreshNeeded, failures, processes.Count > 1);
    }

    /// <summary>Ends each selected process's descendants deepest-first, then the selected roots.</summary>
    public static ProcessTerminationBatchResult ExecuteTrees(
        IReadOnlyList<ProcessEndTaskItem> processes,
        TryTerminateProcessAction terminateProcess,
        TryTerminateDescendantsAction terminateDescendants,
        Func<ProcessTerminationTarget, bool> isTargetGone,
        Func<ProcessTerminationTarget, bool>? isAccessDenied = null)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(terminateProcess);
        ArgumentNullException.ThrowIfNull(terminateDescendants);
        ArgumentNullException.ThrowIfNull(isTargetGone);
        if (processes.Count == 0)
            throw new ArgumentException("At least one process is required.", nameof(processes));

        List<ProcessTerminationFailure> failures = [];
        bool refreshNeeded = false;
        for (int processIndex = 0; processIndex < processes.Count; processIndex++)
        {
            ProcessEndTaskItem process = processes[processIndex];
            if (isTargetGone(process.Target))
            {
                refreshNeeded = true;
                continue;
            }

            // A refused descendant becomes its own failure so the elevated retry targets exactly that instance,
            // even after its root has ended; deferring it keeps it out of the root's other descendant errors
            List<ProcessTerminationFailure> refusedDescendants = [];
            bool TerminateDescendant(ProcessTerminationTarget target, out string errorMessage)
            {
                if (terminateProcess(target, out errorMessage)) return true;
                if (isAccessDenied?.Invoke(target) != true) return false;

                refusedDescendants.Add(new ProcessTerminationFailure(
                    new ProcessEndTaskItem(target, ProcessName: string.Empty),
                    errorMessage,
                    AccessDenied: true,
                    IsDescendant: true));
                return true;
            }

            bool descendantsEnded = terminateDescendants(process.Target, TerminateDescendant, out string descendantError);

            // Descendant termination can partially succeed before reporting a failure
            refreshNeeded = true;
            if (!descendantsEnded && !isTargetGone(process.Target))
                failures.Add(new ProcessTerminationFailure(process, descendantError, AccessDenied: false));
            failures.AddRange(refusedDescendants);
        }

        refreshNeeded |= AddRootFailures(processes, terminateProcess, isTargetGone, isAccessDenied, failures);
        return new ProcessTerminationBatchResult(refreshNeeded, failures, processes.Count > 1);
    }

    private static bool AddRootFailures(
        IReadOnlyList<ProcessEndTaskItem> processes,
        TryTerminateProcessAction terminateProcess,
        Func<ProcessTerminationTarget, bool> isTargetGone,
        Func<ProcessTerminationTarget, bool>? isAccessDenied,
        List<ProcessTerminationFailure> failures)
    {
        bool refreshNeeded = false;
        for (int processIndex = 0; processIndex < processes.Count; processIndex++)
        {
            ProcessEndTaskItem process = processes[processIndex];
            if (isTargetGone(process.Target))
            {
                refreshNeeded = true;
                continue;
            }

            if (terminateProcess(process.Target, out string errorMessage))
            {
                refreshNeeded = true;
                continue;
            }

            if (isTargetGone(process.Target))
            {
                refreshNeeded = true;
                continue;
            }

            failures.Add(new ProcessTerminationFailure(
                process,
                errorMessage,
                isAccessDenied?.Invoke(process.Target) == true));
        }

        return refreshNeeded;
    }

    internal static string FormatFailures(IReadOnlyList<ProcessTerminationFailure> failures, bool isMultiple)
    {
        ArgumentNullException.ThrowIfNull(failures);
        if (failures.Count == 0) return string.Empty;

        string[] lines = new string[failures.Count];
        for (int failureIndex = 0; failureIndex < failures.Count; failureIndex++)
        {
            ProcessTerminationFailure failure = failures[failureIndex];
            // A child always names itself, since the selection alone doesn't identify it
            lines[failureIndex] = failure.IsDescendant
                ? $"Child process PID {failure.Process.Target.ProcessID}: {NormalizeFailure(failure.ErrorMessage)}"
                : isMultiple
                    ? FormatFailure(failure.Process, failure.ErrorMessage)
                    : NormalizeFailure(failure.ErrorMessage);
        }

        return string.Join(separator: "\n", lines);
    }

    internal static string FormatFailure(ProcessEndTaskItem process, string errorMessage)
    {
        string processName = string.IsNullOrWhiteSpace(process.ProcessName)
            ? $"PID {process.Target.ProcessID}"
            : $"{process.ProcessName} (PID {process.Target.ProcessID})";
        return $"{processName}: {NormalizeFailure(errorMessage)}";
    }

    private static string NormalizeFailure(string errorMessage) =>
        string.IsNullOrWhiteSpace(errorMessage)
            ? "The process action failed."
            : errorMessage;
}
