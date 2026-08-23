using Avalonia;

namespace TaskManagerTrayAppDotNET.UI;

public enum ProcessTableColumnKind : byte
{
    Name,
    ProcessID,
    Status,
    UserName,
    SessionID,
    JobObjectID,
    CPU,
    CPUTime,
    Cycle,
    WorkingSet,
    PeakWorkingSet,
    WorkingSetDelta,
    ActivePrivateWorkingSet,
    PrivateMemory,
    SharedWorkingSet,
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

internal readonly record struct ProcessTableMetrics(
    double HeaderHeight,
    double RowHeight,
    double CellPadding,
    double FontSize,
    double HeaderFontSize,
    double ProcessIconSize,
    double ProcessIconGap);

internal readonly record struct ProcessTableColumn(
    ProcessTableColumnKind Kind,
    string Title,
    double Left,
    double Width,
    ProcessTableColumnAlignment Alignment)
{
    public double Right => Left + Width;
}

internal readonly record struct ProcessTableColumnDefinition(
    ProcessTableColumnKind Kind,
    string Title,
    ProcessTableColumnLifetime Lifetime,
    double DefaultWidth,
    ProcessTableColumnAlignment Alignment,
    bool DefaultVisible);

/// <summary>Complete Details-column catalog for the current Windows Task Manager surface.</summary>
internal static class ProcessTableColumnCatalog
{
    private const double NarrowWidth = 76;
    private const double BooleanWidth = 104;
    private const double CounterWidth = 112;
    private const double MemoryWidth = 142;
    private const double TextWidth = 180;
    private const double LongTextWidth = 420;

    public static readonly ProcessTableColumnDefinition[] Definitions =
    [
        Static(ProcessTableColumnKind.Name, "Name", 280, ProcessTableColumnAlignment.Left, true),
        Static(ProcessTableColumnKind.ProcessID, "PID", 82, ProcessTableColumnAlignment.Right, true),
        Dynamic(ProcessTableColumnKind.Status, "Status", 106, ProcessTableColumnAlignment.Left, true),
        Static(ProcessTableColumnKind.UserName, "User name", 140, ProcessTableColumnAlignment.Left, true),
        Static(ProcessTableColumnKind.SessionID, "Session ID", NarrowWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.JobObjectID, "Job object ID", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.CPU, "CPU", 68, ProcessTableColumnAlignment.Right, true),
        Dynamic(ProcessTableColumnKind.CPUTime, "CPU time", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.Cycle, "Cycle", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.WorkingSet, "Working set (memory)", MemoryWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.PeakWorkingSet, "Peak working set (memory)", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.WorkingSetDelta, "Working set delta (memory)", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.ActivePrivateWorkingSet, "Memory (active private working set)", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.PrivateMemory, "Memory (private working set)", 136,
            ProcessTableColumnAlignment.Right, true),
        Dynamic(ProcessTableColumnKind.SharedWorkingSet, "Memory (shared working set)", 136,
            ProcessTableColumnAlignment.Right, true),
        Dynamic(ProcessTableColumnKind.CommitSize, "Commit size", MemoryWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.PagedPool, "Paged pool", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.NonPagedPool, "NP pool", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.PageFaults, "Page faults", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.PageFaultDelta, "PF Delta", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.BasePriority, "Base priority", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.Handles, "Handles", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.Threads, "Threads", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.UserObjects, "User objects", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.GDIObjects, "GDI objects", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.IOReads, "I/O reads", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.IOWrites, "I/O writes", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.IOOther, "I/O other", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.IOReadBytes, "I/O read bytes", MemoryWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.IOWriteBytes, "I/O write bytes", MemoryWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.IOOtherBytes, "I/O other bytes", MemoryWidth, ProcessTableColumnAlignment.Right),
        Static(ProcessTableColumnKind.ImagePath, "Image path name", LongTextWidth, ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.CommandLine, "Command line", 520, ProcessTableColumnAlignment.Left, true),
        Static(ProcessTableColumnKind.OperatingSystemContext, "Operating system context", TextWidth,
            ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.Platform, "Platform", CounterWidth, ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.Elevated, "Elevated", BooleanWidth, ProcessTableColumnAlignment.Left),
        Dynamic(ProcessTableColumnKind.UACVirtualization, "UAC virtualization", 150,
            ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.Description, "Description", 240, ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.DataExecutionPrevention, "Data execution prevention", 190,
            ProcessTableColumnAlignment.Left),
        Dynamic(ProcessTableColumnKind.IOPriority, "I/O priority", CounterWidth, ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.PackageName, "Package name", 220, ProcessTableColumnAlignment.Left),
        Dynamic(ProcessTableColumnKind.EnterpriseContext, "Enterprise context", TextWidth,
            ProcessTableColumnAlignment.Left),
        Dynamic(ProcessTableColumnKind.PowerThrottling, "Power throttling", 140,
            ProcessTableColumnAlignment.Left),
        Dynamic(ProcessTableColumnKind.GPU, "GPU", NarrowWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.GPUEngine, "GPU engine", TextWidth, ProcessTableColumnAlignment.Left),
        Dynamic(ProcessTableColumnKind.DedicatedGPUMemory, "Dedicated GPU memory", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.SharedGPUMemory, "Shared GPU memory", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.DPIAwareness, "DPI awareness", 150, ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.Architecture, "Architecture", CounterWidth, ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.HardwareStackProtection, "Hardware-enforced Stack Protection", 250,
            ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.ExtendedControlFlowGuard, "Extended Control Flow Guard", 220,
            ProcessTableColumnAlignment.Left),
        Static(ProcessTableColumnKind.Isolation, "Isolation", CounterWidth, ProcessTableColumnAlignment.Left),
        Dynamic(ProcessTableColumnKind.NPU, "NPU", NarrowWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.NPUEngine, "NPU engine", TextWidth, ProcessTableColumnAlignment.Left),
        Dynamic(ProcessTableColumnKind.DedicatedNPUMemory, "Dedicated NPU memory", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.SharedNPUMemory, "Shared NPU memory", MemoryWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.CPUUtility, "CPU utility", CounterWidth, ProcessTableColumnAlignment.Right)
    ];

    public static readonly ulong StaticMask = CreateLifetimeMask(ProcessTableColumnLifetime.Static);
    public static readonly ulong DynamicMask = CreateLifetimeMask(ProcessTableColumnLifetime.Dynamic);

    public static ProcessTableColumnDefinition Get(ProcessTableColumnKind kind) => Definitions[(int)kind];

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

    public static ProcessTableColumn[] CreateDefaultLayout()
    {
        List<ProcessTableColumn> columns = [];
        double left = 0;
        foreach (ProcessTableColumnDefinition definition in Definitions)
        {
            if (!definition.DefaultVisible) continue;

            columns.Add(new ProcessTableColumn(
                definition.Kind,
                definition.Title,
                left,
                definition.DefaultWidth,
                definition.Alignment));
            left += definition.DefaultWidth;
        }

        return columns.ToArray();
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

/// <summary>Pure fixed-row geometry for painting, hit-testing, and viewport culling.</summary>
internal static class ProcessTableLayout
{
    private const int ViewportOverscanRows = 1;

    public static double GetContentHeight(int rowCount, ProcessTableMetrics metrics) =>
        metrics.HeaderHeight + Math.Max(0, rowCount) * metrics.RowHeight;

    public static int HitTestRow(double y, int rowCount, ProcessTableMetrics metrics)
    {
        if (rowCount <= 0 || y < metrics.HeaderHeight) return -1;

        int rowIndex = (int)Math.Floor((y - metrics.HeaderHeight) / metrics.RowHeight);
        return rowIndex >= 0 && rowIndex < rowCount ? rowIndex : -1;
    }

    public static int HitTestColumn(double x, ProcessTableColumn[] columns)
    {
        if (!double.IsFinite(x) || x < 0) return -1;

        int lowerBound = 0;
        int upperBound = columns.Length - 1;
        while (lowerBound <= upperBound)
        {
            int columnIndex = lowerBound + (upperBound - lowerBound) / 2;
            ProcessTableColumn column = columns[columnIndex];
            if (x < column.Left)
            {
                upperBound = columnIndex - 1;
                continue;
            }
            if (x >= column.Right)
            {
                lowerBound = columnIndex + 1;
                continue;
            }

            return columnIndex;
        }

        return -1;
    }

    public static int HitTestColumnDivider(
        double x,
        ProcessTableColumn[] columns,
        double hitRadius)
    {
        if (!double.IsFinite(x)
            || !double.IsFinite(hitRadius)
            || x < 0
            || hitRadius < 0)
        {
            return -1;
        }

        int columnIndex = HitTestColumn(x, columns);
        if (columnIndex >= 0)
        {
            ProcessTableColumn column = columns[columnIndex];
            if (columnIndex > 0 && Math.Abs(x - column.Left) <= hitRadius)
                return columnIndex - 1;
            return Math.Abs(x - column.Right) <= hitRadius ? columnIndex : -1;
        }

        int lastColumnIndex = columns.Length - 1;
        return lastColumnIndex >= 0
               && Math.Abs(x - columns[lastColumnIndex].Right) <= hitRadius
            ? lastColumnIndex
            : -1;
    }

    /// <summary>Writes resized display geometry without modifying the committed column layout.</summary>
    public static void WriteResizedColumns(
        ReadOnlySpan<ProcessTableColumn> columns,
        int resizedColumnIndex,
        double width,
        Span<ProcessTableColumn> destination)
    {
        if (columns.Length != destination.Length)
            throw new ArgumentException("Source and destination column counts must match.", nameof(destination));
        if ((uint)resizedColumnIndex >= (uint)columns.Length)
            throw new ArgumentOutOfRangeException(nameof(resizedColumnIndex));
        if (!double.IsFinite(width) || width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        double offset = width - columns[resizedColumnIndex].Width;
        for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            ProcessTableColumn column = columns[columnIndex];
            if (columnIndex < resizedColumnIndex)
            {
                destination[columnIndex] = column;
                continue;
            }

            destination[columnIndex] = columnIndex == resizedColumnIndex
                ? column with { Width = width }
                : column with { Left = column.Left + offset };
        }
    }

    /// <summary>Returns the final visible index after removing and reinserting the source column.</summary>
    public static int GetReorderInsertionIndex(
        double x,
        ProcessTableColumn[] columns,
        int sourceColumnIndex)
    {
        if (!double.IsFinite(x) || (uint)sourceColumnIndex >= (uint)columns.Length) return -1;

        int insertionIndex = 0;
        for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            if (columnIndex == sourceColumnIndex) continue;
            ProcessTableColumn column = columns[columnIndex];
            if (x < column.Left + column.Width / 2) break;
            insertionIndex++;
        }

        return Math.Clamp(insertionIndex, 0, columns.Length - 1);
    }

    /// <summary>Returns the current-layout divider that represents a pending insertion.</summary>
    public static double GetReorderInsertionX(
        ProcessTableColumn[] columns,
        int sourceColumnIndex,
        int insertionIndex)
    {
        if ((uint)sourceColumnIndex >= (uint)columns.Length
            || (uint)insertionIndex >= (uint)columns.Length)
        {
            return double.NaN;
        }

        if (insertionIndex <= sourceColumnIndex)
            return columns[insertionIndex].Left;

        int rightNeighborIndex = insertionIndex + 1;
        return rightNeighborIndex < columns.Length
            ? columns[rightNeighborIndex].Left
            : columns[^1].Right;
    }

    public static void GetVisibleRowRange(
        Rect viewport,
        int rowCount,
        ProcessTableMetrics metrics,
        out int firstRow,
        out int lastRowExclusive)
    {
        if (rowCount <= 0 || viewport.Height <= 0)
        {
            firstRow = 0;
            lastRowExclusive = 0;
            return;
        }

        double firstRowPosition = Math.Max(0, viewport.Y - metrics.HeaderHeight);
        double lastRowPosition = Math.Max(0, viewport.Bottom - metrics.HeaderHeight);
        int unclampedFirst = (int)Math.Floor(firstRowPosition / metrics.RowHeight) - ViewportOverscanRows;
        int unclampedLast = (int)Math.Ceiling(lastRowPosition / metrics.RowHeight) + ViewportOverscanRows;
        firstRow = Math.Clamp(unclampedFirst, 0, rowCount);
        lastRowExclusive = Math.Clamp(unclampedLast, firstRow, rowCount);
    }
}
