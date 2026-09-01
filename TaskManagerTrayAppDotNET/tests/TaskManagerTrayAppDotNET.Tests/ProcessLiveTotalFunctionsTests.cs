using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessLiveTotalFunctionsTests
{
    [Fact]
    public void CPUAggregateExcludesIdleAndUnavailableValues()
    {
        ProcessSnapshotBuffer snapshot = CreateSnapshot(
            ProcessTableColumnKind.CPU,
            (ProcessID: 0, EncodeDouble(82.5)),
            (ProcessID: 10, EncodeDouble(12.25)),
            (ProcessID: 11, EncodeDouble(5.5)),
            (ProcessID: 12, EncodeDouble(-1)),
            (ProcessID: 13, EncodeDouble(double.NaN)));

        ProcessLiveTotalValue total = ProcessLiveTotalFunctions.Calculate(
            snapshot,
            snapshot.Count,
            ProcessTableColumnKind.CPU);

        Assert.True(total.HasValue);
        Assert.Equal(expected: 17.75, BitConverter.Int64BitsToDouble(total.EncodedValue), precision: 10);
    }

    [Fact]
    public void TransferRateAggregateIncludesEveryValidProcess()
    {
        ProcessSnapshotBuffer snapshot = CreateSnapshot(
            ProcessTableColumnKind.Disk,
            (ProcessID: 0, EncodeDouble(100)),
            (ProcessID: 10, EncodeDouble(250.5)),
            (ProcessID: 11, EncodeDouble(-1)));

        ProcessLiveTotalValue total = ProcessLiveTotalFunctions.Calculate(
            snapshot,
            snapshot.Count,
            ProcessTableColumnKind.Disk);

        Assert.True(total.HasValue);
        Assert.Equal(expected: 350.5, BitConverter.Int64BitsToDouble(total.EncodedValue), precision: 10);
    }

    [Fact]
    public void MemoryAggregateIgnoresUnavailableRows()
    {
        ProcessSnapshotBuffer snapshot = CreateSnapshot(
            ProcessTableColumnKind.PrivateMemory,
            (ProcessID: 10, Value: 1_000),
            (ProcessID: 11, Value: -1),
            (ProcessID: 12, Value: 2_500));

        ProcessLiveTotalValue total = ProcessLiveTotalFunctions.Calculate(
            snapshot,
            snapshot.Count,
            ProcessTableColumnKind.PrivateMemory);

        Assert.True(total.HasValue);
        Assert.Equal(expected: 3_500, total.EncodedValue);
    }

    [Fact]
    public void SignedDeltaAggregateRetainsNegativeValues()
    {
        ProcessSnapshotBuffer snapshot = CreateSnapshot(
            ProcessTableColumnKind.WorkingSetDelta,
            (ProcessID: 10, Value: 100),
            (ProcessID: 11, Value: -250),
            (ProcessID: 12, Value: 25));

        ProcessLiveTotalValue total = ProcessLiveTotalFunctions.Calculate(
            snapshot,
            snapshot.Count,
            ProcessTableColumnKind.WorkingSetDelta);

        Assert.True(total.HasValue);
        Assert.Equal(expected: -125, total.EncodedValue);
    }

    [Fact]
    public void UnsignedAggregateSaturatesWithoutWrapping()
    {
        ProcessSnapshotBuffer snapshot = CreateSnapshot(
            ProcessTableColumnKind.IOReads,
            (ProcessID: 10, Value: unchecked((long)(ulong.MaxValue - 2))),
            (ProcessID: 11, Value: 10));

        ProcessLiveTotalValue total = ProcessLiveTotalFunctions.Calculate(
            snapshot,
            snapshot.Count,
            ProcessTableColumnKind.IOReads);

        Assert.True(total.HasValue);
        Assert.Equal(ulong.MaxValue, unchecked((ulong)total.EncodedValue));
    }

    [Fact]
    public void SignedAggregateSaturatesWithoutWrapping()
    {
        ProcessSnapshotBuffer snapshot = CreateSnapshot(
            ProcessTableColumnKind.Handles,
            (ProcessID: 10, Value: long.MaxValue),
            (ProcessID: 11, Value: 1));

        ProcessLiveTotalValue total = ProcessLiveTotalFunctions.Calculate(
            snapshot,
            snapshot.Count,
            ProcessTableColumnKind.Handles);

        Assert.True(total.HasValue);
        Assert.Equal(long.MaxValue, total.EncodedValue);
    }

    [Fact]
    public void AllUnavailableAcceleratorMemoryProducesNoValue()
    {
        ProcessSnapshotBuffer snapshot = CreateSnapshot(
            ProcessTableColumnKind.DedicatedGPUMemory,
            (ProcessID: 10, Value: -1),
            (ProcessID: 11, Value: -1));

        ProcessLiveTotalValue total = ProcessLiveTotalFunctions.Calculate(
            snapshot,
            snapshot.Count,
            ProcessTableColumnKind.DedicatedGPUMemory);

        Assert.False(total.HasValue);
    }

    private static ProcessSnapshotBuffer CreateSnapshot(
        ProcessTableColumnKind column,
        params (int ProcessID, long Value)[] rows)
    {
        ProcessColumnSetting setting = new()
        {
            Column = column,
            Visible = true,
            Width = ProcessTableColumnCatalog.Get(column).DefaultWidth
        };
        ProcessDataSchema schema = ProcessDataSchema.Create([setting]);
        ProcessSnapshotBuffer snapshot = new();
        snapshot.BeginWrite(schema, rows.Length);
        int dynamicSlot = schema.GetDynamicNumericSlot(column);
        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            (int processID, long value) = rows[rowIndex];
            long[] dynamicNumericValues = new long[schema.DynamicNumericCount];
            dynamicNumericValues[dynamicSlot] = value;
            ProcessImageIdentity image = new(
                key: processID.ToString(),
                name: $"process-{processID}",
                imagePath: string.Empty,
                description: string.Empty,
                iconSource: default);
            ProcessStaticData staticData = new()
            {
                InstanceKey = new ProcessInstanceKey(processID, CreationTimeTicks: processID * 100L),
                Image = image,
                UserName = string.Empty,
                NumericValues = new long[schema.StaticNumericCount],
                TextValues = new string?[schema.StaticTextCount]
            };
            snapshot.SetRow(
                rowIndex,
                staticData,
                dynamicNumericValues,
                new string?[schema.DynamicTextCount]);
        }

        snapshot.CompleteWrite(rows.Length);
        return snapshot;
    }

    private static long EncodeDouble(double value) => BitConverter.DoubleToInt64Bits(value);
}
