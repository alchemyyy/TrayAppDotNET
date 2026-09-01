namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Identifies one process instance without relying on a reusable PID alone.</summary>
internal readonly record struct ProcessTerminationTarget(
    int ProcessID,
    long CreationTimeFileTime);

/// <summary>Pairs one identity-checked process instance with its confirmation display name.</summary>
internal readonly record struct ProcessEndTaskItem(
    ProcessTerminationTarget Target,
    string ProcessName);

/// <summary>Captures the immutable process selection used by one end-task operation.</summary>
internal sealed class ProcessEndTaskRequest
{
    private readonly ProcessEndTaskItem[] _processes;

    public ProcessEndTaskRequest(IReadOnlyList<ProcessEndTaskItem> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        if (processes.Count == 0)
            throw new ArgumentException("At least one process is required.", nameof(processes));

        _processes = new ProcessEndTaskItem[processes.Count];
        for (int processIndex = 0; processIndex < processes.Count; processIndex++)
            _processes[processIndex] = processes[processIndex];
    }

    public IReadOnlyList<ProcessEndTaskItem> Processes => _processes;

    public int Count => _processes.Length;
}
