namespace TaskManagerTrayAppDotNET.Models;

public enum ProcessTableColumnKind : byte
{
    Name,
    ProcessID,
    Status,
    UserName,
    SessionID,
    JobObjectID,
    CPU,
    CPUSingle,
    CPUTime,
    Lifetime,
    Cycle,
    WorkingSet,
    PeakWorkingSet,
    WorkingSetDelta,
    ActivePrivateWorkingSet,
    PrivateMemory,
    SharedWorkingSet,
    Disk,
    Network,
    CommitSize,
    PagedPool,
    NonPagedPool,
    PageFaults,
    PageFaultDelta,
    BasePriority,
    Handles,
    Threads,
    UserObjects,
    GDIObjects,
    IOReads,
    IOWrites,
    IOOther,
    IOReadBytes,
    IOWriteBytes,
    IOOtherBytes,
    ImagePath,
    CommandLine,
    OperatingSystemContext,
    Platform,
    Elevated,
    UACVirtualization,
    Description,
    DataExecutionPrevention,
    IOPriority,
    PackageName,
    EnterpriseContext,
    PowerThrottling,
    GPU,
    GPUEngine,
    DedicatedGPUMemory,
    SharedGPUMemory,
    DPIAwareness,
    Architecture,
    HardwareStackProtection,
    ExtendedControlFlowGuard,
    Isolation,
    NPU,
    NPUEngine,
    DedicatedNPUMemory,
    SharedNPUMemory,
    CPUUtility
}

internal enum ProcessTableColumnLifetime : byte
{
    Static,
    Dynamic
}

internal enum ProcessTableColumnAlignment : byte
{
    Left,
    Right
}

internal readonly record struct ProcessTableColumnDefinition(
    ProcessTableColumnKind Kind,
    string Title,
    ProcessTableColumnLifetime Lifetime,
    double DefaultWidth,
    ProcessTableColumnAlignment Alignment,
    bool DefaultVisible);

/// <summary>Complete Processes-column catalog for the current Windows Task Manager surface.</summary>
internal static class ProcessTableColumnCatalog
{
    // AXAML hot-reload exception: These model defaults seed persisted column settings before
    // Avalonia resources are available; runtime-tunable displayed widths live in TaskManagerWindow.axaml
    private const double NarrowWidth = 76;
    private const double BooleanWidth = 104;
    private const double CounterWidth = 112;
    private const double MemoryWidth = 142;
    private const double TextWidth = 180;
    private const double LongTextWidth = 420;

    public static readonly ProcessTableColumnDefinition[] Definitions =
    [
        Static(ProcessTableColumnKind.Name, title: "Name", width: 280, ProcessTableColumnAlignment.Left, visible: true),
        Static(ProcessTableColumnKind.ProcessID, title: "PID", width: 52.67, ProcessTableColumnAlignment.Right,
            visible: true),
        Dynamic(ProcessTableColumnKind.Status, title: "Status", width: 48, ProcessTableColumnAlignment.Left,
            visible: true),
        Static(ProcessTableColumnKind.UserName, title: "User name", width: 140, ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.SessionID, title: "Session ID", NarrowWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.JobObjectID, title: "Job object ID", CounterWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.CPU, title: "CPU", width: 58.67, ProcessTableColumnAlignment.Right,
            visible: true),
        Dynamic(ProcessTableColumnKind.CPUSingle, title: "CPU (single)", width: 83.33,
            ProcessTableColumnAlignment.Right, visible: true),
        Dynamic(ProcessTableColumnKind.CPUTime, title: "CPU time", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.Lifetime, title: "Lifetime", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.Cycle, title: "Cycle", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.WorkingSet, title: "Working set (memory)", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.PeakWorkingSet, title: "Peak working set (memory)", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.WorkingSetDelta, title: "Working set delta (memory)", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.ActivePrivateWorkingSet, title: "Memory (active private working set)",
            MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.PrivateMemory, title: "Memory (private working set)", width: 98.67,
            ProcessTableColumnAlignment.Right, visible: true),
        Dynamic(ProcessTableColumnKind.SharedWorkingSet, title: "Memory (shared working set)", width: 95.33,
            ProcessTableColumnAlignment.Right, visible: true),
        Dynamic(ProcessTableColumnKind.Disk, title: "Disk", width: 69.33, ProcessTableColumnAlignment.Right,
            visible: true),
        Dynamic(ProcessTableColumnKind.Network, title: "Network", width: 84.67, ProcessTableColumnAlignment.Right,
            visible: true),
        Dynamic(ProcessTableColumnKind.CommitSize, title: "Commit size", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.PagedPool, title: "Paged pool", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.NonPagedPool, title: "NP pool", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.PageFaults, title: "Page faults", CounterWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.PageFaultDelta, title: "PF Delta", CounterWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.BasePriority, title: "Base priority", CounterWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.Handles, title: "Handles", width: 66, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.Threads, title: "Threads", width: 62, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.UserObjects, title: "User objects", CounterWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.GDIObjects, title: "GDI objects", width: 44,
            ProcessTableColumnAlignment.Right, visible: true),
        Dynamic(ProcessTableColumnKind.IOReads, title: "I/O reads", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.IOWrites, title: "I/O writes", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.IOOther, title: "I/O other", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.IOReadBytes, title: "I/O read bytes", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.IOWriteBytes, title: "I/O write bytes", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.IOOtherBytes, title: "I/O other bytes", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Static(ProcessTableColumnKind.ImagePath, title: "Image path name", LongTextWidth,
            ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.CommandLine, title: "Command line", width: 946, ProcessTableColumnAlignment.Left,
            visible: true),
        Static(ProcessTableColumnKind.OperatingSystemContext, title: "Operating system context", TextWidth,
            ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.Platform, title: "Platform", CounterWidth, ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.Elevated, title: "Elevated", BooleanWidth, ProcessTableColumnAlignment.Left),
        Dynamic(ProcessTableColumnKind.UACVirtualization, title: "UAC virtualization", width: 150,
            ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.Description, title: "Description", width: 240, ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.DataExecutionPrevention, title: "Data execution prevention", width: 190,
            ProcessTableColumnAlignment.Left),
        Dynamic(ProcessTableColumnKind.IOPriority, title: "I/O priority", CounterWidth,
            ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.PackageName, title: "Package name", width: 220, ProcessTableColumnAlignment.Left),
        Dynamic(ProcessTableColumnKind.EnterpriseContext, title: "Enterprise context", TextWidth,
            ProcessTableColumnAlignment.Left),
        Dynamic(ProcessTableColumnKind.PowerThrottling, title: "Power throttling", width: 140,
            ProcessTableColumnAlignment.Left),
        Dynamic(ProcessTableColumnKind.GPU, title: "GPU", NarrowWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.GPUEngine, title: "GPU engine", TextWidth, ProcessTableColumnAlignment.Left),
        Dynamic(ProcessTableColumnKind.DedicatedGPUMemory, title: "Dedicated GPU memory", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.SharedGPUMemory, title: "Shared GPU memory", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.DPIAwareness, title: "DPI awareness", width: 150,
            ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.Architecture, title: "Architecture", CounterWidth,
            ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.HardwareStackProtection, title: "Hardware-enforced Stack Protection", width: 250,
            ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.ExtendedControlFlowGuard, title: "Extended Control Flow Guard", width: 220,
            ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.Isolation, title: "Isolation", CounterWidth, ProcessTableColumnAlignment.Left),
        Dynamic(ProcessTableColumnKind.NPU, title: "NPU", NarrowWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.NPUEngine, title: "NPU engine", TextWidth, ProcessTableColumnAlignment.Left),
        Dynamic(ProcessTableColumnKind.DedicatedNPUMemory, title: "Dedicated NPU memory", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.SharedNPUMemory, title: "Shared NPU memory", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.CPUUtility, title: "CPU utility", CounterWidth,
            ProcessTableColumnAlignment.Right)
    ];

    public static readonly ulong StaticMask = CreateLifetimeMask(ProcessTableColumnLifetime.Static);
    public static readonly ulong DynamicMask = CreateLifetimeMask(ProcessTableColumnLifetime.Dynamic);

    public static ProcessTableColumnDefinition Get(ProcessTableColumnKind kind) => Definitions[(int)kind];

    /// <summary>Returns whether a column's primary sort places its highest value first.</summary>
    public static bool SortsDescendingByDefault(ProcessTableColumnKind kind) =>
        Get(kind).Alignment == ProcessTableColumnAlignment.Right;

    public static ulong GetMask(ProcessTableColumnKind kind) => 1UL << (int)kind;

    public static bool Contains(ulong mask, ProcessTableColumnKind kind) =>
        (mask & GetMask(kind)) != 0;

    public static ulong CreateVisibleMask(IReadOnlyList<ProcessColumnSetting> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        ulong mask = 0;
        for (int settingIndex = 0; settingIndex < settings.Count; settingIndex++)
        {
            ProcessColumnSetting setting = settings[settingIndex];
            if (!setting.Visible || !Enum.IsDefined(setting.Column)) continue;
            mask |= GetMask(setting.Column);
        }

        return mask;
    }

    private static ProcessTableColumnDefinition Static(
        ProcessTableColumnKind kind,
        string title,
        double width,
        ProcessTableColumnAlignment alignment,
        bool visible = false) =>
        new(kind, title, ProcessTableColumnLifetime.Static, width, alignment, visible);

    private static ProcessTableColumnDefinition Dynamic(
        ProcessTableColumnKind kind,
        string title,
        double width,
        ProcessTableColumnAlignment alignment,
        bool visible = false) =>
        new(kind, title, ProcessTableColumnLifetime.Dynamic, width, alignment, visible);

    private static ulong CreateLifetimeMask(ProcessTableColumnLifetime lifetime)
    {
        ulong mask = 0;
        for (int definitionIndex = 0; definitionIndex < Definitions.Length; definitionIndex++)
        {
            ProcessTableColumnDefinition definition = Definitions[definitionIndex];
            if (definition.Lifetime != lifetime) continue;
            mask |= GetMask(definition.Kind);
        }

        return mask;
    }
}
