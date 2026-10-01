#if DEBUG
using TaskManagerTrayAppDotNET.Services;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>A Debug-only processor whose simulated Windows topology records replace the live ones.</summary>
internal sealed record CPUArchitectureSimulation(
    string ButtonText,
    string ProcessorName,
    CPUCCDTopology CCDTopology,
    CPUCoreClassTopology CoreClassTopology)
{
    private const uint BytesPerMebibyte = 1_048_576;

    /// <summary>Gets one simulated processor for each topology the Detailed view distinguishes.</summary>
    public static IReadOnlyList<CPUArchitectureSimulation> All { get; } =
    [
        CreateIntelHybrid(),
        CreateIntelLowPowerHybrid(),
        CreateAMDHybrid(),
        CreateAMDX3D()
    ];

    /// <summary>Builds Detailed view groups whose simulated logical processors read live samples.</summary>
    public List<CPUDetailedGraphGroup> CreateGraphGroups(int liveLogicalProcessorCount)
    {
        List<CPUDetailedGraphGroup> groups = CPUPerformanceDetailedView.CreateGraphGroups(
            CCDTopology,
            CoreClassTopology);
        if (liveLogicalProcessorCount <= 0) return groups;

        // Wrap onto the live processors so a simulation larger than this machine still animates
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            ReadOnlySpan<int> simulatedIndexes = groups[groupIndex].LogicalProcessorIndexes.Span;
            int[] liveIndexes = new int[simulatedIndexes.Length];
            for (int processorOffset = 0; processorOffset < simulatedIndexes.Length; processorOffset++)
                liveIndexes[processorOffset] = simulatedIndexes[processorOffset] % liveLogicalProcessorCount;

            groups[groupIndex] = groups[groupIndex] with { LogicalProcessorIndexes = liveIndexes };
        }

        return groups;
    }

    private static CPUArchitectureSimulation CreateIntelHybrid()
    {
        // Two-thread performance cores are listed first, and every core shares the ring's L3
        SimulatedTopologyBuilder builder = new();
        builder.AddCores(coreCount: 8, threadsPerCore: 2, efficiencyClass: 1);
        builder.AddCores(coreCount: 16, threadsPerCore: 1, efficiencyClass: 0);
        builder.AddL3Cache(firstProcessor: 0, builder.ProcessorCount, mebibytes: 36);
        return new CPUArchitectureSimulation(
            ButtonText: "Intel hybrid",
            ProcessorName: "Intel Core i9-14900K",
            CPUCCDTopology.Empty,
            builder.BuildCoreClassTopology());
    }

    private static CPUArchitectureSimulation CreateIntelLowPowerHybrid()
    {
        // Windows gives both efficiency core kinds one class; only the low power ones sit outside the L3
        SimulatedTopologyBuilder builder = new();
        builder.AddCores(coreCount: 6, threadsPerCore: 2, efficiencyClass: 1);
        builder.AddCores(coreCount: 8, threadsPerCore: 1, efficiencyClass: 0);
        int computeTileProcessorCount = builder.ProcessorCount;
        builder.AddCores(coreCount: 2, threadsPerCore: 1, efficiencyClass: 0);
        builder.AddL3Cache(firstProcessor: 0, computeTileProcessorCount, mebibytes: 24);
        return new CPUArchitectureSimulation(
            ButtonText: "Intel 3-tier hybrid",
            ProcessorName: "Intel Core Ultra 7 155H",
            CPUCCDTopology.Empty,
            builder.BuildCoreClassTopology());
    }

    private static CPUArchitectureSimulation CreateAMDHybrid()
    {
        // One die holds a Zen 5 CCX with 16 MB of L3 and a Zen 5c CCX with 8 MB
        SimulatedTopologyBuilder builder = new();
        int performanceStart = builder.AddCores(coreCount: 4, threadsPerCore: 2, efficiencyClass: 1);
        int efficiencyStart = builder.AddCores(coreCount: 8, threadsPerCore: 2, efficiencyClass: 0);
        builder.AddDie(firstProcessor: 0, builder.ProcessorCount);
        builder.AddL3Cache(performanceStart, processorCount: 8, mebibytes: 16);
        builder.AddL3Cache(efficiencyStart, processorCount: 16, mebibytes: 8);
        return new CPUArchitectureSimulation(
            ButtonText: "Ryzen hybrid",
            ProcessorName: "AMD Ryzen AI 9 HX 370",
            builder.BuildCCDTopology(),
            builder.BuildCoreClassTopology());
    }

    private static CPUArchitectureSimulation CreateAMDX3D()
    {
        // Only the first CCD carries stacked cache, so Windows reports 96 MB and 32 MB L3 domains
        SimulatedTopologyBuilder builder = new();
        int cacheCCDStart = builder.AddCores(coreCount: 8, threadsPerCore: 2, efficiencyClass: 0);
        int frequencyCCDStart = builder.AddCores(coreCount: 8, threadsPerCore: 2, efficiencyClass: 0);
        builder.AddDie(cacheCCDStart, processorCount: 16);
        builder.AddDie(frequencyCCDStart, processorCount: 16);
        builder.AddL3Cache(cacheCCDStart, processorCount: 16, mebibytes: 96);
        builder.AddL3Cache(frequencyCCDStart, processorCount: 16, mebibytes: 32);
        return new CPUArchitectureSimulation(
            ButtonText: "Ryzen X3D",
            ProcessorName: "AMD Ryzen 9 9950X3D",
            builder.BuildCCDTopology(),
            builder.BuildCoreClassTopology());
    }

    /// <summary>Emits Windows-shaped core, die, and cache relationships for processor group 0.</summary>
    private sealed class SimulatedTopologyBuilder
    {
        private readonly List<ProcessorRelationshipMasks> _cores = [];
        private readonly List<ProcessorRelationshipMasks> _dies = [];
        private readonly List<CacheRelationshipMasks> _caches = [];

        public int ProcessorCount { get; private set; }

        /// <summary>Appends cores after the existing ones and returns the first new logical processor.</summary>
        public int AddCores(int coreCount, int threadsPerCore, byte efficiencyClass)
        {
            int firstProcessor = ProcessorCount;
            for (int coreIndex = 0; coreIndex < coreCount; coreIndex++)
            {
                _cores.Add(new ProcessorRelationshipMasks(
                    CreateGroupMasks(ProcessorCount, threadsPerCore),
                    EfficiencyClass: efficiencyClass));
                ProcessorCount += threadsPerCore;
            }

            return firstProcessor;
        }

        public void AddDie(int firstProcessor, int processorCount) =>
            _dies.Add(new ProcessorRelationshipMasks(CreateGroupMasks(firstProcessor, processorCount)));

        public void AddL3Cache(int firstProcessor, int processorCount, uint mebibytes) =>
            _caches.Add(new CacheRelationshipMasks(
                Level: 3,
                Type: 0,
                mebibytes * BytesPerMebibyte,
                CreateGroupMasks(firstProcessor, processorCount)));

        public CPUCCDTopology BuildCCDTopology() =>
            CPUTopologyReader.AssignL3CacheSizes(
                CPUTopologyReader.BuildCCDTopology(
                    _cores,
                    _dies,
                    CPUCCDTopologySource.WindowsProcessorDie),
                _caches);

        public CPUCoreClassTopology BuildCoreClassTopology() =>
            CPUTopologyReader.BuildCoreClassTopology(_cores, _caches);

        private static ProcessorGroupAffinityMask[] CreateGroupMasks(int firstProcessor, int processorCount)
        {
            // NOTE: a 64-bit shift count wraps to zero, so a full group needs its own mask
            ulong processorBits = processorCount >= sizeof(ulong) * 8
                ? ulong.MaxValue
                : (1UL << processorCount) - 1;
            return [new ProcessorGroupAffinityMask(Group: 0, processorBits << firstProcessor)];
        }
    }
}
#endif
