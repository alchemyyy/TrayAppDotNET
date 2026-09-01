namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Aggregates one semantic group without counting its synthetic row as a process.</summary>
internal static class SemanticProcessAggregation
{
    public static long AggregateDynamicNumeric(
        ProcessSnapshotBuffer snapshot,
        ReadOnlySpan<int> memberRowIndexes,
        ProcessTableColumnKind column,
        int representativeRowIndex)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (memberRowIndexes.IsEmpty)
            throw new ArgumentException("A semantic group must contain a process.", nameof(memberRowIndexes));
        if ((uint)representativeRowIndex >= (uint)snapshot.Count)
            throw new ArgumentOutOfRangeException(nameof(representativeRowIndex));

        return column switch
        {
            ProcessTableColumnKind.CPU
                or ProcessTableColumnKind.CPUSingle
                or ProcessTableColumnKind.Disk
                or ProcessTableColumnKind.Network
                or ProcessTableColumnKind.GPU
                or ProcessTableColumnKind.NPU
                or ProcessTableColumnKind.CPUUtility => SumDouble(
                    snapshot,
                    memberRowIndexes,
                    column),
            ProcessTableColumnKind.Lifetime => MaximumNonNegative(
                snapshot,
                memberRowIndexes,
                column),
            ProcessTableColumnKind.Cycle
                or ProcessTableColumnKind.IOReads
                or ProcessTableColumnKind.IOWrites
                or ProcessTableColumnKind.IOOther
                or ProcessTableColumnKind.IOReadBytes
                or ProcessTableColumnKind.IOWriteBytes
                or ProcessTableColumnKind.IOOtherBytes => SumUnsigned(
                    snapshot,
                    memberRowIndexes,
                    column),
            ProcessTableColumnKind.CPUTime
                or ProcessTableColumnKind.WorkingSet
                or ProcessTableColumnKind.PeakWorkingSet
                or ProcessTableColumnKind.ActivePrivateWorkingSet
                or ProcessTableColumnKind.PrivateMemory
                or ProcessTableColumnKind.SharedWorkingSet
                or ProcessTableColumnKind.CommitSize
                or ProcessTableColumnKind.PagedPool
                or ProcessTableColumnKind.NonPagedPool
                or ProcessTableColumnKind.PageFaults
                or ProcessTableColumnKind.Handles
                or ProcessTableColumnKind.Threads
                or ProcessTableColumnKind.UserObjects
                or ProcessTableColumnKind.GDIObjects
                or ProcessTableColumnKind.DedicatedGPUMemory
                or ProcessTableColumnKind.SharedGPUMemory
                or ProcessTableColumnKind.DedicatedNPUMemory
                or ProcessTableColumnKind.SharedNPUMemory => SumNonNegative(
                    snapshot,
                    memberRowIndexes,
                    column),
            ProcessTableColumnKind.WorkingSetDelta
                or ProcessTableColumnKind.PageFaultDelta => SumSigned(
                    snapshot,
                    memberRowIndexes,
                    column),
            _ => snapshot.GetDynamicNumeric(representativeRowIndex, column)
        };
    }

    private static long SumDouble(
        ProcessSnapshotBuffer snapshot,
        ReadOnlySpan<int> memberRowIndexes,
        ProcessTableColumnKind column)
    {
        double total = 0;
        bool hasValue = false;
        for (int memberIndex = 0; memberIndex < memberRowIndexes.Length; memberIndex++)
        {
            double value = BitConverter.Int64BitsToDouble(
                snapshot.GetDynamicNumeric(memberRowIndexes[memberIndex], column));
            if (!double.IsFinite(value) || value < 0) continue;

            hasValue = true;
            total += value;
            if (double.IsPositiveInfinity(total)) total = double.MaxValue;
        }

        return BitConverter.DoubleToInt64Bits(hasValue ? total : -1);
    }

    private static long MaximumNonNegative(
        ProcessSnapshotBuffer snapshot,
        ReadOnlySpan<int> memberRowIndexes,
        ProcessTableColumnKind column)
    {
        long maximum = -1;
        for (int memberIndex = 0; memberIndex < memberRowIndexes.Length; memberIndex++)
        {
            long value = snapshot.GetDynamicNumeric(memberRowIndexes[memberIndex], column);
            if (value > maximum) maximum = value;
        }

        return maximum;
    }

    private static long SumUnsigned(
        ProcessSnapshotBuffer snapshot,
        ReadOnlySpan<int> memberRowIndexes,
        ProcessTableColumnKind column)
    {
        ulong total = 0;
        for (int memberIndex = 0; memberIndex < memberRowIndexes.Length; memberIndex++)
        {
            ulong value = unchecked((ulong)snapshot.GetDynamicNumeric(
                memberRowIndexes[memberIndex],
                column));
            total = value > ulong.MaxValue - total ? ulong.MaxValue : total + value;
        }

        return unchecked((long)total);
    }

    private static long SumNonNegative(
        ProcessSnapshotBuffer snapshot,
        ReadOnlySpan<int> memberRowIndexes,
        ProcessTableColumnKind column)
    {
        long total = 0;
        bool hasValue = false;
        for (int memberIndex = 0; memberIndex < memberRowIndexes.Length; memberIndex++)
        {
            long value = snapshot.GetDynamicNumeric(memberRowIndexes[memberIndex], column);
            if (value < 0) continue;

            hasValue = true;
            total = SaturatingAdd(total, value);
        }

        return hasValue ? total : -1;
    }

    private static long SumSigned(
        ProcessSnapshotBuffer snapshot,
        ReadOnlySpan<int> memberRowIndexes,
        ProcessTableColumnKind column)
    {
        long total = 0;
        for (int memberIndex = 0; memberIndex < memberRowIndexes.Length; memberIndex++)
        {
            total = SaturatingAdd(
                total,
                snapshot.GetDynamicNumeric(memberRowIndexes[memberIndex], column));
        }

        return total;
    }

    private static long SaturatingAdd(long left, long right)
    {
        if (right > 0 && left > long.MaxValue - right) return long.MaxValue;
        if (right < 0 && left < long.MinValue - right) return long.MinValue;
        return left + right;
    }
}
