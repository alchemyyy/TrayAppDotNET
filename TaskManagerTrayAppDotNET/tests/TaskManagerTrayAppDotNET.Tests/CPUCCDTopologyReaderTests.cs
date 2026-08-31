using System.Buffers.Binary;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class CPUCCDTopologyReaderTests
{
    private const int NativeProcessorRelationshipSize = 48;

    [Fact]
    public void BuildTopologyMapsPhysicalCoresToDeterministicallyOrderedCCDs()
    {
        ProcessorRelationshipMasks[] cores =
        [
            Relationship(group: 0, mask: 0xC0),
            Relationship(group: 0, mask: 0x03),
            Relationship(group: 0, mask: 0x30),
            Relationship(group: 0, mask: 0x0C)
        ];
        ProcessorRelationshipMasks[] dies =
        [
            Relationship(group: 0, mask: 0xF0, hardwareTopologyID: 9),
            Relationship(group: 0, mask: 0x0F, hardwareTopologyID: 4)
        ];

        CPUCCDTopology topology = CPUCCDTopologyReader.BuildTopology(
            cores,
            dies,
            CPUCCDTopologySource.AMDExtendedCPUTopology);

        Assert.True(topology.IsAvailable);
        Assert.Equal(CPUCCDTopologySource.AMDExtendedCPUTopology, topology.Source);
        Assert.Equal(expected: 8, topology.LogicalProcessors.Length);
        Assert.Equal(expected: 4, topology.Cores.Length);
        Assert.Equal(expected: 2, topology.CCDs.Length);
        Assert.Equal([0, 1], topology.CCDs.Span[0].CoreIndexes.ToArray());
        Assert.Equal([0, 1, 2, 3], topology.CCDs.Span[0].LogicalProcessorIndexes.ToArray());
        Assert.Equal(expected: 4U, topology.CCDs.Span[0].HardwareTopologyID);
        Assert.Equal([2, 3], topology.CCDs.Span[1].CoreIndexes.ToArray());
        Assert.Equal([4, 5, 6, 7], topology.CCDs.Span[1].LogicalProcessorIndexes.ToArray());
        Assert.Equal(expected: 9U, topology.CCDs.Span[1].HardwareTopologyID);
        Assert.All(
            topology.Cores.Span[..2].ToArray(),
            static core => Assert.Equal(expected: 0, core.CCDIndex));
        Assert.All(
            topology.Cores.Span[2..].ToArray(),
            static core => Assert.Equal(expected: 1, core.CCDIndex));
    }

    [Fact]
    public void BuildTopologyUsesProcessorGroupThenNumberForSystemIndexes()
    {
        ProcessorRelationshipMasks[] cores =
        [
            Relationship(group: 1, mask: 0x03),
            Relationship(group: 0, mask: 0x03)
        ];
        ProcessorRelationshipMasks[] dies =
        [
            Relationship(group: 1, mask: 0x03),
            Relationship(group: 0, mask: 0x03)
        ];

        CPUCCDTopology topology = CPUCCDTopologyReader.BuildTopology(
            cores,
            dies,
            CPUCCDTopologySource.WindowsProcessorDie);

        Assert.True(topology.IsAvailable);
        Assert.Collection(
            topology.LogicalProcessors.ToArray(),
            static processor => Assert.Equal(new CPULogicalProcessor(SystemIndex: 0, Group: 0, Number: 0), processor),
            static processor => Assert.Equal(new CPULogicalProcessor(SystemIndex: 1, Group: 0, Number: 1), processor),
            static processor => Assert.Equal(new CPULogicalProcessor(SystemIndex: 2, Group: 1, Number: 0), processor),
            static processor => Assert.Equal(new CPULogicalProcessor(SystemIndex: 3, Group: 1, Number: 1), processor));
        Assert.Equal([0, 1], topology.CCDs.Span[0].LogicalProcessorIndexes.ToArray());
        Assert.Equal([2, 3], topology.CCDs.Span[1].LogicalProcessorIndexes.ToArray());
        Assert.Null(topology.CCDs.Span[0].HardwareTopologyID);
    }

    [Fact]
    public void BuildTopologyRejectsPhysicalCoreSplitAcrossDies()
    {
        ProcessorRelationshipMasks[] cores = [Relationship(group: 0, mask: 0x03)];
        ProcessorRelationshipMasks[] dies =
        [
            Relationship(group: 0, mask: 0x01),
            Relationship(group: 0, mask: 0x02)
        ];

        CPUCCDTopology topology = CPUCCDTopologyReader.BuildTopology(
            cores,
            dies,
            CPUCCDTopologySource.WindowsProcessorDie);

        Assert.False(topology.IsAvailable);
        Assert.Same(CPUCCDTopology.Empty, topology);
    }

    [Fact]
    public void BuildTopologyRejectsIncompleteDieCoverage()
    {
        ProcessorRelationshipMasks[] cores =
        [
            Relationship(group: 0, mask: 0x03),
            Relationship(group: 0, mask: 0x0C)
        ];
        ProcessorRelationshipMasks[] dies = [Relationship(group: 0, mask: 0x03)];

        CPUCCDTopology topology = CPUCCDTopologyReader.BuildTopology(
            cores,
            dies,
            CPUCCDTopologySource.WindowsProcessorDie);

        Assert.Same(CPUCCDTopology.Empty, topology);
    }

    [Fact]
    public void ParserReadsVariableProcessorRelationshipRecords()
    {
        byte[] buffer = new byte[NativeProcessorRelationshipSize * 2];
        WriteProcessorRelationship(buffer, offset: 0, relationship: 5, group: 0, mask: 0x0F);
        WriteProcessorRelationship(
            buffer,
            NativeProcessorRelationshipSize,
            relationship: 5,
            group: 1,
            mask: 0xF0);

        bool parsed = CPUCCDTopologyReader.TryParseProcessorRelationships(
            buffer,
            expectedRelationship: 5,
            out ProcessorRelationshipMasks[] relationships);

        Assert.True(parsed);
        Assert.Collection(
            relationships,
            static relationship => Assert.Equal(
                new ProcessorGroupAffinityMask(Group: 0, Mask: 0x0F),
                Assert.Single(relationship.GroupMasks.ToArray())),
            static relationship => Assert.Equal(
                new ProcessorGroupAffinityMask(Group: 1, Mask: 0xF0),
                Assert.Single(relationship.GroupMasks.ToArray())));
    }

    [Fact]
    public void ParserRejectsUnexpectedRelationshipType()
    {
        byte[] buffer = new byte[NativeProcessorRelationshipSize];
        WriteProcessorRelationship(buffer, offset: 0, relationship: 0, group: 0, mask: 0x03);

        bool parsed = CPUCCDTopologyReader.TryParseProcessorRelationships(
            buffer,
            expectedRelationship: 5,
            out ProcessorRelationshipMasks[] relationships);

        Assert.False(parsed);
        Assert.Empty(relationships);
    }

    [Fact]
    public void AMDCPUIDDecoderExtractsDieDomainFromExtendedAPICID()
    {
        bool decoded = CPUCCDTopologyReader.TryDecodeAMDCCDTopologyLevel(
            eax: 4,
            ebx: 12,
            ecx: 0x00000302,
            edx: 0x1B,
            out uint hardwareTopologyID);

        Assert.True(decoded);
        Assert.Equal(expected: 1U, hardwareTopologyID);
    }

    [Theory]
    [InlineData(0x00000201, 12)]
    [InlineData(0x00000302, 0)]
    public void AMDCPUIDDecoderRejectsNonDieOrEmptyLevels(int ecx, int ebx)
    {
        bool decoded = CPUCCDTopologyReader.TryDecodeAMDCCDTopologyLevel(
            eax: 4,
            ebx,
            ecx,
            edx: 0x1B,
            out _);

        Assert.False(decoded);
    }

    [Fact]
    public void LiveTopologyIsInternallyConsistentWhenAvailable()
    {
        CPUCCDTopology topology = CPUCCDTopologyReader.Read();
        if (!topology.IsAvailable) return;

        Assert.True(CPUCCDTopologyReader.IsAMDProcessor());
        Assert.NotEqual(CPUCCDTopologySource.None, topology.Source);
        Assert.NotEmpty(topology.LogicalProcessors.ToArray());
        Assert.NotEmpty(topology.Cores.ToArray());
        Assert.NotEmpty(topology.CCDs.ToArray());
        Assert.Equal(
            Enumerable.Range(start: 0, topology.LogicalProcessors.Length),
            topology.LogicalProcessors.ToArray().Select(static processor => processor.SystemIndex));
        Assert.All(topology.Cores.ToArray(), core =>
        {
            Assert.InRange(core.CCDIndex, low: 0, topology.CCDs.Length - 1);
            Assert.NotEmpty(core.LogicalProcessorIndexes.ToArray());
        });
        Assert.All(topology.CCDs.ToArray(), ccd =>
        {
            Assert.NotEmpty(ccd.CoreIndexes.ToArray());
            Assert.NotEmpty(ccd.LogicalProcessorIndexes.ToArray());
        });
    }

    [Fact]
    public void LiveAMDCPUIDFallbackProducesCompleteTopologyWhenAdvertised()
    {
        if (!CPUCCDTopologyReader.SupportsAMDExtendedCPUTopology()) return;

        CPUCCDTopology topology = CPUCCDTopologyReader.ReadAMDExtendedCPUTopology();

        Assert.True(topology.IsAvailable);
        Assert.Equal(CPUCCDTopologySource.AMDExtendedCPUTopology, topology.Source);
        Assert.NotEmpty(topology.LogicalProcessors.ToArray());
        Assert.NotEmpty(topology.Cores.ToArray());
        Assert.NotEmpty(topology.CCDs.ToArray());
        Assert.All(
            topology.CCDs.ToArray(),
            static ccd => Assert.NotNull(ccd.HardwareTopologyID));
    }

    [Fact]
    public void PerformanceSnapshotExposesApplicationLifetimeCCDTopology()
    {
        CPUCCDTopology expected = CPUCCDTopologyReader.Read();
        using PerformanceSnapshotService service = new();

        CPUCCDTopology actual = service.SampleNow().CPU.CCDTopology;

        Assert.Equal(expected.Source, actual.Source);
        Assert.Equal(expected.LogicalProcessors.ToArray(), actual.LogicalProcessors.ToArray());
        Assert.Equal(expected.Cores.Length, actual.Cores.Length);
        Assert.Equal(expected.CCDs.Length, actual.CCDs.Length);
    }

    private static ProcessorRelationshipMasks Relationship(
        ushort group,
        ulong mask,
        uint? hardwareTopologyID = null) =>
        new(
            new ProcessorGroupAffinityMask[] { new(group, mask) },
            hardwareTopologyID);

    private static void WriteProcessorRelationship(
        Span<byte> buffer,
        int offset,
        uint relationship,
        ushort group,
        ulong mask)
    {
        Span<byte> entry = buffer.Slice(offset, NativeProcessorRelationshipSize);
        BinaryPrimitives.WriteUInt32LittleEndian(entry, relationship);
        BinaryPrimitives.WriteUInt32LittleEndian(
            entry[sizeof(uint)..],
            NativeProcessorRelationshipSize);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[30..], value: 1);
        BinaryPrimitives.WriteUInt64LittleEndian(entry[32..], mask);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[40..], group);
    }
}
