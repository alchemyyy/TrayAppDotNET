using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformancePageTests
{
    private const ulong BytesPerMebibyte = 1_048_576;

    [Fact]
    public void CPUOverallHistoriesKeepAverageAndHighestCoreSamplesPaired()
    {
        PerformanceHistory utilizationHistory = new();
        PerformanceHistory highestCoreHistory = new();
        CPUPerformanceSnapshot snapshot = CPUPerformanceSnapshot.Empty with
        {
            HasUtilizationSample = true, UtilizationPercent = 37.5, HighestLogicalProcessorPercent = 82.25
        };

        PerformancePage.AppendCPUOverallHistories(
            utilizationHistory,
            highestCoreHistory,
            snapshot,
            capturedTimestamp: 123);

        Assert.Equal(expected: 1, utilizationHistory.Count);
        Assert.Equal(expected: 1, highestCoreHistory.Count);
        Assert.Equal(expected: 123, utilizationHistory.GetTimestampChronological(0));
        Assert.Equal(expected: 123, highestCoreHistory.GetTimestampChronological(0));
        Assert.Equal(expected: 37.5, utilizationHistory.GetChronological(0));
        Assert.Equal(expected: 82.25, highestCoreHistory.GetChronological(0));
    }

    [Fact]
    public void CPUOverallHistoriesAdvanceTogetherWhenUtilizationIsUnavailable()
    {
        PerformanceHistory utilizationHistory = new();
        PerformanceHistory highestCoreHistory = new();

        PerformancePage.AppendCPUOverallHistories(
            utilizationHistory,
            highestCoreHistory,
            CPUPerformanceSnapshot.Empty,
            capturedTimestamp: 456);

        Assert.Equal(expected: 456, utilizationHistory.CurrentTimestamp);
        Assert.Equal(expected: 456, highestCoreHistory.CurrentTimestamp);
        Assert.Equal(expected: 0, utilizationHistory.Count);
        Assert.Equal(expected: 0, highestCoreHistory.Count);
    }

    [Fact]
    public void CPUOverallHoverMetricShowsHighestThenOverall()
    {
        string metric =
            PerformancePage.FormatCPUOverallHoverMetric(highestCPUUtilizationPercent: 82,
                overallUtilizationPercent: 37);

        Assert.Equal(expected: "Highest LP: 82%\nOverall util: 37%", metric);
    }

    [Fact]
    public void CPUDetailedViewAveragesLogicalProcessorsWithinEachGroup()
    {
        const long CapturedTimestamp = 789;
        List<CPUDetailedGraphGroup> groups = CPUPerformanceDetailedView.CreateGraphGroups(
            CreateCCDTopology([0, 1], [2, 3]),
            CPUCoreClassTopology.Empty);
        PerformanceHistory[] histories = [new(), new()];
        CPUPerformanceSnapshot snapshot = CPUPerformanceSnapshot.Empty with
        {
            HasUtilizationSample = true, LogicalProcessorUtilizationPercents = new double[] { 10, 30, 50, 70 }
        };

        CPUPerformanceDetailedView.AppendGroupHistories(
            histories,
            groups,
            snapshot,
            CapturedTimestamp);

        Assert.Equal(expected: 20, histories[0].GetChronological(0));
        Assert.Equal(expected: 60, histories[1].GetChronological(0));
        Assert.Equal(CapturedTimestamp, histories[0].GetTimestampChronological(0));
        Assert.Equal(CapturedTimestamp, histories[1].GetTimestampChronological(0));
    }

    [Fact]
    public void CPUDetailedViewRejectsIncompleteLogicalProcessorSamples()
    {
        const long CapturedTimestamp = 987;
        List<CPUDetailedGraphGroup> groups = CPUPerformanceDetailedView.CreateGraphGroups(
            CreateCCDTopology([0, 1], [2, 3]),
            CPUCoreClassTopology.Empty);
        PerformanceHistory[] histories = [new(), new()];
        CPUPerformanceSnapshot snapshot = CPUPerformanceSnapshot.Empty with
        {
            HasUtilizationSample = true, LogicalProcessorUtilizationPercents = new double[] { 10, 20, 30 }
        };

        CPUPerformanceDetailedView.AppendGroupHistories(
            histories,
            groups,
            snapshot,
            CapturedTimestamp);

        Assert.All(histories, history => Assert.Equal(CapturedTimestamp, history.CurrentTimestamp));
        Assert.All(histories, static history => Assert.Equal(expected: 0, history.Count));
    }

    [Fact]
    public void CPUDetailedViewOmitsTheOnlyCCDGraph()
    {
        List<CPUDetailedGraphGroup> singleCCDGroups = CPUPerformanceDetailedView.CreateGraphGroups(
            CreateCCDTopology([0, 1]),
            CPUCoreClassTopology.Empty);
        List<CPUDetailedGraphGroup> multipleCCDGroups = CPUPerformanceDetailedView.CreateGraphGroups(
            CreateCCDTopology([0, 1], [2, 3]),
            CPUCoreClassTopology.Empty);

        Assert.Empty(singleCCDGroups);
        Assert.Equal(["CCD 0", "CCD 1"], multipleCCDGroups.Select(static group => group.Label));
    }

    [Fact]
    public void CPUDetailedViewKeepsPlainCCDLabelsWhenL3SizesMatch()
    {
        CPUCCDTopology topology = WithL3CacheSizes(
            CreateCCDTopology([0, 1], [2, 3]),
            32 * BytesPerMebibyte,
            32 * BytesPerMebibyte);

        List<CPUDetailedGraphGroup> groups = CPUPerformanceDetailedView.CreateGraphGroups(
            topology,
            CPUCoreClassTopology.Empty);

        Assert.Equal(["CCD 0", "CCD 1"], groups.Select(static group => group.Label));
    }

    [Fact]
    public void CPUDetailedViewLabelsTheLargerL3CCDAsX3D()
    {
        CPUCCDTopology topology = WithL3CacheSizes(
            CreateCCDTopology([0, 1], [2, 3]),
            96 * BytesPerMebibyte,
            32 * BytesPerMebibyte);

        List<CPUDetailedGraphGroup> groups = CPUPerformanceDetailedView.CreateGraphGroups(
            topology,
            CPUCoreClassTopology.Empty);

        Assert.Equal(
            [
                string.Concat(
                    str0: "CCD 0 (X3D, ",
                    PerformanceDevicePresentationFactory.FormatBytes(96 * BytesPerMebibyte),
                    str2: " L3)"),
                string.Concat(
                    str0: "CCD 1 (",
                    PerformanceDevicePresentationFactory.FormatBytes(32 * BytesPerMebibyte),
                    str2: " L3)")
            ],
            groups.Select(static group => group.Label));
    }

    [Fact]
    public void CPUDetailedViewSkipsX3DWhenAnyCCDLacksAnL3Size()
    {
        CPUCCDTopology topology = WithL3CacheSizes(
            CreateCCDTopology([0, 1], [2, 3]),
            96 * BytesPerMebibyte,
            0);

        List<CPUDetailedGraphGroup> groups = CPUPerformanceDetailedView.CreateGraphGroups(
            topology,
            CPUCoreClassTopology.Empty);

        Assert.Equal(["CCD 0", "CCD 1"], groups.Select(static group => group.Label));
    }

    [Fact]
    public void CPUDetailedViewAddsCoreClassGraphsAfterCCDs()
    {
        CPUCoreClassTopology coreClasses = new(new CPUCoreClassEntry[]
        {
            new(EfficiencyClass: 1, IsOutsideL3Cache: false, CoreCount: 1, new[] { 0, 1 }),
            new(EfficiencyClass: 0, IsOutsideL3Cache: false, CoreCount: 2, new[] { 2, 3 })
        });

        List<CPUDetailedGraphGroup> groups = CPUPerformanceDetailedView.CreateGraphGroups(
            CreateCCDTopology([0, 1], [2, 3]),
            coreClasses);

        Assert.Equal(
            ["CCD 0", "CCD 1", "1 Performance Core", "2 Efficiency Cores"],
            groups.Select(static group => group.Label));
        Assert.Equal([2, 3], groups[3].LogicalProcessorIndexes.ToArray());
    }

    [Fact]
    public void CPUDetailedViewNamesLowerClassCoresOutsideL3AsLowPowerEfficiencyCores()
    {
        CPUCoreClassTopology coreClasses = new(new CPUCoreClassEntry[]
        {
            new(EfficiencyClass: 1, IsOutsideL3Cache: false, CoreCount: 6, new[] { 0, 1 }),
            new(EfficiencyClass: 0, IsOutsideL3Cache: false, CoreCount: 8, new[] { 2 }),
            new(EfficiencyClass: 0, IsOutsideL3Cache: true, CoreCount: 2, new[] { 3 })
        });

        List<CPUDetailedGraphGroup> groups = CPUPerformanceDetailedView.CreateGraphGroups(
            CPUCCDTopology.Empty,
            coreClasses);

        Assert.Equal(
            ["6 Performance Cores", "8 Efficiency Cores", "2 Low Power Efficiency Cores"],
            groups.Select(static group => group.Label));
    }

    [Fact]
    public void CPUDetailedViewShowsTheWindowsClassWhenMoreThanTwoClassesExist()
    {
        CPUCoreClassTopology coreClasses = new(new CPUCoreClassEntry[]
        {
            new(EfficiencyClass: 2, IsOutsideL3Cache: false, CoreCount: 4, new[] { 0 }),
            new(EfficiencyClass: 1, IsOutsideL3Cache: false, CoreCount: 4, new[] { 1 }),
            new(EfficiencyClass: 0, IsOutsideL3Cache: false, CoreCount: 4, new[] { 2 })
        });

        List<CPUDetailedGraphGroup> groups = CPUPerformanceDetailedView.CreateGraphGroups(
            CPUCCDTopology.Empty,
            coreClasses);

        Assert.Equal(
            ["4 Performance Cores", "4 Efficiency Cores (class 1)", "4 Efficiency Cores (class 0)"],
            groups.Select(static group => group.Label));
    }

    [Fact]
    public void CPUDetailedViewOmitsCoreClassGraphsForOneClass()
    {
        CPUCoreClassTopology coreClasses = new(new CPUCoreClassEntry[]
        {
            new(EfficiencyClass: 0, IsOutsideL3Cache: false, CoreCount: 2, new[] { 0, 1, 2, 3 })
        });

        List<CPUDetailedGraphGroup> groups = CPUPerformanceDetailedView.CreateGraphGroups(
            CPUCCDTopology.Empty,
            coreClasses);

        Assert.Empty(groups);
    }

#if DEBUG
    [Fact]
    public void CPUArchitectureSimulationsProduceTheExpectedGraphs()
    {
        Dictionary<string, string[]> labelsByButton = CPUArchitectureSimulation.All.ToDictionary(
            static simulation => simulation.ButtonText,
            static simulation => simulation.CreateGraphGroups(liveLogicalProcessorCount: 0)
                .Select(static group => group.Label)
                .ToArray());

        Assert.Equal(["8 Performance Cores", "16 Efficiency Cores"], labelsByButton["Intel hybrid"]);
        Assert.Equal(
            ["6 Performance Cores", "8 Efficiency Cores", "2 Low Power Efficiency Cores"],
            labelsByButton["Intel 3-tier hybrid"]);
        Assert.Equal(["4 Performance Cores", "8 Efficiency Cores"], labelsByButton["Ryzen hybrid"]);
        Assert.Equal(
            [
                string.Concat(
                    str0: "CCD 0 (X3D, ",
                    PerformanceDevicePresentationFactory.FormatBytes(96 * BytesPerMebibyte),
                    str2: " L3)"),
                string.Concat(
                    str0: "CCD 1 (",
                    PerformanceDevicePresentationFactory.FormatBytes(32 * BytesPerMebibyte),
                    str2: " L3)")
            ],
            labelsByButton["Ryzen X3D"]);
    }

    [Fact]
    public void CPUArchitectureSimulationWrapsOntoLiveLogicalProcessors()
    {
        CPUArchitectureSimulation simulation = CPUArchitectureSimulation.All.Single(
            static candidate => candidate.ButtonText == "Ryzen X3D");

        List<CPUDetailedGraphGroup> groups = simulation.CreateGraphGroups(liveLogicalProcessorCount: 12);

        Assert.All(
            groups,
            static group => Assert.All(
                group.LogicalProcessorIndexes.ToArray(),
                static processorIndex => Assert.InRange(processorIndex, low: 0, high: 11)));
    }
#endif

    [Fact]
    public void NetworkHoverMetricShowsSendThenReceive()
    {
        string metric =
            PerformancePage.FormatNetworkTransferHoverMetric(sendBytesPerSecond: 100, receiveBytesPerSecond: 200);

        Assert.Equal(expected: "Send: 100 B/s\nReceive: 200 B/s", metric);
    }

    [Fact]
    public void NetworkDeviceColumnHoverMetricUsesCompactLabels()
    {
        string metric =
            PerformancePage.FormatNetworkDeviceColumnHoverMetric(sendBytesPerSecond: 100, receiveBytesPerSecond: 200);

        Assert.Equal(expected: "S: 100 B/s\nR: 200 B/s", metric);
    }

    [Fact]
    public void DiskHoverMetricShowsReadThenWriteWithCompactLabels()
    {
        string metric =
            PerformancePage.FormatDiskTransferHoverMetric(readBytesPerSecond: 100, writeBytesPerSecond: 200);

        Assert.Equal(expected: "R: 100 B/s\nW: 200 B/s", metric);
    }

    [Fact]
    public void DiskTransferRateHistoriesKeepReadAndWriteSamplesPaired()
    {
        const long CapturedTimestamp = 123;
        PerformanceMetricHistory readHistory = new(historyLengthMinutes: 1, sampleIntervalMilliseconds: 1_000);
        PerformanceMetricHistory writeHistory = new(historyLengthMinutes: 1, sampleIntervalMilliseconds: 1_000);
        DiskPerformanceSnapshot snapshot = CreateDiskSnapshot(true);

        PerformancePage.AppendDiskTransferRateHistories(
            readHistory,
            writeHistory,
            snapshot,
            CapturedTimestamp);

        Assert.True(readHistory.TryGetExact(CapturedTimestamp, out double readBytesPerSecond));
        Assert.True(writeHistory.TryGetExact(CapturedTimestamp, out double writeBytesPerSecond));
        Assert.Equal(expected: 100, readBytesPerSecond);
        Assert.Equal(expected: 200, writeBytesPerSecond);
    }

    [Fact]
    public void DiskTransferRateHistoriesAdvanceTogetherWhenSampleIsUnavailable()
    {
        const long CapturedTimestamp = 456;
        PerformanceMetricHistory readHistory = new(historyLengthMinutes: 1, sampleIntervalMilliseconds: 1_000);
        PerformanceMetricHistory writeHistory = new(historyLengthMinutes: 1, sampleIntervalMilliseconds: 1_000);
        DiskPerformanceSnapshot snapshot = CreateDiskSnapshot(false);

        PerformancePage.AppendDiskTransferRateHistories(
            readHistory,
            writeHistory,
            snapshot,
            CapturedTimestamp);

        Assert.Equal(CapturedTimestamp, readHistory.CurrentTimestamp);
        Assert.Equal(CapturedTimestamp, writeHistory.CurrentTimestamp);
        Assert.Equal(expected: 0, readHistory.Count);
        Assert.Equal(expected: 0, writeHistory.Count);
    }

    [Fact]
    public void MemoryDeviceColumnHoverMetricUsesGigabytesWithCompactSuffix()
    {
        const double Gibibyte = 1_073_741_824;

        string metric = PerformancePage.FormatMemoryDeviceColumnHoverMetric(4.5 * Gibibyte);

        Assert.Equal(expected: "4.5 G", metric);
    }

    private static DiskPerformanceSnapshot CreateDiskSnapshot(bool hasPerformanceSample) => new(
        DeviceID: "disk:test",
        PerformanceDeviceKind.Disk,
        SortKey: 0,
        Name: "Test disk",
        VolumeNames: "C:",
        DeviceType: "SSD",
        hasPerformanceSample,
        ActiveTimePercent: 25,
        ReadBytesPerSecond: 100,
        WriteBytesPerSecond: 200,
        AverageResponseTimeMilliseconds: 1,
        QueueDepth: 0,
        CapacityBytes: 0,
        FormattedCapacityBytes: 0,
        AvailableBytes: 0);

    private static CPUCCDTopology CreateCCDTopology(params int[][] processorIndexesByCCD)
    {
        CPUCCDTopologyEntry[] CCDs = new CPUCCDTopologyEntry[processorIndexesByCCD.Length];
        List<CPULogicalProcessor> logicalProcessors = [];
        List<CPUCoreTopologyEntry> cores = [];
        for (int CCDIndex = 0; CCDIndex < processorIndexesByCCD.Length; CCDIndex++)
        {
            int[] processorIndexes = processorIndexesByCCD[CCDIndex];
            int[] coreIndexes = new int[processorIndexes.Length];
            for (int processorOffset = 0;
                 processorOffset < processorIndexes.Length;
                 processorOffset++)
            {
                int processorIndex = processorIndexes[processorOffset];
                logicalProcessors.Add(new CPULogicalProcessor(
                    processorIndex,
                    Group: 0,
                    checked((byte)processorIndex)));
                coreIndexes[processorOffset] = cores.Count;
                cores.Add(new CPUCoreTopologyEntry(
                    cores.Count,
                    CCDIndex,
                    new[] { processorIndex }));
            }

            CCDs[CCDIndex] = new CPUCCDTopologyEntry(
                CCDIndex,
                HardwareTopologyID: null,
                coreIndexes,
                processorIndexes);
        }

        logicalProcessors.Sort(static (left, right) => left.SystemIndex.CompareTo(right.SystemIndex));
        return new CPUCCDTopology(
            CPUCCDTopologySource.WindowsProcessorDie,
            logicalProcessors.ToArray(),
            cores.ToArray(),
            CCDs);
    }

    private static CPUCCDTopology WithL3CacheSizes(
        CPUCCDTopology topology,
        params ulong[] l3CacheBytesByCCD)
    {
        CPUCCDTopologyEntry[] CCDs = topology.CCDs.ToArray();
        for (int CCDIndex = 0; CCDIndex < CCDs.Length; CCDIndex++)
            CCDs[CCDIndex] = CCDs[CCDIndex] with { L3CacheBytes = l3CacheBytesByCCD[CCDIndex] };

        return topology with { CCDs = CCDs };
    }
}
