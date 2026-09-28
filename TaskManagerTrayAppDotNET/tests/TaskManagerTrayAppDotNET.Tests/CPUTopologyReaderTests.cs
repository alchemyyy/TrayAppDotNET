using System.Buffers.Binary;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class CPUTopologyReaderTests
{
    private const int NativeProcessorRelationshipSize = 48;
    private const int NativeCacheRelationshipSize = 56;
    private const uint RelationCache = 2;
    private const uint BytesPerMebibyte = 1_048_576;

    [Fact]
    public void BuildCCDTopologyMapsPhysicalCoresToDeterministicallyOrderedCCDs()
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

        CPUCCDTopology topology = CPUTopologyReader.BuildCCDTopology(
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
    public void BuildCCDTopologyUsesProcessorGroupThenNumberForSystemIndexes()
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

        CPUCCDTopology topology = CPUTopologyReader.BuildCCDTopology(
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
    public void BuildCCDTopologyRejectsPhysicalCoreSplitAcrossDies()
    {
        ProcessorRelationshipMasks[] cores = [Relationship(group: 0, mask: 0x03)];
        ProcessorRelationshipMasks[] dies =
        [
            Relationship(group: 0, mask: 0x01),
            Relationship(group: 0, mask: 0x02)
        ];

        CPUCCDTopology topology = CPUTopologyReader.BuildCCDTopology(
            cores,
            dies,
            CPUCCDTopologySource.WindowsProcessorDie);

        Assert.False(topology.IsAvailable);
        Assert.Same(CPUCCDTopology.Empty, topology);
    }

    [Fact]
    public void BuildCCDTopologyRejectsIncompleteDieCoverage()
    {
        ProcessorRelationshipMasks[] cores =
        [
            Relationship(group: 0, mask: 0x03),
            Relationship(group: 0, mask: 0x0C)
        ];
        ProcessorRelationshipMasks[] dies = [Relationship(group: 0, mask: 0x03)];

        CPUCCDTopology topology = CPUTopologyReader.BuildCCDTopology(
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

        bool parsed = CPUTopologyReader.TryParseProcessorRelationships(
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
    public void ParserReadsCoreEfficiencyClass()
    {
        byte[] buffer = new byte[NativeProcessorRelationshipSize * 2];
        WriteProcessorRelationship(
            buffer,
            offset: 0,
            relationship: 0,
            group: 0,
            mask: 0x03,
            efficiencyClass: 1);
        WriteProcessorRelationship(
            buffer,
            NativeProcessorRelationshipSize,
            relationship: 0,
            group: 0,
            mask: 0x04);

        bool parsed = CPUTopologyReader.TryParseProcessorRelationships(
            buffer,
            expectedRelationship: 0,
            out ProcessorRelationshipMasks[] relationships);

        Assert.True(parsed);
        Assert.Equal(
            new byte[] { 1, 0 },
            relationships.Select(static core => core.EfficiencyClass).ToArray());
    }

    [Fact]
    public void ParserRejectsUnexpectedRelationshipType()
    {
        byte[] buffer = new byte[NativeProcessorRelationshipSize];
        WriteProcessorRelationship(buffer, offset: 0, relationship: 0, group: 0, mask: 0x03);

        bool parsed = CPUTopologyReader.TryParseProcessorRelationships(
            buffer,
            expectedRelationship: 5,
            out ProcessorRelationshipMasks[] relationships);

        Assert.False(parsed);
        Assert.Empty(relationships);
    }

    [Fact]
    public void CacheParserReadsLevelTypeSizeAndMasks()
    {
        byte[] buffer = new byte[NativeCacheRelationshipSize * 2];
        WriteCacheRelationship(
            buffer,
            offset: 0,
            level: 2,
            sizeBytes: 1_048_576,
            groupCount: 1,
            group: 0,
            mask: 0x03);
        WriteCacheRelationship(
            buffer,
            NativeCacheRelationshipSize,
            level: 3,
            sizeBytes: 96 * BytesPerMebibyte,
            groupCount: 1,
            group: 1,
            mask: 0xFFFF);

        bool parsed = CPUTopologyReader.TryParseCacheRelationships(
            buffer,
            out CacheRelationshipMasks[] caches);

        Assert.True(parsed);
        Assert.Collection(
            caches,
            static cache =>
            {
                Assert.Equal(expected: 2, cache.Level);
                Assert.Equal(expected: 0U, cache.Type);
                Assert.Equal(expected: 1_048_576U, cache.CacheSizeBytes);
                Assert.Equal(
                    new ProcessorGroupAffinityMask(Group: 0, Mask: 0x03),
                    Assert.Single(cache.GroupMasks.ToArray()));
            },
            static cache =>
            {
                Assert.Equal(expected: 3, cache.Level);
                Assert.Equal(96 * BytesPerMebibyte, cache.CacheSizeBytes);
                Assert.Equal(
                    new ProcessorGroupAffinityMask(Group: 1, Mask: 0xFFFF),
                    Assert.Single(cache.GroupMasks.ToArray()));
            });
    }

    [Fact]
    public void CacheParserReadsOneMaskWhenGroupCountIsZero()
    {
        byte[] buffer = new byte[NativeCacheRelationshipSize];
        WriteCacheRelationship(
            buffer,
            offset: 0,
            level: 3,
            sizeBytes: 32 * BytesPerMebibyte,
            groupCount: 0,
            group: 0,
            mask: 0xFFF);

        bool parsed = CPUTopologyReader.TryParseCacheRelationships(
            buffer,
            out CacheRelationshipMasks[] caches);

        Assert.True(parsed);
        Assert.Equal(
            new ProcessorGroupAffinityMask(Group: 0, Mask: 0xFFF),
            Assert.Single(Assert.Single(caches).GroupMasks.ToArray()));
    }

    [Fact]
    public void CacheParserRejectsMasksBeyondTheRecord()
    {
        byte[] buffer = new byte[NativeCacheRelationshipSize];
        WriteCacheRelationship(
            buffer,
            offset: 0,
            level: 3,
            sizeBytes: 32 * BytesPerMebibyte,
            groupCount: 2,
            group: 0,
            mask: 0xFFF);

        bool parsed = CPUTopologyReader.TryParseCacheRelationships(
            buffer,
            out CacheRelationshipMasks[] caches);

        Assert.False(parsed);
        Assert.Empty(caches);
    }

    [Fact]
    public void CoreClassesAreOrderedFromHighestEfficiencyClass()
    {
        ProcessorRelationshipMasks[] cores =
        [
            Relationship(group: 0, mask: 0x10),
            Relationship(group: 0, mask: 0x03, efficiencyClass: 1),
            Relationship(group: 0, mask: 0x20),
            Relationship(group: 0, mask: 0x0C, efficiencyClass: 1)
        ];

        CPUCoreClassTopology topology = CPUTopologyReader.BuildCoreClassTopology(
            cores,
            [L3Cache(group: 0, mask: 0x3F, mebibytes: 36)]);

        Assert.True(topology.IsHeterogeneous);
        Assert.Collection(
            topology.Classes.ToArray(),
            static coreClass =>
            {
                Assert.Equal(expected: 1, coreClass.EfficiencyClass);
                Assert.False(coreClass.IsOutsideL3Cache);
                Assert.Equal(expected: 2, coreClass.CoreCount);
                Assert.Equal([0, 1, 2, 3], coreClass.LogicalProcessorIndexes.ToArray());
            },
            static coreClass =>
            {
                Assert.Equal(expected: 0, coreClass.EfficiencyClass);
                Assert.False(coreClass.IsOutsideL3Cache);
                Assert.Equal(expected: 2, coreClass.CoreCount);
                Assert.Equal([4, 5], coreClass.LogicalProcessorIndexes.ToArray());
            });
    }

    [Fact]
    public void LowerClassCoresOutsideEveryL3AreSeparated()
    {
        ProcessorRelationshipMasks[] cores =
        [
            Relationship(group: 0, mask: 0x03, efficiencyClass: 1),
            Relationship(group: 0, mask: 0x04),
            Relationship(group: 0, mask: 0x08),
            Relationship(group: 0, mask: 0x10),
            Relationship(group: 0, mask: 0x20)
        ];
        CacheRelationshipMasks[] caches =
        [
            new(Level: 2, Type: 0, 2 * BytesPerMebibyte, new ProcessorGroupAffinityMask[] { new(Group: 0, Mask: 0x30) }),
            L3Cache(group: 0, mask: 0x0F, mebibytes: 24)
        ];

        CPUCoreClassTopology topology = CPUTopologyReader.BuildCoreClassTopology(cores, caches);

        Assert.Collection(
            topology.Classes.ToArray(),
            static coreClass =>
            {
                Assert.Equal(expected: 1, coreClass.EfficiencyClass);
                Assert.False(coreClass.IsOutsideL3Cache);
            },
            static coreClass =>
            {
                Assert.Equal(expected: 0, coreClass.EfficiencyClass);
                Assert.False(coreClass.IsOutsideL3Cache);
                Assert.Equal([2, 3], coreClass.LogicalProcessorIndexes.ToArray());
            },
            static coreClass =>
            {
                Assert.Equal(expected: 0, coreClass.EfficiencyClass);
                Assert.True(coreClass.IsOutsideL3Cache);
                Assert.Equal(expected: 2, coreClass.CoreCount);
                Assert.Equal([4, 5], coreClass.LogicalProcessorIndexes.ToArray());
            });
    }

    [Fact]
    public void CoreClassesStayWholeWithoutL3Records()
    {
        ProcessorRelationshipMasks[] cores =
        [
            Relationship(group: 0, mask: 0x03, efficiencyClass: 1),
            Relationship(group: 0, mask: 0x04),
            Relationship(group: 0, mask: 0x08)
        ];

        CPUCoreClassTopology topology = CPUTopologyReader.BuildCoreClassTopology(cores, []);

        Assert.Equal(expected: 2, topology.Classes.Length);
        Assert.All(topology.Classes.ToArray(), static coreClass => Assert.False(coreClass.IsOutsideL3Cache));
    }

    [Fact]
    public void OneEfficiencyClassIsHomogeneousEvenWhenSomeCoresLackL3()
    {
        ProcessorRelationshipMasks[] cores =
        [
            Relationship(group: 0, mask: 0x03),
            Relationship(group: 0, mask: 0x0C)
        ];

        CPUCoreClassTopology topology = CPUTopologyReader.BuildCoreClassTopology(
            cores,
            [L3Cache(group: 0, mask: 0x03, mebibytes: 16)]);

        Assert.True(topology.IsAvailable);
        Assert.False(topology.IsHeterogeneous);
        Assert.Equal([0, 1, 2, 3], Assert.Single(topology.Classes.ToArray()).LogicalProcessorIndexes.ToArray());
    }

    [Fact]
    public void CoreClassesRejectDuplicateLogicalProcessors()
    {
        ProcessorRelationshipMasks[] cores =
        [
            Relationship(group: 0, mask: 0x03, efficiencyClass: 1),
            Relationship(group: 0, mask: 0x02)
        ];

        CPUCoreClassTopology topology = CPUTopologyReader.BuildCoreClassTopology(cores, []);

        Assert.Same(CPUCoreClassTopology.Empty, topology);
    }

    [Fact]
    public void L3CacheSizesAreAttributedToTheirCCDs()
    {
        CPUCCDTopology topology = BuildTwoCCDTopology();
        CacheRelationshipMasks[] caches =
        [
            new(Level: 2, Type: 0, BytesPerMebibyte, new ProcessorGroupAffinityMask[] { new(Group: 0, Mask: 0x03) }),
            L3Cache(group: 0, mask: 0x0F, mebibytes: 96),
            L3Cache(group: 0, mask: 0xF0, mebibytes: 32)
        ];

        CPUCCDTopology topologyWithCaches = CPUTopologyReader.AssignL3CacheSizes(topology, caches);

        Assert.Equal(96 * (ulong)BytesPerMebibyte, topologyWithCaches.CCDs.Span[0].L3CacheBytes);
        Assert.Equal(32 * (ulong)BytesPerMebibyte, topologyWithCaches.CCDs.Span[1].L3CacheBytes);
        Assert.Equal(
            topology.CCDs.Span[0].LogicalProcessorIndexes.ToArray(),
            topologyWithCaches.CCDs.Span[0].LogicalProcessorIndexes.ToArray());
    }

    [Fact]
    public void SeveralL3DomainsInOneCCDAreSummed()
    {
        CPUCCDTopology topology = BuildTwoCCDTopology();
        CacheRelationshipMasks[] caches =
        [
            L3Cache(group: 0, mask: 0x03, mebibytes: 16),
            L3Cache(group: 0, mask: 0x0C, mebibytes: 16),
            L3Cache(group: 0, mask: 0xF0, mebibytes: 32)
        ];

        CPUCCDTopology topologyWithCaches = CPUTopologyReader.AssignL3CacheSizes(topology, caches);

        Assert.All(
            topologyWithCaches.CCDs.ToArray(),
            static CCD => Assert.Equal(32 * (ulong)BytesPerMebibyte, CCD.L3CacheBytes));
    }

    [Fact]
    public void L3SpanningCCDsLeavesEveryCCDUnattributed()
    {
        CPUCCDTopology topology = BuildTwoCCDTopology();
        CacheRelationshipMasks[] caches =
        [
            L3Cache(group: 0, mask: 0x0F, mebibytes: 32),
            L3Cache(group: 0, mask: 0xF0, mebibytes: 32),
            L3Cache(group: 0, mask: 0x18, mebibytes: 8)
        ];

        CPUCCDTopology topologyWithCaches = CPUTopologyReader.AssignL3CacheSizes(topology, caches);

        Assert.Same(topology, topologyWithCaches);
        Assert.All(topologyWithCaches.CCDs.ToArray(), static CCD => Assert.Equal(expected: 0UL, CCD.L3CacheBytes));
    }

    [Fact]
    public void AMDCPUIDDecoderExtractsDieDomainFromExtendedAPICID()
    {
        bool decoded = CPUTopologyReader.TryDecodeAMDCCDTopologyLevel(
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
        bool decoded = CPUTopologyReader.TryDecodeAMDCCDTopologyLevel(
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
        CPUCCDTopology topology = CPUTopologyReader.ReadCCDTopology();
        if (!topology.IsAvailable) return;

        Assert.True(CPUTopologyReader.IsAMDProcessor());
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
        Assert.All(topology.CCDs.ToArray(), static CCD =>
        {
            Assert.NotEmpty(CCD.CoreIndexes.ToArray());
            Assert.NotEmpty(CCD.LogicalProcessorIndexes.ToArray());
            Assert.True(CCD.L3CacheBytes > 0);
        });
    }

    [Fact]
    public void LiveCoreClassesCoverEveryLogicalProcessorOnce()
    {
        CPUCoreClassTopology topology = CPUTopologyReader.ReadCoreClassTopology();

        Assert.True(topology.IsAvailable);
        int[] logicalProcessorIndexes = topology.Classes.ToArray()
            .SelectMany(static coreClass => coreClass.LogicalProcessorIndexes.ToArray())
            .Order()
            .ToArray();
        Assert.Equal(Enumerable.Range(start: 0, logicalProcessorIndexes.Length), logicalProcessorIndexes);
        Assert.Equal(
            topology.Classes.ToArray().Select(static coreClass => coreClass.EfficiencyClass).OrderDescending(),
            topology.Classes.ToArray().Select(static coreClass => coreClass.EfficiencyClass));
    }

    [Fact]
    public void LiveAMDCPUIDFallbackProducesCompleteTopologyWhenAdvertised()
    {
        if (!CPUTopologyReader.SupportsAMDExtendedCPUTopology()) return;

        CPUCCDTopology topology = CPUTopologyReader.ReadAMDExtendedCPUTopology();

        Assert.True(topology.IsAvailable);
        Assert.Equal(CPUCCDTopologySource.AMDExtendedCPUTopology, topology.Source);
        Assert.NotEmpty(topology.LogicalProcessors.ToArray());
        Assert.NotEmpty(topology.Cores.ToArray());
        Assert.NotEmpty(topology.CCDs.ToArray());
        Assert.All(
            topology.CCDs.ToArray(),
            static CCD => Assert.NotNull(CCD.HardwareTopologyID));
    }

    [Fact]
    public void PerformanceSnapshotExposesApplicationLifetimeTopologies()
    {
        CPUCCDTopology expectedCCDTopology = CPUTopologyReader.ReadCCDTopology();
        CPUCoreClassTopology expectedCoreClassTopology = CPUTopologyReader.ReadCoreClassTopology();
        using PerformanceSnapshotService service = new();

        CPUPerformanceSnapshot snapshot = service.SampleNow().CPU;

        Assert.Equal(expectedCCDTopology.Source, snapshot.CCDTopology.Source);
        Assert.Equal(
            expectedCCDTopology.LogicalProcessors.ToArray(),
            snapshot.CCDTopology.LogicalProcessors.ToArray());
        Assert.Equal(expectedCCDTopology.Cores.Length, snapshot.CCDTopology.Cores.Length);
        Assert.Equal(expectedCCDTopology.CCDs.Length, snapshot.CCDTopology.CCDs.Length);
        Assert.Equal(expectedCoreClassTopology.Classes.Length, snapshot.CoreClassTopology.Classes.Length);
    }

    private static CPUCCDTopology BuildTwoCCDTopology() =>
        CPUTopologyReader.BuildCCDTopology(
            [
                Relationship(group: 0, mask: 0x03),
                Relationship(group: 0, mask: 0x0C),
                Relationship(group: 0, mask: 0x30),
                Relationship(group: 0, mask: 0xC0)
            ],
            [
                Relationship(group: 0, mask: 0x0F),
                Relationship(group: 0, mask: 0xF0)
            ],
            CPUCCDTopologySource.WindowsProcessorDie);

    private static ProcessorRelationshipMasks Relationship(
        ushort group,
        ulong mask,
        uint? hardwareTopologyID = null,
        byte efficiencyClass = 0) =>
        new(
            new ProcessorGroupAffinityMask[] { new(group, mask) },
            hardwareTopologyID,
            efficiencyClass);

    private static CacheRelationshipMasks L3Cache(ushort group, ulong mask, uint mebibytes) =>
        new(
            Level: 3,
            Type: 0,
            mebibytes * BytesPerMebibyte,
            new ProcessorGroupAffinityMask[] { new(group, mask) });

    private static void WriteProcessorRelationship(
        Span<byte> buffer,
        int offset,
        uint relationship,
        ushort group,
        ulong mask,
        byte efficiencyClass = 0)
    {
        Span<byte> entry = buffer.Slice(offset, NativeProcessorRelationshipSize);
        BinaryPrimitives.WriteUInt32LittleEndian(entry, relationship);
        BinaryPrimitives.WriteUInt32LittleEndian(
            entry[sizeof(uint)..],
            NativeProcessorRelationshipSize);
        entry[9] = efficiencyClass;
        BinaryPrimitives.WriteUInt16LittleEndian(entry[30..], value: 1);
        BinaryPrimitives.WriteUInt64LittleEndian(entry[32..], mask);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[40..], group);
    }

    private static void WriteCacheRelationship(
        Span<byte> buffer,
        int offset,
        byte level,
        uint sizeBytes,
        ushort groupCount,
        ushort group,
        ulong mask)
    {
        Span<byte> entry = buffer.Slice(offset, NativeCacheRelationshipSize);
        BinaryPrimitives.WriteUInt32LittleEndian(entry, RelationCache);
        BinaryPrimitives.WriteUInt32LittleEndian(
            entry[sizeof(uint)..],
            NativeCacheRelationshipSize);
        entry[8] = level;
        BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], sizeBytes);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[38..], groupCount);
        BinaryPrimitives.WriteUInt64LittleEndian(entry[40..], mask);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[48..], group);
    }
}
