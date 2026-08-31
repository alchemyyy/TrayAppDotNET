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
        DateTimeOffset startedAt = new(year: 2026, month: 8, day: 30, hour: 12, minute: 0, second: 0, TimeSpan.Zero);
        AppHistoryStore store = new(startedAt);
        long firstTimestamp = Stopwatch.Frequency * 10L;
        ProcessSnapshotBuffer first = CreateSnapshot(
            new ProcessSample(ProcessID: 10, CreationTimeTicks: 100, ImageKey: "editor", Name: "editor.exe",
                CPUTimeTicks: 100, NetworkBytesPerSecond: 1_000),
            new ProcessSample(ProcessID: 11, CreationTimeTicks: 110, ImageKey: "editor", Name: "editor.exe",
                CPUTimeTicks: 200, NetworkBytesPerSecond: 2_000));
        ProcessSnapshotBuffer second = CreateSnapshot(
            new ProcessSample(ProcessID: 10, CreationTimeTicks: 100, ImageKey: "editor", Name: "editor.exe",
                CPUTimeTicks: 160, NetworkBytesPerSecond: 1_000),
            new ProcessSample(ProcessID: 11, CreationTimeTicks: 110, ImageKey: "editor", Name: "editor.exe",
                CPUTimeTicks: 260, NetworkBytesPerSecond: 500));

        Assert.True(store.Consume(first, firstTimestamp));
        AppHistoryEntry baseline = Assert.Single(store.GetSnapshot().Entries);
        Assert.Equal(expected: 0, baseline.CPUTimeTicks);
        Assert.Equal(expected: 0, baseline.NetworkBytes);

        Assert.True(store.Consume(second, firstTimestamp + Stopwatch.Frequency * 2L));
        AppHistoryEntry accumulated = Assert.Single(store.GetSnapshot().Entries);

        Assert.Equal(expected: 120, accumulated.CPUTimeTicks);
        Assert.Equal(expected: 3_000, accumulated.NetworkBytes);
        Assert.Equal(expected: "editor", accumulated.Key);
        Assert.Equal(expected: "editor.exe", accumulated.Name);
        Assert.Equal(expected: @"C:\Apps\editor.exe", accumulated.ExecutablePath);
        Assert.Equal(expected: @"C:\Apps\editor.exe", accumulated.IconSource.ExecutablePath);
        Assert.False(accumulated.NotificationsAvailable);
        Assert.Equal(expected: 0, accumulated.NotificationCount);
        Assert.Equal(startedAt, store.StartedAt);
    }

    [Fact]
    public void RetainsExitedAppsAndDoesNotBridgeReusedProcessIdentities()
    {
        AppHistoryStore store =
            new(new DateTimeOffset(year: 2026, month: 8, day: 30, hour: 12, minute: 0, second: 0, TimeSpan.Zero));
        long firstTimestamp = Stopwatch.Frequency;
        ProcessSnapshotBuffer first = CreateSnapshot(
            new ProcessSample(ProcessID: 42, CreationTimeTicks: 100, ImageKey: "tool", Name: "tool.exe",
                CPUTimeTicks: 1_000, NetworkBytesPerSecond: 0));
        ProcessSnapshotBuffer exited = CreateSnapshot();
        ProcessSnapshotBuffer reusedPID = CreateSnapshot(
            new ProcessSample(ProcessID: 42, CreationTimeTicks: 200, ImageKey: "tool", Name: "tool.exe",
                CPUTimeTicks: 50, NetworkBytesPerSecond: 0));

        Assert.True(store.Consume(first, firstTimestamp));
        Assert.True(store.Consume(exited, firstTimestamp + Stopwatch.Frequency));
        Assert.Single(store.GetSnapshot().Entries);
        Assert.True(store.Consume(reusedPID, firstTimestamp + Stopwatch.Frequency * 2L));

        AppHistoryEntry entry = Assert.Single(store.GetSnapshot().Entries);
        Assert.Equal(expected: 0, entry.CPUTimeTicks);
    }

    [Fact]
    public void DeleteHistoryClearsEntriesAndRebaselinesFollowingSamples()
    {
        DateTimeOffset firstStart = new(year: 2026, month: 8, day: 30, hour: 12, minute: 0, second: 0, TimeSpan.Zero);
        DateTimeOffset resetStart = firstStart.AddMinutes(5);
        AppHistoryStore store = new(firstStart);
        long firstTimestamp = Stopwatch.Frequency;
        ProcessSnapshotBuffer baseline = CreateSnapshot(
            new ProcessSample(ProcessID: 7, CreationTimeTicks: 70, ImageKey: "sample", Name: "sample.exe",
                CPUTimeTicks: 100, NetworkBytesPerSecond: 100));
        ProcessSnapshotBuffer changed = CreateSnapshot(
            new ProcessSample(ProcessID: 7, CreationTimeTicks: 70, ImageKey: "sample", Name: "sample.exe",
                CPUTimeTicks: 180, NetworkBytesPerSecond: 100));
        Assert.True(store.Consume(baseline, firstTimestamp));
        Assert.True(store.Consume(changed, firstTimestamp + Stopwatch.Frequency));
        Assert.Equal(expected: 80, Assert.Single(store.GetSnapshot().Entries).CPUTimeTicks);

        store.DeleteHistory(resetStart);

        AppHistorySnapshot empty = store.GetSnapshot();
        Assert.Empty(empty.Entries);
        Assert.Equal(resetStart, empty.StartedAt);
        Assert.True(store.Consume(changed, firstTimestamp + Stopwatch.Frequency * 2L));
        AppHistoryEntry rebaselined = Assert.Single(store.GetSnapshot().Entries);
        Assert.Equal(expected: 0, rebaselined.CPUTimeTicks);
        Assert.Equal(expected: 0, rebaselined.NetworkBytes);
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
        incomplete.BeginWrite(schema, requiredCapacity: 0);
        incomplete.CompleteWrite(0);
        AppHistoryStore store = new();

        Assert.False(store.Consume(incomplete, Stopwatch.GetTimestamp()));
        Assert.Empty(store.GetSnapshot().Entries);
    }

    [Fact]
    public void FormatsAppHistoryValuesAtTaskManagerCadence()
    {
        Assert.Equal(expected: "1:02:03", TaskManagerUsageFormatter.FormatCPUTime(TimeSpan.FromHours(1).Ticks
            + TimeSpan.FromMinutes(2).Ticks
            + TimeSpan.FromSeconds(3).Ticks));
        Assert.Equal(
            expected: "1.5 MB",
            TaskManagerUsageFormatter.FormatAppHistoryNetwork(
                1.5 * 1_048_576,
                CultureInfo.InvariantCulture));
        Assert.Equal(
            expected: "Unavailable",
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
            string executablePath = Path.Combine(path1: @"C:\Apps", sample.Name);
            ProcessIconSource iconSource = new(executablePath, ApplicationUserModelID: null);
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
        new() { Column = column, Visible = true, Width = ProcessTableColumnCatalog.Get(column).DefaultWidth };

    private sealed record ProcessSample(
        int ProcessID,
        long CreationTimeTicks,
        string ImageKey,
        string Name,
        long CPUTimeTicks,
        double NetworkBytesPerSecond);
}
