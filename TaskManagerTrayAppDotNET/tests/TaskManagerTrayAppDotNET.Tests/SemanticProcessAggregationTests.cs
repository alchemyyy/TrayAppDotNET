using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class SemanticProcessAggregationTests
{
    [Fact]
    public void DoubleAggregateCountsOnlyRequestedMembers()
    {
        ProcessSnapshotBuffer snapshot = CreateSnapshot(
            ProcessTableColumnKind.CPU,
            EncodeDouble(12.5),
            EncodeDouble(90),
            EncodeDouble(7.25));

        long aggregate = SemanticProcessAggregation.AggregateDynamicNumeric(
            snapshot,
            [0, 2],
            ProcessTableColumnKind.CPU,
            representativeRowIndex: 0);

        Assert.Equal(expected: 19.75, BitConverter.Int64BitsToDouble(aggregate), precision: 10);
    }

    [Fact]
    public void MemoryAggregateIgnoresUnavailableMembers()
    {
        ProcessSnapshotBuffer snapshot = CreateSnapshot(
            ProcessTableColumnKind.PrivateMemory,
            1_000,
            -1,
            2_500);

        long aggregate = SemanticProcessAggregation.AggregateDynamicNumeric(
            snapshot,
            [0, 1, 2],
            ProcessTableColumnKind.PrivateMemory,
            representativeRowIndex: 0);

        Assert.Equal(expected: 3_500, aggregate);
    }

    [Fact]
    public void LifetimeAggregateUsesOldestMemberInsteadOfSumming()
    {
        ProcessSnapshotBuffer snapshot = CreateSnapshot(
            ProcessTableColumnKind.Lifetime,
            100,
            400,
            250);

        long aggregate = SemanticProcessAggregation.AggregateDynamicNumeric(
            snapshot,
            [0, 1, 2],
            ProcessTableColumnKind.Lifetime,
            representativeRowIndex: 0);

        Assert.Equal(expected: 400, aggregate);
    }

    [Fact]
    public void NonAggregateColumnUsesRepresentativeValue()
    {
        ProcessSnapshotBuffer snapshot = CreateSnapshot(
            ProcessTableColumnKind.BasePriority,
            8,
            13);

        long aggregate = SemanticProcessAggregation.AggregateDynamicNumeric(
            snapshot,
            [0, 1],
            ProcessTableColumnKind.BasePriority,
            representativeRowIndex: 1);

        Assert.Equal(expected: 13, aggregate);
    }

    private static ProcessSnapshotBuffer CreateSnapshot(
        ProcessTableColumnKind column,
        params long[] values)
    {
        ProcessColumnSetting setting = new()
        {
            Column = column,
            Visible = true,
            Width = ProcessTableColumnCatalog.Get(column).DefaultWidth
        };
        ProcessDataSchema schema = ProcessDataSchema.Create([setting]);
        ProcessSnapshotBuffer snapshot = new();
        snapshot.BeginWrite(schema, values.Length);
        int dynamicSlot = schema.GetDynamicNumericSlot(column);
        for (int rowIndex = 0; rowIndex < values.Length; rowIndex++)
        {
            long[] dynamicValues = new long[schema.DynamicNumericCount];
            dynamicValues[dynamicSlot] = values[rowIndex];
            ProcessImageIdentity image = new(
                key: rowIndex.ToString(),
                name: $"process-{rowIndex}",
                imagePath: string.Empty,
                description: string.Empty,
                iconSource: default);
            ProcessStaticData staticData = new()
            {
                InstanceKey = new ProcessInstanceKey(rowIndex + 1, rowIndex + 100),
                Image = image,
                UserName = string.Empty,
                NumericValues = new long[schema.StaticNumericCount],
                TextValues = new string?[schema.StaticTextCount]
            };
            snapshot.SetRow(
                rowIndex,
                staticData,
                dynamicValues,
                new string?[schema.DynamicTextCount]);
        }

        snapshot.CompleteWrite(values.Length);
        return snapshot;
    }

    private static long EncodeDouble(double value) => BitConverter.DoubleToInt64Bits(value);
}
