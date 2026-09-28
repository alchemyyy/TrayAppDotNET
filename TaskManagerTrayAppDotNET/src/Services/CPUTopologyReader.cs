using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Reads Windows core efficiency classes and exact AMD core-to-CCD membership without a hardware driver.</summary>
internal static unsafe class CPUTopologyReader
{
    private const uint RelationProcessorCore = 0;
    private const uint RelationCache = 2;
    private const uint RelationProcessorDie = 5;
    private const int ErrorInsufficientBuffer = 122;
    private const int LogicalProcessorInformationHeaderSize = 8;
    private const int ProcessorEfficiencyClassOffset = 9;
    private const int ProcessorGroupCountOffset = 30;
    private const int ProcessorGroupMasksOffset = 32;
    private const int CacheLevelOffset = 8;
    private const int CacheSizeOffset = 12;
    private const int CacheTypeOffset = 16;
    private const int CacheGroupCountOffset = 38;
    private const int CacheGroupMasksOffset = 40;
    private const byte L3CacheLevel = 3;
    private const uint UnifiedCacheType = 0;
    private const int GroupAffinitySize = 16;
    private const int MaximumTopologyLevelCount = 32;
    private const int AMDExtendedFunctionMaximum = unchecked((int)0x80000000);
    private const uint AMDExtendedCPUTopologyFunction = 0x80000026;
    private const uint AMDDieTopologyLevel = 3;
    private const int TopologyLevelTypeShift = 8;
    private const uint TopologyLevelTypeMask = 0xFF;
    private const uint TopologyMaskWidthMask = 0x1F;
    private const uint TopologyLogicalProcessorCountMask = 0xFFFF;
    private const int AuthenticAMDEBX = 0x68747541;
    private const int AuthenticAMDECX = 0x444D4163;
    private const int AuthenticAMDEDX = 0x69746E65;

    /// <summary>Reads the active AMD CCD topology, preferring Windows processor-die records.</summary>
    public static CPUCCDTopology ReadCCDTopology()
    {
        if (!IsAMDProcessor()) return CPUCCDTopology.Empty;

        try
        {
            if (!TryReadProcessorRelationships(
                    RelationProcessorCore,
                    out ProcessorRelationshipMasks[] coreRelationships))
                return CPUCCDTopology.Empty;

            if (TryReadProcessorRelationships(
                    RelationProcessorDie,
                    out ProcessorRelationshipMasks[] dieRelationships))
            {
                CPUCCDTopology windowsTopology = BuildCCDTopology(
                    coreRelationships,
                    dieRelationships,
                    CPUCCDTopologySource.WindowsProcessorDie);
                if (windowsTopology.IsAvailable) return AttachL3CacheSizes(windowsTopology);
            }

            return TryReadAMDExtendedCPUTopology(
                coreRelationships,
                out CPUCCDTopology amdTopology)
                ? AttachL3CacheSizes(amdTopology)
                : CPUCCDTopology.Empty;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"CPUTopologyReader.ReadCCDTopology: {exception}");
            return CPUCCDTopology.Empty;
        }
    }

    /// <summary>Reads the efficiency class and level-3 cache placement Windows reports for every core.</summary>
    public static CPUCoreClassTopology ReadCoreClassTopology()
    {
        try
        {
            if (!TryReadProcessorRelationships(
                    RelationProcessorCore,
                    out ProcessorRelationshipMasks[] coreRelationships))
                return CPUCoreClassTopology.Empty;

            // Without cache records no core can be shown to sit outside L3, so classes stay whole
            CacheRelationshipMasks[] caches =
                TryReadLogicalProcessorInformation(RelationCache, out byte[] information)
                && TryParseCacheRelationships(information, out CacheRelationshipMasks[] parsedCaches)
                    ? parsedCaches
                    : [];
            return BuildCoreClassTopology(coreRelationships, caches);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"CPUTopologyReader.ReadCoreClassTopology: {exception}");
            return CPUCoreClassTopology.Empty;
        }
    }

    /// <summary>Returns whether CPUID identifies the current processor vendor as AMD.</summary>
    internal static bool IsAMDProcessor()
    {
        if (!X86Base.IsSupported) return false;

        (int Eax, int Ebx, int Ecx, int Edx) vendor = X86Base.CpuId(functionId: 0, subFunctionId: 0);
        return vendor is
        {
            Ebx: AuthenticAMDEBX,
            Ecx: AuthenticAMDECX,
            Edx: AuthenticAMDEDX
        };
    }

    /// <summary>Returns whether the processor advertises AMD extended CPU topology.</summary>
    internal static bool SupportsAMDExtendedCPUTopology()
    {
        if (!IsAMDProcessor()) return false;

        (int Eax, int Ebx, int Ecx, int Edx) maximumExtendedFunction =
            X86Base.CpuId(AMDExtendedFunctionMaximum, subFunctionId: 0);
        return unchecked((uint)maximumExtendedFunction.Eax) >= AMDExtendedCPUTopologyFunction;
    }

    /// <summary>Reads the AMD CPUID topology path directly for diagnostics and fallback validation.</summary>
    internal static CPUCCDTopology ReadAMDExtendedCPUTopology()
    {
        if (!SupportsAMDExtendedCPUTopology()) return CPUCCDTopology.Empty;

        try
        {
            return TryReadProcessorRelationships(
                       RelationProcessorCore,
                       out ProcessorRelationshipMasks[] coreRelationships)
                   && TryReadAMDExtendedCPUTopology(coreRelationships, out CPUCCDTopology topology)
                ? AttachL3CacheSizes(topology)
                : CPUCCDTopology.Empty;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"CPUTopologyReader.ReadAMDExtendedCPUTopology: {exception}");
            return CPUCCDTopology.Empty;
        }
    }

    /// <summary>Builds a deterministic CCD topology from exact Windows-style affinity relationships.</summary>
    internal static CPUCCDTopology BuildCCDTopology(
        IReadOnlyList<ProcessorRelationshipMasks> coreRelationships,
        IReadOnlyList<ProcessorRelationshipMasks> dieRelationships,
        CPUCCDTopologySource source)
    {
        ArgumentNullException.ThrowIfNull(coreRelationships);
        ArgumentNullException.ThrowIfNull(dieRelationships);
        if (source == CPUCCDTopologySource.None
            || coreRelationships.Count == 0
            || dieRelationships.Count == 0
            || !TryNormalizeRelationships(
                coreRelationships,
                out CPULogicalProcessorKey[] logicalProcessorKeys,
                out NormalizedProcessorRelationship[] normalizedCores)
            || !TryNormalizeRelationships(
                dieRelationships,
                logicalProcessorKeys,
                out NormalizedProcessorRelationship[] normalizedDies))
            return CPUCCDTopology.Empty;

        Dictionary<CPULogicalProcessorKey, int> dieIndexByProcessor = new();
        for (int dieIndex = 0; dieIndex < normalizedDies.Length; dieIndex++)
        {
            int[] logicalProcessorIndexes = normalizedDies[dieIndex].LogicalProcessorIndexes;
            for (int processorOffset = 0;
                 processorOffset < logicalProcessorIndexes.Length;
                 processorOffset++)
            {
                CPULogicalProcessorKey processor =
                    logicalProcessorKeys[logicalProcessorIndexes[processorOffset]];
                if (!dieIndexByProcessor.TryAdd(processor, dieIndex))
                    return CPUCCDTopology.Empty;
            }
        }

        if (dieIndexByProcessor.Count != logicalProcessorKeys.Length)
            return CPUCCDTopology.Empty;

        List<int>[] coreIndexesByDie = new List<int>[normalizedDies.Length];
        for (int dieIndex = 0; dieIndex < coreIndexesByDie.Length; dieIndex++)
            coreIndexesByDie[dieIndex] = [];

        CPUCoreTopologyEntry[] cores = new CPUCoreTopologyEntry[normalizedCores.Length];
        for (int coreIndex = 0; coreIndex < normalizedCores.Length; coreIndex++)
        {
            int[] logicalProcessorIndexes = normalizedCores[coreIndex].LogicalProcessorIndexes;
            CPULogicalProcessorKey firstProcessor = logicalProcessorKeys[logicalProcessorIndexes[0]];
            if (!dieIndexByProcessor.TryGetValue(firstProcessor, out int dieIndex))
                return CPUCCDTopology.Empty;

            for (int processorOffset = 1;
                 processorOffset < logicalProcessorIndexes.Length;
                 processorOffset++)
            {
                CPULogicalProcessorKey processor =
                    logicalProcessorKeys[logicalProcessorIndexes[processorOffset]];
                if (!dieIndexByProcessor.TryGetValue(processor, out int matchingDieIndex)
                    || matchingDieIndex != dieIndex)
                    return CPUCCDTopology.Empty;
            }

            cores[coreIndex] = new CPUCoreTopologyEntry(
                coreIndex,
                dieIndex,
                logicalProcessorIndexes);
            coreIndexesByDie[dieIndex].Add(coreIndex);
        }

        CPUCCDTopologyEntry[] ccds = new CPUCCDTopologyEntry[normalizedDies.Length];
        for (int dieIndex = 0; dieIndex < normalizedDies.Length; dieIndex++)
        {
            NormalizedProcessorRelationship die = normalizedDies[dieIndex];
            ccds[dieIndex] = new CPUCCDTopologyEntry(
                dieIndex,
                die.HardwareTopologyID,
                coreIndexesByDie[dieIndex].ToArray(),
                die.LogicalProcessorIndexes);
        }

        CPULogicalProcessor[] logicalProcessors = new CPULogicalProcessor[logicalProcessorKeys.Length];
        for (int processorIndex = 0; processorIndex < logicalProcessorKeys.Length; processorIndex++)
        {
            CPULogicalProcessorKey processor = logicalProcessorKeys[processorIndex];
            logicalProcessors[processorIndex] = new CPULogicalProcessor(
                processorIndex,
                processor.Group,
                processor.Number);
        }

        return new CPUCCDTopology(source, logicalProcessors, cores, ccds);
    }

    /// <summary>Partitions active cores by Windows efficiency class and L3 placement, highest class first.</summary>
    internal static CPUCoreClassTopology BuildCoreClassTopology(
        IReadOnlyList<ProcessorRelationshipMasks> coreRelationships,
        IReadOnlyList<CacheRelationshipMasks> caches)
    {
        ArgumentNullException.ThrowIfNull(coreRelationships);
        ArgumentNullException.ThrowIfNull(caches);
        if (coreRelationships.Count == 0
            || !TryNormalizeRelationships(
                coreRelationships,
                out CPULogicalProcessorKey[] logicalProcessorKeys,
                out NormalizedProcessorRelationship[] cores))
            return CPUCoreClassTopology.Empty;

        Dictionary<CPULogicalProcessorKey, int> processorIndexes = new();
        for (int processorIndex = 0; processorIndex < logicalProcessorKeys.Length; processorIndex++)
            processorIndexes.Add(logicalProcessorKeys[processorIndex], processorIndex);

        bool[] processorHasL3Cache = new bool[logicalProcessorKeys.Length];
        bool hasL3Cache = false;
        for (int cacheIndex = 0; cacheIndex < caches.Count; cacheIndex++)
        {
            CacheRelationshipMasks cache = caches[cacheIndex];
            if (cache.Level != L3CacheLevel
                || cache.Type != UnifiedCacheType
                || !TryExpandGroupMasks(cache.GroupMasks.Span, out CPULogicalProcessorKey[] cacheProcessors))
                continue;

            hasL3Cache = true;
            for (int processorOffset = 0; processorOffset < cacheProcessors.Length; processorOffset++)
            {
                if (processorIndexes.TryGetValue(cacheProcessors[processorOffset], out int processorIndex))
                    processorHasL3Cache[processorIndex] = true;
            }
        }

        byte highestEfficiencyClass = 0;
        for (int coreIndex = 0; coreIndex < cores.Length; coreIndex++)
            highestEfficiencyClass = Math.Max(highestEfficiencyClass, cores[coreIndex].EfficiencyClass);

        // Windows gives low power efficiency cores the efficiency class, so only their missing L3 separates them
        CoreClassMember[] members = new CoreClassMember[cores.Length];
        for (int coreIndex = 0; coreIndex < cores.Length; coreIndex++)
        {
            NormalizedProcessorRelationship core = cores[coreIndex];
            bool isOutsideL3Cache = hasL3Cache && core.EfficiencyClass != highestEfficiencyClass;
            for (int processorOffset = 0; processorOffset < core.LogicalProcessorIndexes.Length; processorOffset++)
                isOutsideL3Cache &= !processorHasL3Cache[core.LogicalProcessorIndexes[processorOffset]];

            members[coreIndex] = new CoreClassMember(
                core.EfficiencyClass,
                isOutsideL3Cache,
                core.LogicalProcessorIndexes);
        }

        // Windows documents a higher efficiency class as intrinsically higher performance, so it leads
        Array.Sort(
            members,
            static (left, right) =>
            {
                int classComparison = right.EfficiencyClass.CompareTo(left.EfficiencyClass);
                if (classComparison != 0) return classComparison;

                int placementComparison = left.IsOutsideL3Cache.CompareTo(right.IsOutsideL3Cache);
                return placementComparison != 0
                    ? placementComparison
                    : left.LogicalProcessorIndexes[0].CompareTo(right.LogicalProcessorIndexes[0]);
            });

        List<CPUCoreClassEntry> classes = [];
        int classStart = 0;
        while (classStart < members.Length)
        {
            CoreClassMember firstMember = members[classStart];
            List<int> logicalProcessorIndexes = [];
            int classEnd = classStart;
            while (classEnd < members.Length
                   && members[classEnd].EfficiencyClass == firstMember.EfficiencyClass
                   && members[classEnd].IsOutsideL3Cache == firstMember.IsOutsideL3Cache)
            {
                logicalProcessorIndexes.AddRange(members[classEnd].LogicalProcessorIndexes);
                classEnd++;
            }

            int[] sortedLogicalProcessorIndexes = logicalProcessorIndexes.ToArray();
            Array.Sort(sortedLogicalProcessorIndexes);
            classes.Add(new CPUCoreClassEntry(
                firstMember.EfficiencyClass,
                firstMember.IsOutsideL3Cache,
                classEnd - classStart,
                sortedLogicalProcessorIndexes));
            classStart = classEnd;
        }

        return new CPUCoreClassTopology(classes.ToArray());
    }

    /// <summary>Attributes each level-3 cache to the one CCD containing all of its logical processors.</summary>
    internal static CPUCCDTopology AssignL3CacheSizes(
        CPUCCDTopology topology,
        IReadOnlyList<CacheRelationshipMasks> caches)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(caches);
        if (!topology.IsAvailable) return topology;

        ReadOnlySpan<CPULogicalProcessor> logicalProcessors = topology.LogicalProcessors.Span;
        Dictionary<CPULogicalProcessorKey, int> processorIndexes = new();
        for (int processorIndex = 0; processorIndex < logicalProcessors.Length; processorIndex++)
        {
            CPULogicalProcessor processor = logicalProcessors[processorIndex];
            processorIndexes.Add(
                new CPULogicalProcessorKey(processor.Group, processor.Number),
                processor.SystemIndex);
        }

        ReadOnlySpan<CPUCCDTopologyEntry> CCDs = topology.CCDs.Span;
        int[] CCDIndexByProcessor = new int[logicalProcessors.Length];
        for (int CCDIndex = 0; CCDIndex < CCDs.Length; CCDIndex++)
        {
            ReadOnlySpan<int> CCDProcessorIndexes = CCDs[CCDIndex].LogicalProcessorIndexes.Span;
            for (int processorOffset = 0; processorOffset < CCDProcessorIndexes.Length; processorOffset++)
                CCDIndexByProcessor[CCDProcessorIndexes[processorOffset]] = CCDIndex;
        }

        ulong[] l3CacheBytesByCCD = new ulong[CCDs.Length];
        for (int cacheIndex = 0; cacheIndex < caches.Count; cacheIndex++)
        {
            CacheRelationshipMasks cache = caches[cacheIndex];
            if (cache.Level != L3CacheLevel || cache.Type != UnifiedCacheType) continue;

            if (!TryExpandGroupMasks(cache.GroupMasks.Span, out CPULogicalProcessorKey[] cacheProcessors))
                return topology;

            int cacheCCDIndex = -1;
            for (int processorOffset = 0; processorOffset < cacheProcessors.Length; processorOffset++)
            {
                if (!processorIndexes.TryGetValue(cacheProcessors[processorOffset], out int processorIndex))
                    return topology;

                // A cache shared across CCDs cannot be attributed, so no CCD reports a size
                int processorCCDIndex = CCDIndexByProcessor[processorIndex];
                if (cacheCCDIndex >= 0 && processorCCDIndex != cacheCCDIndex) return topology;

                cacheCCDIndex = processorCCDIndex;
            }

            ulong currentL3CacheBytes = l3CacheBytesByCCD[cacheCCDIndex];
            l3CacheBytesByCCD[cacheCCDIndex] = currentL3CacheBytes > ulong.MaxValue - cache.CacheSizeBytes
                ? ulong.MaxValue
                : currentL3CacheBytes + cache.CacheSizeBytes;
        }

        CPUCCDTopologyEntry[] CCDsWithCaches = new CPUCCDTopologyEntry[CCDs.Length];
        for (int CCDIndex = 0; CCDIndex < CCDs.Length; CCDIndex++)
            CCDsWithCaches[CCDIndex] = CCDs[CCDIndex] with { L3CacheBytes = l3CacheBytesByCCD[CCDIndex] };

        return topology with { CCDs = CCDsWithCaches };
    }

    /// <summary>Parses one direct GetLogicalProcessorInformationEx processor relationship buffer.</summary>
    internal static bool TryParseProcessorRelationships(
        ReadOnlySpan<byte> buffer,
        uint expectedRelationship,
        out ProcessorRelationshipMasks[] relationships)
    {
        List<ProcessorRelationshipMasks> parsedRelationships = [];
        int offset = 0;
        while (offset < buffer.Length)
        {
            if (!TrySliceEntry(
                    buffer,
                    offset,
                    expectedRelationship,
                    ProcessorGroupMasksOffset,
                    out ReadOnlySpan<byte> entry)
                || !TryParseGroupMasks(
                    entry,
                    ProcessorGroupMasksOffset,
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        entry.Slice(ProcessorGroupCountOffset, sizeof(ushort))),
                    out ProcessorGroupAffinityMask[] groupMasks))
            {
                relationships = [];
                return false;
            }

            parsedRelationships.Add(new ProcessorRelationshipMasks(
                groupMasks,
                EfficiencyClass: entry[ProcessorEfficiencyClassOffset]));
            offset += entry.Length;
        }

        relationships = parsedRelationships.ToArray();
        return relationships.Length > 0;
    }

    /// <summary>Parses one direct GetLogicalProcessorInformationEx cache relationship buffer.</summary>
    internal static bool TryParseCacheRelationships(
        ReadOnlySpan<byte> buffer,
        out CacheRelationshipMasks[] caches)
    {
        List<CacheRelationshipMasks> parsedCaches = [];
        int offset = 0;
        while (offset < buffer.Length)
        {
            if (!TrySliceEntry(
                    buffer,
                    offset,
                    RelationCache,
                    CacheGroupMasksOffset,
                    out ReadOnlySpan<byte> entry))
            {
                caches = [];
                return false;
            }

            // NOTE: GroupCount replaced reserved bytes, so older Windows builds report zero with one mask
            ushort groupCount = BinaryPrimitives.ReadUInt16LittleEndian(
                entry.Slice(CacheGroupCountOffset, sizeof(ushort)));
            if (!TryParseGroupMasks(
                    entry,
                    CacheGroupMasksOffset,
                    Math.Max(groupCount, val2: (ushort)1),
                    out ProcessorGroupAffinityMask[] groupMasks))
            {
                caches = [];
                return false;
            }

            parsedCaches.Add(new CacheRelationshipMasks(
                entry[CacheLevelOffset],
                BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(CacheTypeOffset, sizeof(uint))),
                BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(CacheSizeOffset, sizeof(uint))),
                groupMasks));
            offset += entry.Length;
        }

        caches = parsedCaches.ToArray();
        return caches.Length > 0;
    }

    /// <summary>Decodes a CCD domain ID from one AMD extended-topology CPUID level.</summary>
    internal static bool TryDecodeAMDCCDTopologyLevel(
        int eax,
        int ebx,
        int ecx,
        int edx,
        out uint hardwareTopologyID)
    {
        uint levelType = (unchecked((uint)ecx) >> TopologyLevelTypeShift)
                         & TopologyLevelTypeMask;
        uint logicalProcessorCount = unchecked((uint)ebx)
                                     & TopologyLogicalProcessorCountMask;
        if (levelType != AMDDieTopologyLevel || logicalProcessorCount == 0)
        {
            hardwareTopologyID = 0;
            return false;
        }

        int maskWidth = (int)(unchecked((uint)eax) & TopologyMaskWidthMask);
        hardwareTopologyID = unchecked((uint)edx) >> maskWidth;
        return true;
    }

    private static bool TryReadProcessorRelationships(
        uint relationship,
        out ProcessorRelationshipMasks[] relationships)
    {
        relationships = [];
        return TryReadLogicalProcessorInformation(relationship, out byte[] information)
               && TryParseProcessorRelationships(information, relationship, out relationships);
    }

    /// <summary>Adds Windows level-3 cache sizes to a CCD topology, leaving it unchanged if caches are unreadable.</summary>
    private static CPUCCDTopology AttachL3CacheSizes(CPUCCDTopology topology) =>
        TryReadLogicalProcessorInformation(RelationCache, out byte[] information)
        && TryParseCacheRelationships(information, out CacheRelationshipMasks[] caches)
            ? AssignL3CacheSizes(topology, caches)
            : topology;

    private static bool TryReadLogicalProcessorInformation(uint relationship, out byte[] information)
    {
        information = [];
        uint requiredLength = 0;
        if (GetLogicalProcessorInformationEx(relationship, IntPtr.Zero, ref requiredLength)
            || Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer
            || requiredLength < LogicalProcessorInformationHeaderSize
            || requiredLength > int.MaxValue)
            return false;

        byte[] buffer = new byte[requiredLength];
        uint returnedLength = requiredLength;
        fixed (byte* bufferPointer = buffer)
        {
            if (!GetLogicalProcessorInformationEx(relationship, (IntPtr)bufferPointer, ref returnedLength)
                || returnedLength == 0
                || returnedLength > requiredLength)
                return false;
        }

        information = returnedLength == requiredLength ? buffer : buffer[..(int)returnedLength];
        return true;
    }

    /// <summary>Slices one variable-sized relationship record after validating its header.</summary>
    private static bool TrySliceEntry(
        ReadOnlySpan<byte> buffer,
        int offset,
        uint expectedRelationship,
        int minimumEntrySize,
        out ReadOnlySpan<byte> entry)
    {
        entry = default;
        if (buffer.Length - offset < LogicalProcessorInformationHeaderSize) return false;

        ReadOnlySpan<byte> header = buffer.Slice(offset, LogicalProcessorInformationHeaderSize);
        uint relationship = BinaryPrimitives.ReadUInt32LittleEndian(header);
        uint entrySize = BinaryPrimitives.ReadUInt32LittleEndian(header[sizeof(uint)..]);
        if (relationship != expectedRelationship
            || entrySize < minimumEntrySize
            || entrySize > buffer.Length - offset)
            return false;

        entry = buffer.Slice(offset, (int)entrySize);
        return true;
    }

    private static bool TryParseGroupMasks(
        ReadOnlySpan<byte> entry,
        int groupMasksOffset,
        int groupCount,
        out ProcessorGroupAffinityMask[] groupMasks)
    {
        groupMasks = [];
        if (groupCount == 0
            || checked(groupMasksOffset + groupCount * GroupAffinitySize) > entry.Length)
            return false;

        ProcessorGroupAffinityMask[] parsedGroupMasks = new ProcessorGroupAffinityMask[groupCount];
        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            int groupOffset = groupMasksOffset + groupIndex * GroupAffinitySize;
            ulong mask = BinaryPrimitives.ReadUInt64LittleEndian(
                entry.Slice(groupOffset, sizeof(ulong)));
            ushort group = BinaryPrimitives.ReadUInt16LittleEndian(
                entry.Slice(groupOffset + sizeof(ulong), sizeof(ushort)));
            if (mask == 0) return false;

            parsedGroupMasks[groupIndex] = new ProcessorGroupAffinityMask(group, mask);
        }

        groupMasks = parsedGroupMasks;
        return true;
    }

    private static bool TryReadAMDExtendedCPUTopology(
        IReadOnlyList<ProcessorRelationshipMasks> coreRelationships,
        out CPUCCDTopology topology)
    {
        topology = CPUCCDTopology.Empty;
        if (!X86Base.IsSupported || nuint.Size != sizeof(ulong)) return false;

        if (!SupportsAMDExtendedCPUTopology()
            || !TryCollectLogicalProcessors(
                coreRelationships,
                out CPULogicalProcessorKey[] logicalProcessors))
            return false;

        uint[] hardwareTopologyIDs = new uint[logicalProcessors.Length];
        bool probeSucceeded = false;
        Exception? probeException = null;
        Thread probeThread = new(() =>
        {
            bool threadAffinityStarted = false;
            try
            {
                Thread.BeginThreadAffinity();
                threadAffinityStarted = true;
                probeSucceeded = ProbeLogicalProcessors(logicalProcessors, hardwareTopologyIDs);
            }
            catch (Exception exception)
            {
                probeException = exception;
            }
            finally
            {
                if (threadAffinityStarted)
                {
                    try
                    {
                        Thread.EndThreadAffinity();
                    }
                    catch (Exception exception)
                    {
                        probeException ??= exception;
                    }
                }
            }
        }) { IsBackground = true, Name = Constants.ApplicationName + ".CCDTopologyProbe" };
        probeThread.Start();
        probeThread.Join();
        if (probeException != null)
        {
            TADNLog.Log($"CPUTopologyReader CPUID probe: {probeException}");
            return false;
        }

        if (!probeSucceeded) return false;

        Dictionary<uint, List<CPULogicalProcessorKey>> processorsByHardwareTopologyID = new();
        for (int processorIndex = 0; processorIndex < logicalProcessors.Length; processorIndex++)
        {
            uint hardwareTopologyID = hardwareTopologyIDs[processorIndex];
            if (!processorsByHardwareTopologyID.TryGetValue(
                    hardwareTopologyID,
                    out List<CPULogicalProcessorKey>? processors))
            {
                processors = [];
                processorsByHardwareTopologyID.Add(hardwareTopologyID, processors);
            }

            processors.Add(logicalProcessors[processorIndex]);
        }

        uint[] sortedHardwareTopologyIDs = new uint[processorsByHardwareTopologyID.Count];
        processorsByHardwareTopologyID.Keys.CopyTo(sortedHardwareTopologyIDs, index: 0);
        Array.Sort(sortedHardwareTopologyIDs);
        ProcessorRelationshipMasks[] dieRelationships =
            new ProcessorRelationshipMasks[sortedHardwareTopologyIDs.Length];
        for (int dieIndex = 0; dieIndex < sortedHardwareTopologyIDs.Length; dieIndex++)
        {
            uint hardwareTopologyID = sortedHardwareTopologyIDs[dieIndex];
            dieRelationships[dieIndex] = CreateRelationship(
                processorsByHardwareTopologyID[hardwareTopologyID],
                hardwareTopologyID);
        }

        topology = BuildCCDTopology(
            coreRelationships,
            dieRelationships,
            CPUCCDTopologySource.AMDExtendedCPUTopology);
        return topology.IsAvailable;
    }

    private static bool ProbeLogicalProcessors(
        ReadOnlySpan<CPULogicalProcessorKey> logicalProcessors,
        Span<uint> hardwareTopologyIDs)
    {
        IntPtr currentThread = GetCurrentThread();
        for (int processorIndex = 0; processorIndex < logicalProcessors.Length; processorIndex++)
        {
            CPULogicalProcessorKey requestedProcessor = logicalProcessors[processorIndex];
            GROUP_AFFINITY affinity = new()
            {
                Mask = (nuint)(1UL << requestedProcessor.Number), Group = requestedProcessor.Group
            };
            if (!SetThreadGroupAffinity(currentThread, ref affinity, IntPtr.Zero)) return false;

            GetCurrentProcessorNumberEx(out PROCESSOR_NUMBER currentProcessor);
            if (currentProcessor.Group != requestedProcessor.Group
                || currentProcessor.Number != requestedProcessor.Number
                || !TryReadCurrentProcessorCCD(out uint hardwareTopologyID))
                return false;
            hardwareTopologyIDs[processorIndex] = hardwareTopologyID;
        }

        return true;
    }

    private static bool TryReadCurrentProcessorCCD(out uint hardwareTopologyID)
    {
        const int topologyFunction = unchecked((int)AMDExtendedCPUTopologyFunction);
        for (int levelIndex = 0; levelIndex < MaximumTopologyLevelCount; levelIndex++)
        {
            (int Eax, int Ebx, int Ecx, int Edx) level =
                X86Base.CpuId(topologyFunction, levelIndex);
            uint levelType = (unchecked((uint)level.Ecx) >> TopologyLevelTypeShift)
                             & TopologyLevelTypeMask;
            if (levelType == 0) break;
            if (TryDecodeAMDCCDTopologyLevel(
                    level.Eax,
                    level.Ebx,
                    level.Ecx,
                    level.Edx,
                    out hardwareTopologyID))
                return true;
        }

        hardwareTopologyID = 0;
        return false;
    }

    private static ProcessorRelationshipMasks CreateRelationship(
        IReadOnlyList<CPULogicalProcessorKey> processors,
        uint hardwareTopologyID)
    {
        Dictionary<ushort, ulong> maskByGroup = new();
        for (int processorIndex = 0; processorIndex < processors.Count; processorIndex++)
        {
            CPULogicalProcessorKey processor = processors[processorIndex];
            maskByGroup.TryGetValue(processor.Group, out ulong mask);
            maskByGroup[processor.Group] = mask | (1UL << processor.Number);
        }

        ushort[] groups = new ushort[maskByGroup.Count];
        maskByGroup.Keys.CopyTo(groups, index: 0);
        Array.Sort(groups);
        ProcessorGroupAffinityMask[] groupMasks = new ProcessorGroupAffinityMask[groups.Length];
        for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            ushort group = groups[groupIndex];
            groupMasks[groupIndex] = new ProcessorGroupAffinityMask(group, maskByGroup[group]);
        }

        return new ProcessorRelationshipMasks(groupMasks, hardwareTopologyID);
    }

    private static bool TryNormalizeRelationships(
        IReadOnlyList<ProcessorRelationshipMasks> relationships,
        out CPULogicalProcessorKey[] logicalProcessors,
        out NormalizedProcessorRelationship[] normalizedRelationships)
    {
        if (!TryCollectLogicalProcessors(relationships, out logicalProcessors))
        {
            normalizedRelationships = [];
            return false;
        }

        return TryNormalizeRelationships(
            relationships,
            logicalProcessors,
            out normalizedRelationships);
    }

    private static bool TryNormalizeRelationships(
        IReadOnlyList<ProcessorRelationshipMasks> relationships,
        ReadOnlySpan<CPULogicalProcessorKey> logicalProcessors,
        out NormalizedProcessorRelationship[] normalizedRelationships)
    {
        Dictionary<CPULogicalProcessorKey, int> processorIndexes = new();
        for (int processorIndex = 0; processorIndex < logicalProcessors.Length; processorIndex++)
            processorIndexes.Add(logicalProcessors[processorIndex], processorIndex);

        HashSet<int> assignedProcessorIndexes = [];
        normalizedRelationships = new NormalizedProcessorRelationship[relationships.Count];
        for (int relationshipIndex = 0;
             relationshipIndex < relationships.Count;
             relationshipIndex++)
        {
            ProcessorRelationshipMasks relationship = relationships[relationshipIndex];
            if (!TryExpandGroupMasks(relationship.GroupMasks.Span, out CPULogicalProcessorKey[] processors))
            {
                normalizedRelationships = [];
                return false;
            }

            int[] relationshipProcessorIndexes = new int[processors.Length];
            for (int processorOffset = 0; processorOffset < processors.Length; processorOffset++)
            {
                if (!processorIndexes.TryGetValue(processors[processorOffset], out int processorIndex)
                    || !assignedProcessorIndexes.Add(processorIndex))
                {
                    normalizedRelationships = [];
                    return false;
                }

                relationshipProcessorIndexes[processorOffset] = processorIndex;
            }

            Array.Sort(relationshipProcessorIndexes);
            normalizedRelationships[relationshipIndex] = new NormalizedProcessorRelationship(
                relationshipProcessorIndexes,
                relationship.HardwareTopologyID,
                relationship.EfficiencyClass);
        }

        if (assignedProcessorIndexes.Count != logicalProcessors.Length)
        {
            normalizedRelationships = [];
            return false;
        }

        Array.Sort(
            normalizedRelationships,
            static (left, right) =>
                left.LogicalProcessorIndexes[0].CompareTo(right.LogicalProcessorIndexes[0]));
        return true;
    }

    private static bool TryCollectLogicalProcessors(
        IReadOnlyList<ProcessorRelationshipMasks> relationships,
        out CPULogicalProcessorKey[] logicalProcessors)
    {
        HashSet<CPULogicalProcessorKey> uniqueProcessors = [];
        for (int relationshipIndex = 0;
             relationshipIndex < relationships.Count;
             relationshipIndex++)
        {
            if (!TryExpandGroupMasks(
                    relationships[relationshipIndex].GroupMasks.Span,
                    out CPULogicalProcessorKey[] processors))
            {
                logicalProcessors = [];
                return false;
            }

            for (int processorIndex = 0; processorIndex < processors.Length; processorIndex++)
            {
                if (!uniqueProcessors.Add(processors[processorIndex]))
                {
                    logicalProcessors = [];
                    return false;
                }
            }
        }

        logicalProcessors = new CPULogicalProcessorKey[uniqueProcessors.Count];
        uniqueProcessors.CopyTo(logicalProcessors);
        Array.Sort(
            logicalProcessors,
            static (left, right) =>
            {
                int groupComparison = left.Group.CompareTo(right.Group);
                return groupComparison != 0
                    ? groupComparison
                    : left.Number.CompareTo(right.Number);
            });
        return logicalProcessors.Length > 0;
    }

    private static bool TryExpandGroupMasks(
        ReadOnlySpan<ProcessorGroupAffinityMask> groupMasks,
        out CPULogicalProcessorKey[] processors)
    {
        if (groupMasks.Length == 0)
        {
            processors = [];
            return false;
        }

        List<CPULogicalProcessorKey> expandedProcessors = [];
        HashSet<ushort> seenGroups = [];
        for (int groupIndex = 0; groupIndex < groupMasks.Length; groupIndex++)
        {
            ProcessorGroupAffinityMask groupMask = groupMasks[groupIndex];
            if (groupMask.Mask == 0 || !seenGroups.Add(groupMask.Group))
            {
                processors = [];
                return false;
            }

            ulong remainingMask = groupMask.Mask;
            while (remainingMask != 0)
            {
                int processorNumber = BitOperations.TrailingZeroCount(remainingMask);
                expandedProcessors.Add(new CPULogicalProcessorKey(
                    groupMask.Group,
                    checked((byte)processorNumber)));
                remainingMask &= remainingMask - 1;
            }
        }

        processors = expandedProcessors.ToArray();
        Array.Sort(
            processors,
            static (left, right) =>
            {
                int groupComparison = left.Group.CompareTo(right.Group);
                return groupComparison != 0
                    ? groupComparison
                    : left.Number.CompareTo(right.Number);
            });
        return processors.Length > 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(
        uint relationshipType,
        IntPtr buffer,
        ref uint returnedLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadGroupAffinity(
        IntPtr thread,
        ref GROUP_AFFINITY groupAffinity,
        IntPtr previousGroupAffinity);

    [DllImport("kernel32.dll")]
    private static extern void GetCurrentProcessorNumberEx(out PROCESSOR_NUMBER processorNumber);

    [StructLayout(LayoutKind.Sequential)]
    private struct GROUP_AFFINITY
    {
        public nuint Mask;
        public ushort Group;
        public ushort Reserved0;
        public ushort Reserved1;
        public ushort Reserved2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSOR_NUMBER
    {
        public ushort Group;
        public byte Number;
        public byte Reserved;
    }

    private readonly record struct CPULogicalProcessorKey(ushort Group, byte Number);

    private sealed record NormalizedProcessorRelationship(
        int[] LogicalProcessorIndexes,
        uint? HardwareTopologyID,
        byte EfficiencyClass);

    private sealed record CoreClassMember(
        byte EfficiencyClass,
        bool IsOutsideL3Cache,
        int[] LogicalProcessorIndexes);
}

/// <summary>One processor-group affinity mask from a Windows topology relationship.</summary>
internal readonly record struct ProcessorGroupAffinityMask(ushort Group, ulong Mask);

/// <summary>Affinity masks, optional hardware ID, and Windows efficiency class for one processor relationship.</summary>
internal sealed record ProcessorRelationshipMasks(
    ReadOnlyMemory<ProcessorGroupAffinityMask> GroupMasks,
    uint? HardwareTopologyID = null,
    byte EfficiencyClass = 0);

/// <summary>Level, Windows cache type, size, and affinity masks for one cache relationship.</summary>
internal sealed record CacheRelationshipMasks(
    byte Level,
    uint Type,
    uint CacheSizeBytes,
    ReadOnlyMemory<ProcessorGroupAffinityMask> GroupMasks);
