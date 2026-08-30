using System.Diagnostics;
using System.Runtime.InteropServices;
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
        long previousTimestamp = 100;
        long currentTimestamp = previousTimestamp + Stopwatch.Frequency * 2;

        double bytesPerSecond = ProcessSnapshotService.CalculateTransferRate(
            true,
            1_000,
            previousTimestamp,
            5_000,
            currentTimestamp);

        Assert.Equal(2_000.0, bytesPerSecond);
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

        Assert.Equal(0.0, bytesPerSecond);
    }

    [Fact]
    public void SRUMRecordsAccumulateSentAndReceivedBytesByProcess()
    {
        IntPtr recordSet = AllocateZeroed(SRUMRecordSetSize);
        IntPtr record = AllocateZeroed(SRUMRecordSize);
        IntPtr columns = AllocateZeroed(SRUMColumnSize * 3);
        try
        {
            const int processID = 4_242;
            WriteUnsignedColumn(columns, 0, SRUMSentBytesColumnID, 1_000);
            WriteUnsignedColumn(columns, 1, SRUMReceivedBytesColumnID, 2_000);
            WriteUnsignedColumn(columns, 2, SRUMProcessIDColumnID, processID);
            Marshal.WriteIntPtr(record, SRUMRecordUserSIDOffset, new IntPtr(1));
            Marshal.WriteInt16(record, SRUMRecordColumnCountOffset, 3);
            Marshal.WriteIntPtr(record, SRUMRecordColumnsOffset, columns);
            Marshal.WriteInt32(recordSet, 0, 1);
            Marshal.WriteIntPtr(recordSet, 8, record);
            Dictionary<int, ulong> cumulativeBytes = [];

            bool firstAccepted = ProcessNetworkUsageSampler.AccumulateRecordSet(
                recordSet,
                cumulativeBytes);
            bool secondAccepted = ProcessNetworkUsageSampler.AccumulateRecordSet(
                recordSet,
                cumulativeBytes);

            Assert.True(firstAccepted);
            Assert.True(secondAccepted);
            Assert.Equal(6_000UL, cumulativeBytes[processID]);
        }
        finally
        {
            Marshal.FreeHGlobal(columns);
            Marshal.FreeHGlobal(record);
            Marshal.FreeHGlobal(recordSet);
        }
    }

    [Fact]
    public void SRUMRecordsIgnoreGlobalRowsWithoutAUserSID()
    {
        IntPtr recordSet = AllocateZeroed(SRUMRecordSetSize);
        IntPtr record = AllocateZeroed(SRUMRecordSize);
        IntPtr columns = AllocateZeroed(SRUMColumnSize * 2);
        try
        {
            WriteUnsignedColumn(columns, 0, SRUMSentBytesColumnID, 1_000);
            WriteUnsignedColumn(columns, 1, SRUMProcessIDColumnID, 4_242);
            Marshal.WriteInt16(record, SRUMRecordColumnCountOffset, 2);
            Marshal.WriteIntPtr(record, SRUMRecordColumnsOffset, columns);
            Marshal.WriteInt32(recordSet, 0, 1);
            Marshal.WriteIntPtr(recordSet, 8, record);
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
        Marshal.Copy(new byte[byteCount], 0, address, byteCount);
        return address;
    }

    private static void WriteUnsignedColumn(
        IntPtr columns,
        int columnIndex,
        ushort columnID,
        ulong value)
    {
        IntPtr column = IntPtr.Add(columns, checked(columnIndex * SRUMColumnSize));
        Marshal.WriteInt16(column, 0, unchecked((short)columnID));
        Marshal.WriteInt64(column, 8, unchecked((long)value));
    }
}
