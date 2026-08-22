namespace TaskManagerTrayAppDotNET.Models;

internal enum ProcessExecutionState : byte
{
    Running,
    Suspended
}

internal enum ProcessOwnerKind : byte
{
    CurrentUser,
    System,
    Unavailable
}

internal struct ProcessSnapshotRow
{
    public int ProcessID;
    public string Name;
    public ProcessExecutionState State;
    public ProcessOwnerKind Owner;
    public double CPUPercent;
    public long PrivateMemoryBytes;
    public long WorkingSetBytes;
    public string? CommandLine;
}
