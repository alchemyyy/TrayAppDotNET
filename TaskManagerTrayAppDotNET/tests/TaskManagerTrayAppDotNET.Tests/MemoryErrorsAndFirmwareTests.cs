using System.Buffers.Binary;
using System.Globalization;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerTrayAppDotNET.UI.Performance;
using TrayAppDotNETCommon.Serialization;
using static TaskManagerTrayAppDotNET.Tests.TestLocalization;
using AppStrings = TaskManagerTrayAppDotNET.Localization.Strings;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class MemoryErrorsAndFirmwareTests
{
    private const byte AccessLatency = 0;
    private const byte ReadLatency = 1;
    private const byte AccessBandwidth = 3;

    [Fact]
    public void HMATRevision2ReadsPicosecondLatencyAndMegabyteBandwidth()
    {
        // The table an AMD Threadripper 9960X publishes: one initiator, one target
        byte[] table = BuildHMAT(
            revision: 2,
            Locality(hierarchy: 0, AccessLatency, baseUnit: 1_000, [110]),
            Locality(hierarchy: 0, AccessBandwidth, baseUnit: 100, [1_080]));

        MemoryFirmwarePerformance performance = MemoryFirmwarePerformanceReader.Parse(table);

        Assert.Equal(new MemoryFirmwareMetricRange(110_000, 110_000), performance.AccessLatencyPicoseconds);
        Assert.Equal(new MemoryFirmwareMetricRange(108_000, 108_000), performance.AccessBandwidthMegabytesPerSecond);
        Assert.False(performance.ReadLatencyPicoseconds.HasValue);
    }

    [Fact]
    public void HMATSkipsMemorySideCachesAndMissingOrUnreachableEntriesAndRangesTheRest()
    {
        byte[] table = BuildHMAT(
            revision: 2,
            Locality(hierarchy: 1, AccessLatency, baseUnit: 1_000, [20]),
            Locality(hierarchy: 0, AccessLatency, baseUnit: 1_000, [100, 0, ushort.MaxValue, 140]));

        MemoryFirmwarePerformance performance = MemoryFirmwarePerformanceReader.Parse(table);

        Assert.Equal(new MemoryFirmwareMetricRange(100_000, 140_000), performance.AccessLatencyPicoseconds);
    }

    [Fact]
    public void HMATRevision1CountsTenthsOfNanosecondsAndMegabytesPerSecond()
    {
        byte[] table = BuildHMAT(
            revision: 1,
            Locality(hierarchy: 0, ReadLatency, baseUnit: 1, [850]),
            Locality(hierarchy: 0, AccessBandwidth, baseUnit: 1, [500]));

        MemoryFirmwarePerformance performance = MemoryFirmwarePerformanceReader.Parse(table);

        Assert.Equal(new MemoryFirmwareMetricRange(85_000, 85_000), performance.ReadLatencyPicoseconds);
        Assert.Equal(new MemoryFirmwareMetricRange(50, 50), performance.AccessBandwidthMegabytesPerSecond);
    }

    [Fact]
    public void HMATRejectsOtherTablesAndTruncatedStructures()
    {
        byte[] table = BuildHMAT(revision: 2, Locality(hierarchy: 0, AccessLatency, baseUnit: 1_000, [110]));
        byte[] truncated = table[..^2];
        BinaryPrimitives.WriteUInt32LittleEndian(truncated.AsSpan(4), (uint)truncated.Length);
        byte[] renamed = (byte[])table.Clone();
        renamed[0] = (byte)'X';

        Assert.Equal(MemoryFirmwarePerformance.None, MemoryFirmwarePerformanceReader.Parse(truncated));
        Assert.Equal(MemoryFirmwarePerformance.None, MemoryFirmwarePerformanceReader.Parse(renamed));
    }

    [Fact]
    public void MemoryStatisticsShowFirmwareLatencyAndBandwidthOnlyWhenDeclared()
    {
        MemoryPerformanceSnapshot declared = MemoryPerformanceSnapshot.Empty with
        {
            FirmwarePerformance = MemoryFirmwarePerformance.None with
            {
                AccessLatencyPicoseconds = new MemoryFirmwareMetricRange(110_000, 110_000),
                AccessBandwidthMegabytesPerSecond = new MemoryFirmwareMetricRange(108_000, 120_500)
            }
        };

        Dictionary<string, string> values = MemoryStatistics(declared);
        Dictionary<string, string> absent = MemoryStatistics(MemoryPerformanceSnapshot.Empty);

        Assert.Equal(
            string.Concat(110.0.ToString("0.#", CultureInfo.CurrentCulture), " ns"),
            values[L(nameof(AppStrings.Perf_Label_FirmwareLatency))]);
        Assert.Equal(
            string.Concat(
                108.0.ToString("0.#", CultureInfo.CurrentCulture),
                "\u2013",
                120.5.ToString("0.#", CultureInfo.CurrentCulture),
                " GB/s"),
            values[L(nameof(AppStrings.Perf_Label_FirmwareBandwidth))]);
        Assert.DoesNotContain(L(nameof(AppStrings.Perf_Label_FirmwareLatency)), absent.Keys);
        Assert.DoesNotContain(L(nameof(AppStrings.Perf_Label_FirmwareBandwidth)), absent.Keys);
    }

    [Fact]
    public void FirmwareReadAndWriteFiguresAreLabeledWhenNoAccessFigureExists()
    {
        MemoryPerformanceSnapshot memory = MemoryPerformanceSnapshot.Empty with
        {
            FirmwarePerformance = MemoryFirmwarePerformance.None with
            {
                ReadLatencyPicoseconds = new MemoryFirmwareMetricRange(90_000, 90_000),
                WriteLatencyPicoseconds = new MemoryFirmwareMetricRange(120_000, 120_000)
            }
        };

        string latency = MemoryStatistics(memory)[L(nameof(AppStrings.Perf_Label_FirmwareLatency))];

        Assert.Equal(
            F(
                nameof(AppStrings.Perf_Memory_FirmwareReadWriteFormat),
                string.Concat(90.0.ToString("0.#", CultureInfo.CurrentCulture), " ns"),
                string.Concat(120.0.ToString("0.#", CultureInfo.CurrentCulture), " ns")),
            latency);
    }

    [Fact]
    public void LabelsStayCenteredOverTheirSegmentsWhenTheyFit()
    {
        double[] lefts = new double[2];

        MemoryCompositionLabels.ArrangeLabels([100, 300], [40, 60], spacing: 10, availableWidth: 400, lefts);

        Assert.Equal(new double[] { 80, 270 }, lefts);
    }

    [Fact]
    public void CrowdedLabelsAreSeparatedAndKeptInsideTheBar()
    {
        // A wide in-use segment followed by thin modified, standby and free slivers at the right edge
        double[] anchors = [190, 392, 395, 399];
        double[] widths = [40, 50, 50, 30];
        double[] lefts = new double[4];

        MemoryCompositionLabels.ArrangeLabels(anchors, widths, spacing: 10, availableWidth: 400, lefts);

        Assert.Equal(170, lefts[0]);
        for (int index = 1; index < lefts.Length; index++)
            Assert.True(lefts[index] >= lefts[index - 1] + widths[index - 1] + 10);
        Assert.Equal(400, lefts[^1] + widths[^1]);
    }

    [Theory]
    [InlineData("Microsoft-Windows-WHEA-Logger", 47, (int)MemoryErrorKind.Corrected)]
    [InlineData("Microsoft-Windows-WHEA-Logger", 49, (int)MemoryErrorKind.Corrected)]
    [InlineData("Microsoft-Windows-WHEA-Logger", 23, (int)MemoryErrorKind.Corrected)]
    [InlineData("Microsoft-Windows-WHEA-Logger", 46, (int)MemoryErrorKind.Uncorrected)]
    [InlineData("Microsoft-Windows-WHEA-Logger", 48, (int)MemoryErrorKind.Uncorrected)]
    [InlineData("Microsoft-Windows-WHEA-Logger", 22, (int)MemoryErrorKind.Uncorrected)]
    [InlineData("Microsoft-Windows-MemoryDiagnostics-Results", 1102, (int)MemoryErrorKind.DiagnosticFailure)]
    public void MemoryErrorEventsAreClassifiedByProviderAndID(string provider, int eventID, int kind) =>
        Assert.Equal((MemoryErrorKind)kind, MemoryErrorLogMonitor.Classify(provider, eventID));

    [Theory]
    [InlineData("Microsoft-Windows-WHEA-Logger", 19)]
    [InlineData("Microsoft-Windows-WHEA-Logger", 18)]
    [InlineData("Microsoft-Windows-MemoryDiagnostics-Results", 1101)]
    public void NonMemoryAndPassingEventsAreNotErrors(string provider, int eventID) =>
        Assert.Null(MemoryErrorLogMonitor.Classify(provider, eventID));

    [Fact]
    public void ResetAcknowledgesEverythingRecordedSoFarAndCountsOnlyLaterErrors()
    {
        MemoryErrorEntry older = Entry(recordID: 10, fileTime: 1_000);
        MemoryErrorEntry newest = Entry(recordID: 12, fileTime: 3_000);
        MemoryErrorEntry middle = Entry(recordID: 11, fileTime: 2_000);
        MemoryErrorEntry[] entries = [newest, middle, older];

        MemoryErrorAcknowledgement acknowledgement = MemoryErrorAcknowledgement.Through(entries);
        MemoryErrorEntry later = Entry(recordID: 13, fileTime: 4_000);

        Assert.Equal(3, MemoryErrorAcknowledgement.None.CountUnacknowledged(entries));
        Assert.Equal(new MemoryErrorAcknowledgement(3_000, 12), acknowledgement);
        Assert.Equal(0, acknowledgement.CountUnacknowledged(entries));
        Assert.Equal(1, acknowledgement.CountUnacknowledged([later, .. entries]));
    }

    [Fact]
    public void ErrorsLoggedAfterTheLogWasClearedStillCountDespiteRestartedRecordIDs()
    {
        MemoryErrorAcknowledgement acknowledgement = new(TimeCreatedFileTime: 5_000, RecordID: 9_000);

        Assert.Equal(1, acknowledgement.CountUnacknowledged([Entry(recordID: 3, fileTime: 6_000)]));
        Assert.Equal(0, acknowledgement.CountUnacknowledged([Entry(recordID: 8_999, fileTime: 5_000)]));
        Assert.Equal(1, acknowledgement.CountUnacknowledged([Entry(recordID: 9_001, fileTime: 5_000)]));
    }

    [Fact]
    public void MemoryErrorAcknowledgementRoundTripsThroughSettingsXml()
    {
        AppSettings settings = new() { Autosave = false };
        settings.AcknowledgeMemoryErrors(new MemoryErrorAcknowledgement(133_000_000_000_000_000, 42));
        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            TrayXmlSerializer.WriteFile(path, settings);
            AppSettings reloaded = AppSettings.LoadOrDefault(path);

            Assert.Equal(
                new MemoryErrorAcknowledgement(133_000_000_000_000_000, 42),
                reloaded.MemoryErrorAcknowledgement);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MemoryHardwareNameRulesAreDroppedBecauseMemoryHasNoHardwareName()
    {
        List<PerformanceHardwareNameReplacementRule> rules = PerformanceHardwareNameReplacementRuleCollection.Normalize(
        [
            new PerformanceHardwareNameReplacementRule
            {
                DeviceKind = PerformanceDeviceKind.Memory, MatchPattern = "Physical", Replacement = "RAM"
            },
            new PerformanceHardwareNameReplacementRule
            {
                DeviceKind = PerformanceDeviceKind.Disk, MatchPattern = "Samsung", Replacement = "S"
            }
        ]);

        PerformanceHardwareNameReplacementRule rule = Assert.Single(rules);
        Assert.Equal(PerformanceDeviceKind.Disk, rule.DeviceKind);
    }

    private static Dictionary<string, string> MemoryStatistics(MemoryPerformanceSnapshot memory)
    {
        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory
            .Create(PerformanceSnapshot.Empty with { Memory = memory })
            .Single(static candidate => candidate.Kind == PerformanceDeviceKind.Memory);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (PerformanceStatistic statistic in device.Statistics.Span)
            values[statistic.Label] = statistic.Value;
        return values;
    }

    private static MemoryErrorEntry Entry(long recordID, long fileTime) =>
        new(recordID, fileTime, EventID: 47, MemoryErrorKind.Corrected, Message: "Corrected memory error");

    private static byte[] Locality(int hierarchy, byte dataType, ulong baseUnit, ushort[] entries)
    {
        byte[] structure = new byte[32 + 4 + 4 * entries.Length + 2 * entries.Length];
        Span<byte> span = structure;
        BinaryPrimitives.WriteUInt16LittleEndian(span, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)structure.Length);
        span[8] = (byte)hierarchy;
        span[9] = dataType;
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], (uint)entries.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(span[24..], baseUnit);
        int entriesOffset = 32 + 4 + 4 * entries.Length;
        for (int index = 0; index < entries.Length; index++)
            BinaryPrimitives.WriteUInt16LittleEndian(span[(entriesOffset + 2 * index)..], entries[index]);
        return structure;
    }

    private static byte[] BuildHMAT(byte revision, params byte[][] structures)
    {
        int length = 40 + structures.Sum(static structure => structure.Length);
        byte[] table = new byte[length];
        "HMAT"u8.CopyTo(table);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(4), (uint)length);
        table[8] = revision;
        int offset = 40;
        foreach (byte[] structure in structures)
        {
            structure.CopyTo(table, offset);
            offset += structure.Length;
        }

        return table;
    }
}
