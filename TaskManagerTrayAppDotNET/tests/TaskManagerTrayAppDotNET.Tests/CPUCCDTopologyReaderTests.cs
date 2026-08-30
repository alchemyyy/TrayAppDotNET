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
            Relationship(0, 0xC0),
            Relationship(0, 0x03),
            Relationship(0, 0x30),
            Relationship(0, 0x0C)
        ];
        ProcessorRelationshipMasks[] dies =
        [
            Relationship(0, 0xF0, 9),
            Relationship(0, 0x0F, 4)
        ];

        CPUCCDTopology topology = CPUCCDTopologyReader.BuildTopology(
            cores,
            dies,
            CPUCCDTopologySource.AMDExtendedCPUTopology);

        Assert.True(topology.IsAvailable);
        Assert.Equal(CPUCCDTopologySource.AMDExtendedCPUTopology, topology.Source);
        Assert.Equal(8, topology.LogicalProcessors.Length);
        Assert.Equal(4, topology.Cores.Length);
        Assert.Equal(2, topology.CCDs.Length);
        Assert.Equal([0, 1], topology.CCDs.Span[0].CoreIndexes.ToArray());
        Assert.Equal([0, 1, 2, 3], topology.CCDs.Span[0].LogicalProcessorIndexes.ToArray());
        Assert.Equal(4U, topology.CCDs.Span[0].HardwareTopologyID);
        Assert.Equal([2, 3], topology.CCDs.Span[1].CoreIndexes.ToArray());
        Assert.Equal([4, 5, 6, 7], topology.CCDs.Span[1].LogicalProcessorIndexes.ToArray());
        Assert.Equal(9U, topology.CCDs.Span[1].HardwareTopologyID);
        Assert.All(
            topology.Cores.Span[..2].ToArray(),
            static (CPUCoreTopologyEntry core) => Assert.Equal(0, core.CCDIndex));
        Assert.All(
            topology.Cores.Span[2..].ToArray(),
            static (CPUCoreTopologyEntry core) => Assert.Equal(1, core.CCDIndex));
    }

    [Fact]
    public void BuildTopologyUsesProcessorGroupThenNumberForSystemIndexes()
    {
        ProcessorRelationshipMasks[] cores =
        [
            Relationship(1, 0x03),
            Relationship(0, 0x03)
        ];
        ProcessorRelationshipMasks[] dies =
        [
            Relationship(1, 0x03),
            Relationship(0, 0x03)
        ];

        CPUCCDTopology topology = CPUCCDTopologyReader.BuildTopology(
            cores,
            dies,
            CPUCCDTopologySource.WindowsProcessorDie);

        Assert.True(topology.IsAvailable);
        Assert.Collection(
            topology.LogicalProcessors.ToArray(),
            static processor => Assert.Equal(new CPULogicalProcessor(0, 0, 0), processor),
            static processor => Assert.Equal(new CPULogicalProcessor(1, 0, 1), processor),
            static processor => Assert.Equal(new CPULogicalProcessor(2, 1, 0), processor),
            static processor => Assert.Equal(new CPULogicalProcessor(3, 1, 1), processor));
        Assert.Equal([0, 1], topology.CCDs.Span[0].LogicalProcessorIndexes.ToArray());
        Assert.Equal([2, 3], topology.CCDs.Span[1].LogicalProcessorIndexes.ToArray());
        Assert.Null(topology.CCDs.Span[0].HardwareTopologyID);
    }

    [Fact]
    public void BuildTopologyRejectsPhysicalCoreSplitAcrossDies()
    {
        ProcessorRelationshipMasks[] cores = [Relationship(0, 0x03)];
        ProcessorRelationshipMasks[] dies =
        [
            Relationship(0, 0x01),
            Relationship(0, 0x02)
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
            Relationship(0, 0x03),
            Relationship(0, 0x0C)
        ];
        ProcessorRelationshipMasks[] dies = [Relationship(0, 0x03)];

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
        WriteProcessorRelationship(buffer, 0, relationship: 5, group: 0, mask: 0x0F);
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
                new ProcessorGroupAffinityMask(0, 0x0F),
                Assert.Single(relationship.GroupMasks.ToArray())),
            static relationship => Assert.Equal(
                new ProcessorGroupAffinityMask(1, 0xF0),
                Assert.Single(relationship.GroupMasks.ToArray())));
    }

    [Fact]
    public void ParserRejectsUnexpectedRelationshipType()
    {
        byte[] buffer = new byte[NativeProcessorRelationshipSize];
        WriteProcessorRelationship(buffer, 0, relationship: 0, group: 0, mask: 0x03);

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
        Assert.Equal(1U, hardwareTopologyID);
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
            Enumerable.Range(0, topology.LogicalProcessors.Length),
            topology.LogicalProcessors.ToArray().Select(
                static processor => processor.SystemIndex));
        Assert.All(topology.Cores.ToArray(), core =>
        {
            Assert.InRange(core.CCDIndex, 0, topology.CCDs.Length - 1);
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
            new ProcessorGroupAffinityMask[]
            {
                new(group, mask)
            },
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
        BinaryPrimitives.WriteUInt16LittleEndian(entry[30..], 1);
        BinaryPrimitives.WriteUInt64LittleEndian(entry[32..], mask);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[40..], group);
    }
}
