using System.Diagnostics;
using System.Globalization;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class AppHistoryStoreTests
{
    [Fact]
    public void AggregatesCPUAndNetworkDeltasByStableImageIdentity()
    {
        DateTimeOffset startedAt = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        AppHistoryStore store = new(startedAt);
        long firstTimestamp = Stopwatch.Frequency * 10L;
        ProcessSnapshotBuffer first = CreateSnapshot(
            new ProcessSample(10, 100, "editor", "editor.exe", 100, 1_000),
            new ProcessSample(11, 110, "editor", "editor.exe", 200, 2_000));
        ProcessSnapshotBuffer second = CreateSnapshot(
            new ProcessSample(10, 100, "editor", "editor.exe", 160, 1_000),
            new ProcessSample(11, 110, "editor", "editor.exe", 260, 500));

        Assert.True(store.Consume(first, firstTimestamp));
        AppHistoryEntry baseline = Assert.Single(store.GetSnapshot().Entries);
        Assert.Equal(0, baseline.CPUTimeTicks);
        Assert.Equal(0, baseline.NetworkBytes);

        Assert.True(store.Consume(second, firstTimestamp + Stopwatch.Frequency * 2L));
        AppHistoryEntry accumulated = Assert.Single(store.GetSnapshot().Entries);

        Assert.Equal(120, accumulated.CPUTimeTicks);
        Assert.Equal(3_000, accumulated.NetworkBytes);
        Assert.Equal("editor", accumulated.Key);
        Assert.Equal("editor.exe", accumulated.Name);
        Assert.Equal(@"C:\Apps\editor.exe", accumulated.ExecutablePath);
        Assert.Equal(@"C:\Apps\editor.exe", accumulated.IconSource.ExecutablePath);
        Assert.False(accumulated.NotificationsAvailable);
        Assert.Equal(0, accumulated.NotificationCount);
        Assert.Equal(startedAt, store.StartedAt);
    }

    [Fact]
    public void RetainsExitedAppsAndDoesNotBridgeReusedProcessIdentities()
    {
        AppHistoryStore store = new(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        long firstTimestamp = Stopwatch.Frequency;
        ProcessSnapshotBuffer first = CreateSnapshot(
            new ProcessSample(42, 100, "tool", "tool.exe", 1_000, 0));
        ProcessSnapshotBuffer exited = CreateSnapshot();
        ProcessSnapshotBuffer reusedPID = CreateSnapshot(
            new ProcessSample(42, 200, "tool", "tool.exe", 50, 0));

        Assert.True(store.Consume(first, firstTimestamp));
        Assert.True(store.Consume(exited, firstTimestamp + Stopwatch.Frequency));
        Assert.Single(store.GetSnapshot().Entries);
        Assert.True(store.Consume(reusedPID, firstTimestamp + Stopwatch.Frequency * 2L));

        AppHistoryEntry entry = Assert.Single(store.GetSnapshot().Entries);
        Assert.Equal(0, entry.CPUTimeTicks);
    }

    [Fact]
    public void DeleteHistoryClearsEntriesAndRebaselinesFollowingSamples()
    {
        DateTimeOffset firstStart = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset resetStart = firstStart.AddMinutes(5);
        AppHistoryStore store = new(firstStart);
        long firstTimestamp = Stopwatch.Frequency;
        ProcessSnapshotBuffer baseline = CreateSnapshot(
            new ProcessSample(7, 70, "sample", "sample.exe", 100, 100));
        ProcessSnapshotBuffer changed = CreateSnapshot(
            new ProcessSample(7, 70, "sample", "sample.exe", 180, 100));
        Assert.True(store.Consume(baseline, firstTimestamp));
        Assert.True(store.Consume(changed, firstTimestamp + Stopwatch.Frequency));
        Assert.Equal(80, Assert.Single(store.GetSnapshot().Entries).CPUTimeTicks);

        store.DeleteHistory(resetStart);

        AppHistorySnapshot empty = store.GetSnapshot();
        Assert.Empty(empty.Entries);
        Assert.Equal(resetStart, empty.StartedAt);
        Assert.True(store.Consume(changed, firstTimestamp + Stopwatch.Frequency * 2L));
        AppHistoryEntry rebaselined = Assert.Single(store.GetSnapshot().Entries);
        Assert.Equal(0, rebaselined.CPUTimeTicks);
        Assert.Equal(0, rebaselined.NetworkBytes);
    }

    [Fact]
    public void RejectsSnapshotsWithoutTheRequiredColumns()
    {
        ProcessDataSchema schema = ProcessDataSchema.Create(
        [
            Setting(ProcessTableColumnKind.Name),
            Setting(ProcessTableColumnKind.CPUTime)
        ]);
        ProcessSnapshotBuffer incomplete = new();
        incomplete.BeginWrite(schema, 0);
        incomplete.CompleteWrite(0);
        AppHistoryStore store = new();

        Assert.False(store.Consume(incomplete, Stopwatch.GetTimestamp()));
        Assert.Empty(store.GetSnapshot().Entries);
    }

    [Fact]
    public void FormatsAppHistoryValuesAtTaskManagerCadence()
    {
        Assert.Equal("1:02:03", TaskManagerUsageFormatter.FormatCPUTime(TimeSpan.FromHours(1).Ticks
                                                                        + TimeSpan.FromMinutes(2).Ticks
                                                                        + TimeSpan.FromSeconds(3).Ticks));
        Assert.Equal(
            "1.5 MB",
            TaskManagerUsageFormatter.FormatAppHistoryNetwork(
                1.5 * 1_048_576,
                CultureInfo.InvariantCulture));
        Assert.Equal(
            "Unavailable",
            TaskManagerUsageFormatter.FormatAppHistoryNetwork(double.NaN, CultureInfo.InvariantCulture));
    }

    private static ProcessSnapshotBuffer CreateSnapshot(params ProcessSample[] samples)
    {
        ProcessDataSchema schema = ProcessDataSchema.Create(
        [
            Setting(ProcessTableColumnKind.Name),
            Setting(ProcessTableColumnKind.CPUTime),
            Setting(ProcessTableColumnKind.Network)
        ]);
        ProcessSnapshotBuffer snapshot = new();
        snapshot.BeginWrite(schema, samples.Length);
        for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
        {
            ProcessSample sample = samples[sampleIndex];
            string executablePath = Path.Combine(@"C:\Apps", sample.Name);
            ProcessIconSource iconSource = new(executablePath, null);
            ProcessImageIdentity image = new(
                sample.ImageKey,
                sample.Name,
                executablePath,
                string.Empty,
                iconSource);
            long[] dynamicValues = new long[schema.DynamicNumericCount];
            dynamicValues[schema.GetDynamicNumericSlot(ProcessTableColumnKind.CPUTime)] =
                sample.CPUTimeTicks;
            dynamicValues[schema.GetDynamicNumericSlot(ProcessTableColumnKind.Network)] =
                BitConverter.DoubleToInt64Bits(sample.NetworkBytesPerSecond);
            ProcessStaticData row = new()
            {
                InstanceKey = new ProcessInstanceKey(sample.ProcessID, sample.CreationTimeTicks),
                Image = image,
                UserName = "DOMAIN\\user",
                NumericValues = new long[schema.StaticNumericCount],
                TextValues = new string?[schema.StaticTextCount]
            };
            snapshot.SetRow(sampleIndex, row, dynamicValues, new string?[schema.DynamicTextCount]);
        }

        snapshot.CompleteWrite(samples.Length);
        return snapshot;
    }

    private static ProcessColumnSetting Setting(ProcessTableColumnKind column) =>
        new()
        {
            Column = column,
            Visible = true,
            Width = ProcessTableColumnCatalog.Get(column).DefaultWidth
        };

    private sealed record ProcessSample(
        int ProcessID,
        long CreationTimeTicks,
        string ImageKey,
        string Name,
        long CPUTimeTicks,
        double NetworkBytesPerSecond);
}
