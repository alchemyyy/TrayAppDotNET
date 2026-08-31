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

internal readonly record struct ProcessTableMetrics(
    double HeaderHeight,
    double RowHeight,
    double RowTextHeight,
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
        Static(ProcessTableColumnKind.ProcessID, title: "PID", width: 82, ProcessTableColumnAlignment.Right,
            visible: true),
        Dynamic(ProcessTableColumnKind.Status, title: "Status", width: 106, ProcessTableColumnAlignment.Left,
            visible: true),
        Static(ProcessTableColumnKind.UserName, title: "User name", width: 140, ProcessTableColumnAlignment.Left,
            visible: true),
        Static(ProcessTableColumnKind.SessionID, title: "Session ID", NarrowWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.JobObjectID, title: "Job object ID", CounterWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.CPU, title: "CPU", width: 68, ProcessTableColumnAlignment.Right, visible: true),
        Dynamic(ProcessTableColumnKind.CPUTime, title: "CPU time", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.Lifetime, title: "Lifetime", CounterWidth, ProcessTableColumnAlignment.Right,
            visible: true),
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
        Dynamic(ProcessTableColumnKind.PrivateMemory, title: "Memory (private working set)", width: 136,
            ProcessTableColumnAlignment.Right, visible: true),
        Dynamic(ProcessTableColumnKind.SharedWorkingSet, title: "Memory (shared working set)", width: 136,
            ProcessTableColumnAlignment.Right, visible: true),
        Dynamic(ProcessTableColumnKind.Disk, title: "Disk", width: 90, ProcessTableColumnAlignment.Right,
            visible: true),
        Dynamic(ProcessTableColumnKind.Network, title: "Network", width: 90, ProcessTableColumnAlignment.Right,
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
        Dynamic(ProcessTableColumnKind.Handles, title: "Handles", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.Threads, title: "Threads", CounterWidth, ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.UserObjects, title: "User objects", CounterWidth,
            ProcessTableColumnAlignment.Right),
        Dynamic(ProcessTableColumnKind.GDIObjects, title: "GDI objects", CounterWidth,
            ProcessTableColumnAlignment.Right),
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
        Static(ProcessTableColumnKind.CommandLine, title: "Command line", width: 520, ProcessTableColumnAlignment.Left,
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

/// <summary>Provides Processes-specific column geometry.</summary>
internal static class ProcessTableLayout
{
    public const int MinimumZoomFontWeight = 100;
    public const int MaximumZoomFontWeight = 630;

    private const int ReferenceMaximumZoomFontWeight = 900;
    private const double ReferenceZoomFontWeightLinearCoefficient = 320;
    private const double ReferenceZoomFontWeightCubicCoefficient = 80;
    private const double ZoomFontWeightClampDistanceScale = 0.75;

    /// <summary>Calculates one rendered line height from its measured font-size ratio.</summary>
    public static double CalculateRowTextHeight(double fontSize, double textHeightScale)
    {
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (!double.IsFinite(textHeightScale) || textHeightScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(textHeightScale));

        return fontSize * textHeightScale;
    }

    /// <summary>Builds row height from rendered text height plus the requested visible gap.</summary>
    public static double CalculateRowHeight(double rowTextHeight, double rowSpacing)
    {
        if (!double.IsFinite(rowTextHeight) || rowTextHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowTextHeight));
        if (!double.IsFinite(rowSpacing))
            throw new ArgumentOutOfRangeException(nameof(rowSpacing));

        return rowTextHeight + Math.Max(val1: 0, rowSpacing);
    }

    /// <summary>
    /// Resolves font weight with a monotonic smoothstep sigmoid.
    /// The configured weight remains fixed at default zoom while the reduced output range is
    /// stretched across the previous clamp span.
    /// </summary>
    public static int CalculateZoomFontWeight(
        DetailsGridFontWeight baseFontWeight,
        double referenceFontSize,
        double fontSize)
    {
        if (!Enum.IsDefined(baseFontWeight))
            throw new ArgumentOutOfRangeException(nameof(baseFontWeight));
        if (!double.IsFinite(referenceFontSize) || referenceFontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(referenceFontSize));
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));

        int baseFontWeightValue = (int)baseFontWeight;
        int effectiveMaximumFontWeight = Math.Max(
            MaximumZoomFontWeight,
            baseFontWeightValue);
        double normalizedZoom = fontSize / referenceFontSize - 1;
        double referenceLowerClampZoom = SolveReferenceZoom(
            MinimumZoomFontWeight - baseFontWeightValue);
        double referenceUpperClampZoom = SolveReferenceZoom(
            ReferenceMaximumZoomFontWeight - baseFontWeightValue);
        double clampZoomSpan = ZoomFontWeightClampDistanceScale
                               * (referenceUpperClampZoom - referenceLowerClampZoom);
        double baseWeightPosition = (double)(baseFontWeightValue - MinimumZoomFontWeight)
                                    / (effectiveMaximumFontWeight - MinimumZoomFontWeight);
        double baseSigmoidPosition = InverseSmoothstep(baseWeightPosition);
        double lowerClampZoom = -baseSigmoidPosition * clampZoomSpan;
        double upperClampZoom = lowerClampZoom + clampZoomSpan;
        if (normalizedZoom <= lowerClampZoom) return MinimumZoomFontWeight;
        if (normalizedZoom >= upperClampZoom) return effectiveMaximumFontWeight;

        double sigmoidPosition = (normalizedZoom - lowerClampZoom) / clampZoomSpan;
        double weightPosition = Smoothstep(sigmoidPosition);
        double fontWeight = MinimumZoomFontWeight
                            + weightPosition
                            * (effectiveMaximumFontWeight - MinimumZoomFontWeight);
        return (int)Math.Round(fontWeight, MidpointRounding.AwayFromZero);
    }

    private static double InverseSmoothstep(double position)
    {
        if (position <= 0) return 0;
        if (position >= 1) return 1;

        return 0.5 - Math.Sin(Math.Asin(1 - 2 * position) / 3);
    }

    private static double Smoothstep(double position) =>
        position * position * (3 - 2 * position);

    private static double SolveReferenceZoom(double weightOffset)
    {
        // Retain the previous cubic response only as calibration for the clamp positions
        double halfOffset = weightOffset / (2 * ReferenceZoomFontWeightCubicCoefficient);
        double linearRatio = ReferenceZoomFontWeightLinearCoefficient
                             / (3 * ReferenceZoomFontWeightCubicCoefficient);
        double discriminantRoot = Math.Sqrt(
            halfOffset * halfOffset + linearRatio * linearRatio * linearRatio);
        return Math.Cbrt(halfOffset + discriminantRoot)
               + Math.Cbrt(halfOffset - discriminantRoot);
    }

    /// <summary>Scales process icons by the smaller text or row zoom factor.</summary>
    public static double ScaleProcessIconSize(
        double baseIconSize,
        double baseFontSize,
        double baseRowHeight,
        double fontSize,
        double rowHeight)
    {
        if (!double.IsFinite(baseIconSize) || baseIconSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(baseIconSize));
        if (!double.IsFinite(baseFontSize) || baseFontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(baseFontSize));
        if (!double.IsFinite(baseRowHeight) || baseRowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(baseRowHeight));
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (!double.IsFinite(rowHeight) || rowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowHeight));

        double zoomScale = Math.Min(fontSize / baseFontSize, rowHeight / baseRowHeight);
        return baseIconSize * zoomScale;
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
            return -1;

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
            throw new ArgumentException(message: "Source and destination column counts must match.",
                nameof(destination));
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

        return Math.Clamp(insertionIndex, min: 0, columns.Length - 1);
    }

    /// <summary>Returns the current-layout divider that represents a pending insertion.</summary>
    public static double GetReorderInsertionX(
        ProcessTableColumn[] columns,
        int sourceColumnIndex,
        int insertionIndex)
    {
        if ((uint)sourceColumnIndex >= (uint)columns.Length
            || (uint)insertionIndex >= (uint)columns.Length)
            return double.NaN;

        if (insertionIndex <= sourceColumnIndex)
            return columns[insertionIndex].Left;

        int rightNeighborIndex = insertionIndex + 1;
        return rightNeighborIndex < columns.Length
            ? columns[rightNeighborIndex].Left
            : columns[^1].Right;
    }
}
