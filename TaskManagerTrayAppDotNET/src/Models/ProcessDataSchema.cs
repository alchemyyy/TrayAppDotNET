using TaskManagerTrayAppDotNET.UI;

namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Compact storage map containing visible columns plus active search columns.</summary>
internal sealed class ProcessDataSchema
{
    private readonly int[] _staticNumericSlots;
    private readonly int[] _staticTextSlots;
    private readonly int[] _dynamicNumericSlots;
    private readonly int[] _dynamicTextSlots;

    private ProcessDataSchema(
        ulong visibleMask,
        int[] staticNumericSlots,
        int[] staticTextSlots,
        int[] dynamicNumericSlots,
        int[] dynamicTextSlots,
        int staticNumericCount,
        int staticTextCount,
        int dynamicNumericCount,
        int dynamicTextCount)
    {
        VisibleMask = visibleMask;
        _staticNumericSlots = staticNumericSlots;
        _staticTextSlots = staticTextSlots;
        _dynamicNumericSlots = dynamicNumericSlots;
        _dynamicTextSlots = dynamicTextSlots;
        StaticNumericCount = staticNumericCount;
        StaticTextCount = staticTextCount;
        DynamicNumericCount = dynamicNumericCount;
        DynamicTextCount = dynamicTextCount;
    }

    public ulong VisibleMask { get; }
    public int StaticNumericCount { get; }
    public int StaticTextCount { get; }
    public int DynamicNumericCount { get; }
    public int DynamicTextCount { get; }

    public bool IsVisible(ProcessTableColumnKind column) =>
        ProcessTableColumnCatalog.Contains(VisibleMask, column);

    public int GetStaticNumericSlot(ProcessTableColumnKind column) => _staticNumericSlots[(int)column];
    public int GetStaticTextSlot(ProcessTableColumnKind column) => _staticTextSlots[(int)column];
    public int GetDynamicNumericSlot(ProcessTableColumnKind column) => _dynamicNumericSlots[(int)column];
    public int GetDynamicTextSlot(ProcessTableColumnKind column) => _dynamicTextSlots[(int)column];

    public static ProcessDataSchema Create(
        IReadOnlyList<ProcessColumnSetting> settings,
        ProcessTableColumnKind? additionalColumn = null)
    {
        ulong additionalColumnsMask = 0;
        if (additionalColumn.HasValue)
        {
            if (!Enum.IsDefined(additionalColumn.Value))
                throw new ArgumentOutOfRangeException(nameof(additionalColumn));
            additionalColumnsMask = ProcessTableColumnCatalog.GetMask(additionalColumn.Value);
        }

        return Create(settings, additionalColumnsMask);
    }

    /// <summary>Creates storage for visible columns and every column required by the active search.</summary>
    public static ProcessDataSchema Create(
        IReadOnlyList<ProcessColumnSetting> settings,
        ulong additionalColumnsMask)
    {
        ArgumentNullException.ThrowIfNull(settings);

        int columnCount = ProcessTableColumnCatalog.Definitions.Length;
        ulong knownColumnsMask = columnCount == sizeof(ulong) * 8
            ? ulong.MaxValue
            : (1UL << columnCount) - 1;
        if ((additionalColumnsMask & ~knownColumnsMask) != 0)
            throw new ArgumentOutOfRangeException(nameof(additionalColumnsMask));

        int[] staticNumericSlots = CreateEmptySlots(columnCount);
        int[] staticTextSlots = CreateEmptySlots(columnCount);
        int[] dynamicNumericSlots = CreateEmptySlots(columnCount);
        int[] dynamicTextSlots = CreateEmptySlots(columnCount);
        ulong visibleMask = ProcessTableColumnCatalog.CreateVisibleMask(settings) | additionalColumnsMask;
        int staticNumericCount = 0;
        int staticTextCount = 0;
        int dynamicNumericCount = 0;
        int dynamicTextCount = 0;

        for (int definitionIndex = 0; definitionIndex < columnCount; definitionIndex++)
        {
            ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Definitions[definitionIndex];
            if (!ProcessTableColumnCatalog.Contains(visibleMask, definition.Kind)) continue;

            bool storesText = StoresText(definition.Kind);
            if (definition.Lifetime == ProcessTableColumnLifetime.Static
                && !storesText
                && UsesIdentityNumericStorage(definition.Kind))
            {
                continue;
            }

            if (definition.Lifetime == ProcessTableColumnLifetime.Static
                && storesText
                && UsesIdentityTextStorage(definition.Kind))
            {
                continue;
            }

            switch ((definition.Lifetime, storesText))
            {
                case (ProcessTableColumnLifetime.Static, false):
                    staticNumericSlots[definitionIndex] = staticNumericCount;
                    staticNumericCount++;
                    break;
                case (ProcessTableColumnLifetime.Static, true):
                    staticTextSlots[definitionIndex] = staticTextCount;
                    staticTextCount++;
                    break;
                case (ProcessTableColumnLifetime.Dynamic, false):
                    dynamicNumericSlots[definitionIndex] = dynamicNumericCount;
                    dynamicNumericCount++;
                    break;
                case (ProcessTableColumnLifetime.Dynamic, true):
                    dynamicTextSlots[definitionIndex] = dynamicTextCount;
                    dynamicTextCount++;
                    break;
            }
        }

        return new ProcessDataSchema(
            visibleMask,
            staticNumericSlots,
            staticTextSlots,
            dynamicNumericSlots,
            dynamicTextSlots,
            staticNumericCount,
            staticTextCount,
            dynamicNumericCount,
            dynamicTextCount);
    }

    public static bool StoresText(ProcessTableColumnKind column) => column switch
    {
        ProcessTableColumnKind.Name
            or ProcessTableColumnKind.UserName
            or ProcessTableColumnKind.ImagePath
            or ProcessTableColumnKind.CommandLine
            or ProcessTableColumnKind.Description
            or ProcessTableColumnKind.PackageName
            or ProcessTableColumnKind.EnterpriseContext
            or ProcessTableColumnKind.GPUEngine
            or ProcessTableColumnKind.NPUEngine => true,
        _ => false
    };

    /// <summary>Returns whether the value already lives in the shared row identity.</summary>
    public static bool UsesIdentityTextStorage(ProcessTableColumnKind column) => column switch
    {
        ProcessTableColumnKind.Name
            or ProcessTableColumnKind.UserName
            or ProcessTableColumnKind.ImagePath
            or ProcessTableColumnKind.Description => true,
        _ => false
    };

    /// <summary>Returns whether the value already lives in the process instance key.</summary>
    public static bool UsesIdentityNumericStorage(ProcessTableColumnKind column) =>
        column == ProcessTableColumnKind.ProcessID;

    private static int[] CreateEmptySlots(int count)
    {
        int[] slots = new int[count];
        Array.Fill(slots, -1);
        return slots;
    }
}
