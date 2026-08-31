using System.Diagnostics;
using TaskManagerTrayAppDotNET.UI;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Accumulates process CPU deltas and sampled network rates for the current app session.</summary>
internal sealed class AppHistoryStore
{
    private readonly Lock _gate = new();

    private readonly Dictionary<string, AppAccumulator> _apps =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<ProcessInstanceKey, ProcessBaseline> _processBaselines = [];
    private readonly HashSet<ProcessInstanceKey> _seenProcesses = [];
    private readonly List<ProcessInstanceKey> _staleProcesses = [];
    private DateTimeOffset _startedAt;
    private long _lastSampleTimestamp;
    private bool _hasSampleTimestamp;
    private long _version;

    public AppHistoryStore()
        : this(DateTimeOffset.Now)
    {
    }

    internal AppHistoryStore(DateTimeOffset startedAt) => _startedAt = startedAt;

    public static ulong RequiredColumnMask =>
        ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.Name)
        | ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.CPUTime)
        | ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.Network);

    public DateTimeOffset StartedAt
    {
        get
        {
            lock (_gate)
                return _startedAt;
        }
    }

    /// <summary>Consumes one process sample using the current monotonic timestamp.</summary>
    public bool Consume(ProcessSnapshotBuffer snapshot) => Consume(snapshot, Stopwatch.GetTimestamp());

    /// <summary>Consumes one monotonic process sample when all required columns are present.</summary>
    public bool Consume(ProcessSnapshotBuffer snapshot, long sampleTimestamp)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProcessDataSchema? schema = snapshot.Schema;
        if (schema == null || (schema.VisibleMask & RequiredColumnMask) != RequiredColumnMask)
            return false;

        lock (_gate)
        {
            double elapsedSeconds = ResolveElapsedSeconds(sampleTimestamp);
            _seenProcesses.Clear();
            for (int rowIndex = 0; rowIndex < snapshot.Count; rowIndex++)
            {
                ProcessStaticData? row = snapshot.StaticRows[rowIndex];
                if (row == null) continue;

                ProcessInstanceKey processKey = row.InstanceKey;
                _seenProcesses.Add(processKey);
                ProcessImageIdentity image = row.Image;
                AppAccumulator app = GetOrCreateApp(image);
                long totalCPUTimeTicks = snapshot.GetDynamicNumeric(
                    rowIndex,
                    ProcessTableColumnKind.CPUTime);
                AccumulateCPUTime(processKey, image.Key, totalCPUTimeTicks, app);
                AccumulateNetwork(
                    snapshot.GetDynamicNumeric(rowIndex, ProcessTableColumnKind.Network),
                    elapsedSeconds,
                    app);
            }

            RemoveExitedProcessBaselines();
            _lastSampleTimestamp = sampleTimestamp;
            _hasSampleTimestamp = true;
            _version = unchecked(_version + 1);
            return true;
        }
    }

    /// <summary>Returns an immutable, deterministically ordered history snapshot.</summary>
    public AppHistorySnapshot GetSnapshot()
    {
        lock (_gate)
        {
            AppHistoryEntry[] entries = new AppHistoryEntry[_apps.Count];
            int entryIndex = 0;
            foreach (AppAccumulator app in _apps.Values)
            {
                entries[entryIndex] = new AppHistoryEntry(
                    app.Key,
                    app.Name,
                    app.ExecutablePath,
                    app.IconSource,
                    app.CPUTimeTicks,
                    app.NetworkBytes,
                    NotificationsAvailable: false,
                    NotificationCount: 0);
                entryIndex++;
            }

            Array.Sort(entries, CompareEntries);
            return new AppHistorySnapshot(_startedAt, _version, entries);
        }
    }

    /// <summary>Deletes every retained entry and establishes a new session-history start time.</summary>
    public void DeleteHistory() => DeleteHistory(DateTimeOffset.Now);

    /// <summary>Resets session history without persisting any prior resource totals.</summary>
    public void Reset() => DeleteHistory();

    internal void DeleteHistory(DateTimeOffset startedAt)
    {
        lock (_gate)
        {
            _apps.Clear();
            _processBaselines.Clear();
            _seenProcesses.Clear();
            _staleProcesses.Clear();
            _startedAt = startedAt;
            _lastSampleTimestamp = 0;
            _hasSampleTimestamp = false;
            _version = unchecked(_version + 1);
        }
    }

    internal void Reset(DateTimeOffset startedAt) => DeleteHistory(startedAt);

    private double ResolveElapsedSeconds(long sampleTimestamp)
    {
        if (!_hasSampleTimestamp || sampleTimestamp <= _lastSampleTimestamp) return 0;

        return (sampleTimestamp - _lastSampleTimestamp) / (double)Stopwatch.Frequency;
    }

    private AppAccumulator GetOrCreateApp(ProcessImageIdentity image)
    {
        if (_apps.TryGetValue(image.Key, out AppAccumulator? existing)) return existing;

        AppAccumulator app = new(
            image.Key,
            image.Name,
            image.ImagePath,
            image.IconSource);
        _apps.Add(image.Key, app);
        return app;
    }

    private void AccumulateCPUTime(
        ProcessInstanceKey processKey,
        string imageKey,
        long totalCPUTimeTicks,
        AppAccumulator app)
    {
        if (_processBaselines.TryGetValue(processKey, out ProcessBaseline baseline)
            && string.Equals(baseline.ImageKey, imageKey, StringComparison.OrdinalIgnoreCase)
            && totalCPUTimeTicks >= baseline.TotalCPUTimeTicks)
        {
            app.CPUTimeTicks = SaturatingAdd(
                app.CPUTimeTicks,
                totalCPUTimeTicks - baseline.TotalCPUTimeTicks);
        }

        _processBaselines[processKey] = new ProcessBaseline(imageKey, Math.Max(val1: 0, totalCPUTimeTicks));
    }

    private static void AccumulateNetwork(
        long encodedRate,
        double elapsedSeconds,
        AppAccumulator app)
    {
        if (elapsedSeconds <= 0 || !double.IsFinite(elapsedSeconds)) return;

        double bytesPerSecond = BitConverter.Int64BitsToDouble(encodedRate);
        if (!double.IsFinite(bytesPerSecond) || bytesPerSecond < 0) return;

        double addedBytes = bytesPerSecond * elapsedSeconds;
        if (!double.IsFinite(addedBytes) || addedBytes < 0) return;

        double nextBytes = app.NetworkBytes + addedBytes;
        app.NetworkBytes = double.IsFinite(nextBytes) ? nextBytes : double.MaxValue;
    }

    private void RemoveExitedProcessBaselines()
    {
        _staleProcesses.Clear();
        foreach (ProcessInstanceKey processKey in _processBaselines.Keys)
        {
            if (!_seenProcesses.Contains(processKey))
                _staleProcesses.Add(processKey);
        }

        for (int staleIndex = 0; staleIndex < _staleProcesses.Count; staleIndex++)
            _processBaselines.Remove(_staleProcesses[staleIndex]);
    }

    private static long SaturatingAdd(long left, long right)
    {
        if (right <= 0) return left;
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }

    private static int CompareEntries(AppHistoryEntry left, AppHistoryEntry right)
    {
        int nameComparison = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        return nameComparison != 0
            ? nameComparison
            : StringComparer.OrdinalIgnoreCase.Compare(left.Key, right.Key);
    }

    private sealed class AppAccumulator(
        string key,
        string name,
        string executablePath,
        ProcessIconSource iconSource)
    {
        public string Key { get; } = key;
        public string Name { get; } = name;
        public string ExecutablePath { get; } = executablePath;
        public ProcessIconSource IconSource { get; } = iconSource;
        public long CPUTimeTicks { get; set; }
        public double NetworkBytes { get; set; }
    }

    private readonly record struct ProcessBaseline(string ImageKey, long TotalCPUTimeTicks);
}
