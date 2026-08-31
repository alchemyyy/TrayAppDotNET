using System.Diagnostics;
using System.Runtime.InteropServices;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessResourceUsageTests
{
    private const int SRUMRecordSetSize = 16;
    private const int SRUMRecordSize = 64;
    private const int SRUMColumnSize = 24;
    private const int SRUMRecordUserSIDOffset = 40;
    private const int SRUMRecordColumnCountOffset = 48;
    private const int SRUMRecordColumnsOffset = 56;
    private const ushort SRUMSentBytesColumnID = 3;
    private const ushort SRUMReceivedBytesColumnID = 4;
    private const ushort SRUMProcessIDColumnID = 6;

    [Fact]
    public void TransferRateUsesCounterDeltaAndElapsedTime()
    {
        const long previousTimestamp = 100;
        long currentTimestamp = previousTimestamp + Stopwatch.Frequency * 2;

        double bytesPerSecond = ProcessSnapshotService.CalculateTransferRate(
            hasPreviousSample: true,
            previousBytes: 1_000,
            previousTimestamp,
            currentBytes: 5_000,
            currentTimestamp);

        Assert.Equal(expected: 2_000.0, bytesPerSecond);
    }

    [Theory]
    [InlineData(false, 1_000UL, 2_000UL, 100L, 200L)]
    [InlineData(true, 2_000UL, 1_000UL, 100L, 200L)]
    [InlineData(true, 1_000UL, 2_000UL, 200L, 200L)]
    public void TransferRateTreatsFirstSamplesResetsAndRepeatedTimestampsAsBaselines(
        bool hasPreviousSample,
        ulong previousBytes,
        ulong currentBytes,
        long previousTimestamp,
        long currentTimestamp)
    {
        double bytesPerSecond = ProcessSnapshotService.CalculateTransferRate(
            hasPreviousSample,
            previousBytes,
            previousTimestamp,
            currentBytes,
            currentTimestamp);

        Assert.Equal(expected: 0.0, bytesPerSecond);
    }

    [Fact]
    public void InitialAndCallbackSRUMRecordsAccumulateSentAndReceivedBytesByProcess()
    {
        IntPtr recordSet = AllocateZeroed(SRUMRecordSetSize);
        IntPtr record = AllocateZeroed(SRUMRecordSize);
        IntPtr columns = AllocateZeroed(SRUMColumnSize * 3);
        try
        {
            const int processID = 4_242;
            WriteUnsignedColumn(columns, columnIndex: 0, SRUMSentBytesColumnID, value: 1_000);
            WriteUnsignedColumn(columns, columnIndex: 1, SRUMReceivedBytesColumnID, value: 2_000);
            WriteUnsignedColumn(columns, columnIndex: 2, SRUMProcessIDColumnID, processID);
            Marshal.WriteIntPtr(record, SRUMRecordUserSIDOffset, new IntPtr(1));
            Marshal.WriteInt16(record, SRUMRecordColumnCountOffset, val: 3);
            Marshal.WriteIntPtr(record, SRUMRecordColumnsOffset, columns);
            Marshal.WriteInt32(recordSet, ofs: 0, val: 1);
            Marshal.WriteIntPtr(recordSet, ofs: 8, record);
            Dictionary<int, ulong> cumulativeBytes = [];

            bool initialAccepted = ProcessNetworkUsageSampler.AccumulateInitialRecordSet(
                recordSet,
                cumulativeBytes);
            bool callbackAccepted = ProcessNetworkUsageSampler.AccumulateRecordSet(
                recordSet,
                cumulativeBytes);

            Assert.True(initialAccepted);
            Assert.True(callbackAccepted);
            Assert.Equal(expected: 6_000UL, cumulativeBytes[processID]);
        }
        finally
        {
            Marshal.FreeHGlobal(columns);
            Marshal.FreeHGlobal(record);
            Marshal.FreeHGlobal(recordSet);
        }
    }

    [Fact]
    public void MissingInitialSRUMRecordSetCreatesAnEmptyBaseline()
    {
        Dictionary<int, ulong> cumulativeBytes = [];

        bool accepted = ProcessNetworkUsageSampler.AccumulateInitialRecordSet(
            IntPtr.Zero,
            cumulativeBytes);

        Assert.True(accepted);
        Assert.Empty(cumulativeBytes);
    }

    [Fact]
    public void NetworkRateCacheRetainsLatestValueAcrossProcessWalks()
    {
        ProcessNetworkRateCache cache = new();
        ProcessInstanceKey instanceKey = new(ProcessID: 4_242, CreationTimeTicks: 100);
        cache.Set(instanceKey, bytesPerSecond: 12_345, generation: 1);

        cache.MarkSeen(instanceKey, generation: 2);
        cache.RemoveStale(generation: 2);

        Assert.True(cache.TryGet(instanceKey, out double bytesPerSecond));
        Assert.Equal(expected: 12_345.0, bytesPerSecond);
    }

    [Fact]
    public void NetworkRateCacheRejectsReusedProcessIDs()
    {
        ProcessNetworkRateCache cache = new();
        ProcessInstanceKey previousInstance = new(ProcessID: 4_242, CreationTimeTicks: 100);
        ProcessInstanceKey reusedInstance = new(ProcessID: 4_242, CreationTimeTicks: 200);
        cache.Set(previousInstance, bytesPerSecond: 12_345, generation: 1);

        bool found = cache.TryGet(reusedInstance, out double bytesPerSecond);

        Assert.False(found);
        Assert.Equal(expected: 0.0, bytesPerSecond);
    }

    [Fact]
    public void SRUMRecordsIgnoreGlobalRowsWithoutAUserSID()
    {
        IntPtr recordSet = AllocateZeroed(SRUMRecordSetSize);
        IntPtr record = AllocateZeroed(SRUMRecordSize);
        IntPtr columns = AllocateZeroed(SRUMColumnSize * 2);
        try
        {
            WriteUnsignedColumn(columns, columnIndex: 0, SRUMSentBytesColumnID, value: 1_000);
            WriteUnsignedColumn(columns, columnIndex: 1, SRUMProcessIDColumnID, value: 4_242);
            Marshal.WriteInt16(record, SRUMRecordColumnCountOffset, val: 2);
            Marshal.WriteIntPtr(record, SRUMRecordColumnsOffset, columns);
            Marshal.WriteInt32(recordSet, ofs: 0, val: 1);
            Marshal.WriteIntPtr(recordSet, ofs: 8, record);
            Dictionary<int, ulong> cumulativeBytes = [];

            bool accepted = ProcessNetworkUsageSampler.AccumulateRecordSet(
                recordSet,
                cumulativeBytes);

            Assert.True(accepted);
            Assert.Empty(cumulativeBytes);
        }
        finally
        {
            Marshal.FreeHGlobal(columns);
            Marshal.FreeHGlobal(record);
            Marshal.FreeHGlobal(recordSet);
        }
    }

    private static IntPtr AllocateZeroed(int byteCount)
    {
        IntPtr address = Marshal.AllocHGlobal(byteCount);
        Marshal.Copy(new byte[byteCount], startIndex: 0, address, byteCount);
        return address;
    }

    private static void WriteUnsignedColumn(
        IntPtr columns,
        int columnIndex,
        ushort columnID,
        ulong value)
    {
        IntPtr column = IntPtr.Add(columns, checked(columnIndex * SRUMColumnSize));
        Marshal.WriteInt16(column, ofs: 0, unchecked((short)columnID));
        Marshal.WriteInt64(column, ofs: 8, unchecked((long)value));
    }
}
