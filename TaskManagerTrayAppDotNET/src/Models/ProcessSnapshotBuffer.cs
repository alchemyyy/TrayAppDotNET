using TaskManagerTrayAppDotNET.UI;

namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Right-sized structure-of-arrays snapshot with shared immutable static rows.</summary>
internal sealed class ProcessSnapshotBuffer
{
    private const int InitialCapacity = 256;

    public ProcessDataSchema? Schema { get; private set; }
    public ProcessStaticData?[] StaticRows { get; private set; } = [];
    public ProcessGroupingFacts[] GroupingFacts { get; private set; } = [];
    public long[] DynamicNumericValues { get; private set; } = [];
    public string?[] DynamicTextValues { get; private set; } = [];
    public int Count { get; private set; }
    public int Capacity => StaticRows.Length;

    public void BeginWrite(ProcessDataSchema schema, int requiredCapacity)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (requiredCapacity < 0) throw new ArgumentOutOfRangeException(nameof(requiredCapacity));

        ClearReferences();
        int capacity = GetRequiredCapacity(requiredCapacity);
        bool schemaChanged = Schema?.VisibleMask != schema.VisibleMask;
        if (schemaChanged || Capacity < capacity)
        {
            int nextCapacity = Math.Max(capacity, Capacity);
            StaticRows = new ProcessStaticData?[nextCapacity];
            GroupingFacts = new ProcessGroupingFacts[nextCapacity];
            DynamicNumericValues = new long[checked(nextCapacity * schema.DynamicNumericCount)];
            DynamicTextValues = new string?[checked(nextCapacity * schema.DynamicTextCount)];
        }

        Schema = schema;
        Count = 0;
    }

    public void SetRow(
        int rowIndex,
        ProcessStaticData staticData,
        long[] dynamicNumericValues,
        string?[] dynamicTextValues,
        ProcessGroupingFacts groupingFacts = default)
    {
        ProcessDataSchema schema = Schema
                                   ?? throw new InvalidOperationException(
                                       "The snapshot buffer has not been configured.");
        if ((uint)rowIndex >= (uint)Capacity) throw new ArgumentOutOfRangeException(nameof(rowIndex));

        StaticRows[rowIndex] = staticData;
        GroupingFacts[rowIndex] = groupingFacts;
        if (schema.DynamicNumericCount > 0)
        {
            Array.Copy(
                dynamicNumericValues,
                sourceIndex: 0,
                DynamicNumericValues,
                checked(rowIndex * schema.DynamicNumericCount),
                schema.DynamicNumericCount);
        }

        if (schema.DynamicTextCount > 0)
        {
            Array.Copy(
                dynamicTextValues,
                sourceIndex: 0,
                DynamicTextValues,
                checked(rowIndex * schema.DynamicTextCount),
                schema.DynamicTextCount);
        }
    }

    public void CompleteWrite(int count)
    {
        if ((uint)count > (uint)Capacity) throw new ArgumentOutOfRangeException(nameof(count));
        Count = count;
    }

    public void CopyFrom(ProcessSnapshotBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ProcessDataSchema schema = source.Schema
                                   ?? throw new InvalidOperationException("The source snapshot has no schema.");
        BeginWrite(schema, source.Count);
        Array.Copy(source.StaticRows, StaticRows, source.Count);
        Array.Copy(source.GroupingFacts, GroupingFacts, source.Count);
        Array.Copy(
            source.DynamicNumericValues,
            DynamicNumericValues,
            checked(source.Count * schema.DynamicNumericCount));
        Array.Copy(
            source.DynamicTextValues,
            DynamicTextValues,
            checked(source.Count * schema.DynamicTextCount));
        Count = source.Count;
    }

    /// <summary>Releases all row references and backing arrays owned by this buffer.</summary>
    public void Reset()
    {
        ClearReferences();
        Schema = null;
        StaticRows = [];
        GroupingFacts = [];
        DynamicNumericValues = [];
        DynamicTextValues = [];
        Count = 0;
    }

    public long GetDynamicNumeric(int rowIndex, ProcessTableColumnKind column)
    {
        ProcessDataSchema schema = Schema
                                   ?? throw new InvalidOperationException("The snapshot buffer has no schema.");
        int slot = schema.GetDynamicNumericSlot(column);
        return DynamicNumericValues[checked(rowIndex * schema.DynamicNumericCount + slot)];
    }

    public string GetDynamicText(int rowIndex, ProcessTableColumnKind column)
    {
        ProcessDataSchema schema = Schema
                                   ?? throw new InvalidOperationException("The snapshot buffer has no schema.");
        int slot = schema.GetDynamicTextSlot(column);
        return DynamicTextValues[checked(rowIndex * schema.DynamicTextCount + slot)] ?? string.Empty;
    }

    private void ClearReferences()
    {
        if (Count <= 0 || Schema == null) return;

        Array.Clear(StaticRows, index: 0, Count);
        Array.Clear(GroupingFacts, index: 0, Count);
        if (Schema.DynamicTextCount > 0)
            Array.Clear(DynamicTextValues, index: 0, checked(Count * Schema.DynamicTextCount));
    }

    private int GetRequiredCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= Capacity) return Capacity;

        int capacity = Math.Max(InitialCapacity, Capacity);
        while (capacity < requiredCapacity)
            capacity = checked(capacity * 2);
        return capacity;
    }
}
