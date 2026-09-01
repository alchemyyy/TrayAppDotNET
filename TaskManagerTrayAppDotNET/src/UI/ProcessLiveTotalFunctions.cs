namespace TaskManagerTrayAppDotNET.UI;

internal readonly record struct ProcessLiveTotalValue(
    bool HasValue,
    long EncodedValue);

/// <summary>Aggregates one supported numeric column without allocating in the snapshot update path.</summary>
internal static class ProcessLiveTotalFunctions
{
    public static ProcessLiveTotalValue Calculate(
        ProcessSnapshotBuffer snapshot,
        int rowCount,
        ProcessTableColumnKind column)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if ((uint)rowCount > (uint)snapshot.Count)
            throw new ArgumentOutOfRangeException(nameof(rowCount));
        if (!ProcessColumnSettings.SupportsLiveTotal(column))
            throw new ArgumentOutOfRangeException(nameof(column), message: "The column has no live total.");

        ProcessDataSchema? schema = snapshot.Schema;
        if (schema == null || schema.GetDynamicNumericSlot(column) < 0)
            return default;

        return column switch
        {
            ProcessTableColumnKind.CPU
                or ProcessTableColumnKind.CPUSingle
                or ProcessTableColumnKind.Disk
                or ProcessTableColumnKind.Network
                or ProcessTableColumnKind.GPU
                or ProcessTableColumnKind.NPU
                or ProcessTableColumnKind.CPUUtility => CalculateDouble(snapshot, rowCount, column),
            ProcessTableColumnKind.Cycle
                or ProcessTableColumnKind.IOReads
                or ProcessTableColumnKind.IOWrites
                or ProcessTableColumnKind.IOOther
                or ProcessTableColumnKind.IOReadBytes
                or ProcessTableColumnKind.IOWriteBytes
                or ProcessTableColumnKind.IOOtherBytes => CalculateUnsigned(snapshot, rowCount, column),
            _ => CalculateSigned(snapshot, rowCount, column)
        };
    }

    private static ProcessLiveTotalValue CalculateDouble(
        ProcessSnapshotBuffer snapshot,
        int rowCount,
        ProcessTableColumnKind column)
    {
        double total = 0;
        bool hasValue = false;
        bool excludesIdleProcess = column is ProcessTableColumnKind.CPU
            or ProcessTableColumnKind.CPUSingle
            or ProcessTableColumnKind.CPUUtility;
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            double value = BitConverter.Int64BitsToDouble(snapshot.GetDynamicNumeric(rowIndex, column));
            if (!double.IsFinite(value) || value < 0) continue;

            hasValue = true;
            ProcessStaticData? row = snapshot.StaticRows[rowIndex];
            if (excludesIdleProcess && row?.ProcessID == 0) continue;
            total += value;
            if (double.IsPositiveInfinity(total)) total = double.MaxValue;
        }

        return new ProcessLiveTotalValue(
            hasValue,
            BitConverter.DoubleToInt64Bits(total));
    }

    private static ProcessLiveTotalValue CalculateUnsigned(
        ProcessSnapshotBuffer snapshot,
        int rowCount,
        ProcessTableColumnKind column)
    {
        ulong total = 0;
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            ulong value = unchecked((ulong)snapshot.GetDynamicNumeric(rowIndex, column));
            total = value > ulong.MaxValue - total
                ? ulong.MaxValue
                : total + value;
        }

        return new ProcessLiveTotalValue(
            HasValue: rowCount > 0,
            EncodedValue: unchecked((long)total));
    }

    private static ProcessLiveTotalValue CalculateSigned(
        ProcessSnapshotBuffer snapshot,
        int rowCount,
        ProcessTableColumnKind column)
    {
        long total = 0;
        bool hasValue = false;
        bool ignoresNegativeValues = ProcessColumnSettings.IsMemoryColumn(column)
                                     && column != ProcessTableColumnKind.WorkingSetDelta;
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            long value = snapshot.GetDynamicNumeric(rowIndex, column);
            if (ignoresNegativeValues && value < 0) continue;

            hasValue = true;
            total = SaturatingAdd(total, value);
        }

        return new ProcessLiveTotalValue(hasValue, total);
    }

    private static long SaturatingAdd(long left, long right)
    {
        if (right > 0 && left > long.MaxValue - right) return long.MaxValue;
        if (right < 0 && left < long.MinValue - right) return long.MinValue;
        return left + right;
    }
}
