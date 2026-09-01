namespace TaskManagerTrayAppDotNET.Services;

internal readonly record struct ProcessTerminationBatchResult(
    bool RefreshNeeded,
    string ErrorMessage);

/// <summary>Terminates a process selection while treating vanished identities as completed work.</summary>
internal static class ProcessTerminationBatchFunctions
{
    public static ProcessTerminationBatchResult Execute(
        IReadOnlyList<ProcessEndTaskItem> processes,
        TryTerminateProcessAction terminateProcess,
        Func<ProcessTerminationTarget, bool> isTargetGone)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(terminateProcess);
        ArgumentNullException.ThrowIfNull(isTargetGone);
        if (processes.Count == 0)
            throw new ArgumentException("At least one process is required.", nameof(processes));

        List<string> failures = [];
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

            failures.Add(processes.Count > 1
                ? FormatFailure(process, errorMessage)
                : NormalizeFailure(errorMessage));
        }

        return new ProcessTerminationBatchResult(
            refreshNeeded,
            string.Join(separator: "\n", failures));
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
