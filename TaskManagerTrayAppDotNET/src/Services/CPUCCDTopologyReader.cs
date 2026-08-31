using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Reads exact AMD core-to-CCD membership without requiring a hardware driver.</summary>
internal static unsafe class CPUCCDTopologyReader
{
    private const uint RelationProcessorCore = 0;
    private const uint RelationProcessorDie = 5;
    private const int ErrorInsufficientBuffer = 122;
    private const int LogicalProcessorInformationHeaderSize = 8;
    private const int ProcessorGroupCountOffset = 30;
    private const int ProcessorGroupMasksOffset = 32;
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
    public static CPUCCDTopology Read()
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
                CPUCCDTopology windowsTopology = BuildTopology(
                    coreRelationships,
                    dieRelationships,
                    CPUCCDTopologySource.WindowsProcessorDie);
                if (windowsTopology.IsAvailable) return windowsTopology;
            }

            return TryReadAMDExtendedCPUTopology(
                coreRelationships,
                out CPUCCDTopology amdTopology)
                ? amdTopology
                : CPUCCDTopology.Empty;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"CPUCCDTopologyReader.Read: {exception}");
            return CPUCCDTopology.Empty;
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
                ? topology
                : CPUCCDTopology.Empty;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"CPUCCDTopologyReader.ReadAMDExtendedCPUTopology: {exception}");
            return CPUCCDTopology.Empty;
        }
    }

    /// <summary>Builds a deterministic topology from exact Windows-style affinity relationships.</summary>
    internal static CPUCCDTopology BuildTopology(
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

    /// <summary>Parses one direct GetLogicalProcessorInformationEx relationship buffer.</summary>
    internal static bool TryParseProcessorRelationships(
        ReadOnlySpan<byte> buffer,
        uint expectedRelationship,
        out ProcessorRelationshipMasks[] relationships)
    {
        List<ProcessorRelationshipMasks> parsedRelationships = [];
        int offset = 0;
        while (offset < buffer.Length)
        {
            if (buffer.Length - offset < LogicalProcessorInformationHeaderSize)
            {
                relationships = [];
                return false;
            }

            ReadOnlySpan<byte> header = buffer.Slice(offset, LogicalProcessorInformationHeaderSize);
            uint relationship = BinaryPrimitives.ReadUInt32LittleEndian(header);
            uint entrySizeValue = BinaryPrimitives.ReadUInt32LittleEndian(header[sizeof(uint)..]);
            if (relationship != expectedRelationship
                || entrySizeValue < ProcessorGroupMasksOffset
                || entrySizeValue > int.MaxValue)
            {
                relationships = [];
                return false;
            }

            int entrySize = (int)entrySizeValue;
            if (entrySize > buffer.Length - offset)
            {
                relationships = [];
                return false;
            }

            ReadOnlySpan<byte> entry = buffer.Slice(offset, entrySize);
            ushort groupCount = BinaryPrimitives.ReadUInt16LittleEndian(
                entry.Slice(ProcessorGroupCountOffset, sizeof(ushort)));
            int requiredEntrySize = checked(ProcessorGroupMasksOffset + groupCount * GroupAffinitySize);
            if (groupCount == 0 || requiredEntrySize > entrySize)
            {
                relationships = [];
                return false;
            }

            ProcessorGroupAffinityMask[] groupMasks = new ProcessorGroupAffinityMask[groupCount];
            for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                int groupOffset = ProcessorGroupMasksOffset + groupIndex * GroupAffinitySize;
                ulong mask = BinaryPrimitives.ReadUInt64LittleEndian(
                    entry.Slice(groupOffset, sizeof(ulong)));
                ushort group = BinaryPrimitives.ReadUInt16LittleEndian(
                    entry.Slice(groupOffset + sizeof(ulong), sizeof(ushort)));
                if (mask == 0)
                {
                    relationships = [];
                    return false;
                }

                groupMasks[groupIndex] = new ProcessorGroupAffinityMask(group, mask);
            }

            parsedRelationships.Add(new ProcessorRelationshipMasks(groupMasks));
            offset += entrySize;
        }

        relationships = parsedRelationships.ToArray();
        return relationships.Length > 0;
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
        uint requiredLength = 0;
        if (GetLogicalProcessorInformationEx(relationship, IntPtr.Zero, ref requiredLength)
            || Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer
            || requiredLength < ProcessorGroupMasksOffset
            || requiredLength > int.MaxValue)
            return false;

        IntPtr buffer = Marshal.AllocHGlobal((int)requiredLength);
        try
        {
            uint returnedLength = requiredLength;
            if (!GetLogicalProcessorInformationEx(relationship, buffer, ref returnedLength)
                || returnedLength == 0
                || returnedLength > requiredLength)
                return false;

            ReadOnlySpan<byte> bytes = new((void*)buffer, (int)returnedLength);
            return TryParseProcessorRelationships(bytes, relationship, out relationships);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
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
            TADNLog.Log($"CPUCCDTopologyReader CPUID probe: {probeException}");
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

        topology = BuildTopology(
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
            if (!TryExpandRelationship(relationship, out CPULogicalProcessorKey[] processors))
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
                relationship.HardwareTopologyID);
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
            if (!TryExpandRelationship(
                    relationships[relationshipIndex],
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

    private static bool TryExpandRelationship(
        ProcessorRelationshipMasks relationship,
        out CPULogicalProcessorKey[] processors)
    {
        ReadOnlySpan<ProcessorGroupAffinityMask> groupMasks = relationship.GroupMasks.Span;
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
        uint? HardwareTopologyID);
}

/// <summary>One processor-group affinity mask from a Windows topology relationship.</summary>
internal readonly record struct ProcessorGroupAffinityMask(ushort Group, ulong Mask);

/// <summary>Affinity masks and optional hardware ID for one processor relationship.</summary>
internal sealed record ProcessorRelationshipMasks(
    ReadOnlyMemory<ProcessorGroupAffinityMask> GroupMasks,
    uint? HardwareTopologyID = null);
