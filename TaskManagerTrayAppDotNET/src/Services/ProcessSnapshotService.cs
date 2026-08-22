using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>
/// Samples processes on one dedicated background thread and publishes into a fixed-capacity double buffer.
/// </summary>
internal sealed class ProcessSnapshotService : IDisposable
{
    public const int MaximumProcessCount = 8_192;
    private const int HistoryCapacity = MaximumProcessCount * 2;
    private const int RefreshIntervalMilliseconds = 1_000;
    private const int ShutdownJoinTimeoutMilliseconds = 2_000;

    private static readonly ProcessRowComparer RowComparer = new();
    private readonly Lock _publishGate = new();
    private readonly AutoResetEvent _refreshWake = new(false);
    private readonly Thread _samplingThread;
    private readonly Action _notifySnapshotAvailable;
    private readonly Dictionary<int, ProcessHistoryEntry> _history = new(HistoryCapacity);
    private readonly int[] _staleProcessIDs = new int[HistoryCapacity];
    private ProcessSnapshotRow[] _publishedRows = new ProcessSnapshotRow[MaximumProcessCount];
    private ProcessSnapshotRow[] _stagingRows = new ProcessSnapshotRow[MaximumProcessCount];
    private int _publishedCount;
    private long _publishedVersion;
    private long _lastSampleTimestamp;
    private int _historyGeneration;
    private int _notificationPending;
    private int _started;
    private int _disposed;
    private bool _capacityWarningLogged;

    public ProcessSnapshotService()
    {
        _notifySnapshotAvailable = NotifySnapshotAvailable;
        _samplingThread = new Thread(SamplingLoop)
        {
            IsBackground = true,
            Name = Constants.ApplicationName + ".ProcessSampler",
            // Sampling must never outrank UI input or process actions under contention
            Priority = ThreadPriority.BelowNormal
        };
    }

    public event Action? SnapshotAvailable;

    /// <summary>Starts the pre-created sampling thread once.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0) return;

        _samplingThread.Start();
    }

    /// <summary>Wakes the sampler without queuing another worker or UI callback.</summary>
    public void RequestRefresh()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _refreshWake.Set();
    }

    /// <summary>Copies the latest immutable published buffer into caller-owned preallocated storage.</summary>
    public int CopyLatest(ProcessSnapshotRow[] destination, out long version)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Length < MaximumProcessCount)
            throw new ArgumentException("The process snapshot destination is smaller than the fixed capacity.", nameof(destination));

        lock (_publishGate)
        {
            int count = _publishedCount;
            Array.Copy(_publishedRows, destination, count);
            version = _publishedVersion;
            return count;
        }
    }

    private void SamplingLoop()
    {
        while (Volatile.Read(ref _disposed) == 0)
        {
            try
            {
                RefreshCore();
            }
            catch (Exception exception)
            {
                TADNLog.Log($"ProcessSnapshotService.RefreshCore: {exception}");
            }

            if (Volatile.Read(ref _disposed) != 0) return;
            _refreshWake.WaitOne(RefreshIntervalMilliseconds);
        }
    }

    private void RefreshCore()
    {
        long sampleTimestamp = Stopwatch.GetTimestamp();
        long previousTimestamp = _lastSampleTimestamp;
        _lastSampleTimestamp = sampleTimestamp;
        double elapsedSeconds = previousTimestamp == 0
            ? 0
            : (sampleTimestamp - previousTimestamp) / (double)Stopwatch.Frequency;

        int generation = NextHistoryGeneration();
        Array.Clear(_stagingRows);
        Process[] processes = Process.GetProcesses();
        int count = 0;
        int processedProcessCount = 0;
        try
        {
            for (int processIndex = 0; processIndex < processes.Length; processIndex++)
            {
                using Process process = processes[processIndex];
                processedProcessCount = processIndex + 1;
                if (count >= MaximumProcessCount)
                {
                    LogCapacityWarningOnce(processes.Length);
                    continue;
                }

                int processID = ReadProcessID(process);
                if (processID < 0) continue;

                bool hadHistory = _history.TryGetValue(processID, out ProcessHistoryEntry history);
                string processName = hadHistory ? history.Name : ReadProcessName(process, processID);
                long totalProcessorTicks = ReadTotalProcessorTicks(process);
                double cpuPercent = CalculateCPUPercent(history, hadHistory, totalProcessorTicks, elapsedSeconds);
                ProcessOwnerKind owner = ReadOwner(process);

                _stagingRows[count] = new ProcessSnapshotRow
                {
                    ProcessID = processID,
                    Name = processName,
                    State = ProcessExecutionState.Running,
                    Owner = owner,
                    CPUPercent = cpuPercent,
                    PrivateMemoryBytes = ReadPrivateMemoryBytes(process),
                    WorkingSetBytes = ReadWorkingSetBytes(process),
                    CommandLine = null
                };
                count++;

                _history[processID] = new ProcessHistoryEntry(processName, totalProcessorTicks, generation);
            }
        }
        finally
        {
            // Dispose entries not reached when an unexpected sampler failure interrupts the loop
            for (int processIndex = processedProcessCount; processIndex < processes.Length; processIndex++)
                processes[processIndex].Dispose();
        }

        RemoveStaleHistory(generation);
        Array.Sort(_stagingRows, 0, count, RowComparer);
        Publish(count);
    }

    private int NextHistoryGeneration()
    {
        int next = unchecked(_historyGeneration + 1);
        if (next != 0)
        {
            _historyGeneration = next;
            return next;
        }

        _history.Clear();
        _historyGeneration = 1;
        return 1;
    }

    private static int ReadProcessID(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static string ReadProcessName(Process process, int processID)
    {
        try
        {
            string name = process.ProcessName;
            if (string.IsNullOrWhiteSpace(name)) return "Process " + processID;
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return name;
            if (name[0] is '[' or '<') return name;
            return string.Concat(name, ".exe");
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return "Process " + processID;
        }
    }

    private static long ReadTotalProcessorTicks(Process process)
    {
        try
        {
            return process.TotalProcessorTime.Ticks;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return 0;
        }
    }

    private static long ReadPrivateMemoryBytes(Process process)
    {
        try
        {
            return Math.Max(0, process.PrivateMemorySize64);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return 0;
        }
    }

    private static long ReadWorkingSetBytes(Process process)
    {
        try
        {
            return Math.Max(0, process.WorkingSet64);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return 0;
        }
    }

    private static ProcessOwnerKind ReadOwner(Process process)
    {
        try
        {
            return process.SessionId == 0
                ? ProcessOwnerKind.System
                : ProcessOwnerKind.CurrentUser;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return ProcessOwnerKind.Unavailable;
        }
    }

    private static double CalculateCPUPercent(
        ProcessHistoryEntry history,
        bool hadHistory,
        long totalProcessorTicks,
        double elapsedSeconds)
    {
        if (!hadHistory || elapsedSeconds <= 0 || totalProcessorTicks < history.TotalProcessorTicks) return 0;

        long processorTickDelta = totalProcessorTicks - history.TotalProcessorTicks;
        double processorSeconds = processorTickDelta / (double)TimeSpan.TicksPerSecond;
        double normalized = processorSeconds / elapsedSeconds / Environment.ProcessorCount * 100;
        return Math.Clamp(normalized, 0, 100);
    }

    private void RemoveStaleHistory(int generation)
    {
        int staleCount = 0;
        foreach (KeyValuePair<int, ProcessHistoryEntry> pair in _history)
        {
            if (pair.Value.LastSeenGeneration == generation) continue;
            _staleProcessIDs[staleCount] = pair.Key;
            staleCount++;
        }

        for (int staleIndex = 0; staleIndex < staleCount; staleIndex++)
            _history.Remove(_staleProcessIDs[staleIndex]);
    }

    private void Publish(int count)
    {
        lock (_publishGate)
        {
            ProcessSnapshotRow[] previousPublished = _publishedRows;
            _publishedRows = _stagingRows;
            _stagingRows = previousPublished;
            _publishedCount = count;
            _publishedVersion++;
        }

        if (Interlocked.Exchange(ref _notificationPending, 1) != 0) return;
        Dispatcher.UIThread.Post(_notifySnapshotAvailable, DispatcherPriority.Background);
    }

    private void NotifySnapshotAvailable()
    {
        Interlocked.Exchange(ref _notificationPending, 0);
        try
        {
            SnapshotAvailable?.Invoke();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"ProcessSnapshotService.SnapshotAvailable: {exception}");
        }
    }

    private void LogCapacityWarningOnce(int observedProcessCount)
    {
        if (_capacityWarningLogged) return;

        _capacityWarningLogged = true;
        TADNLog.Log(
            $"ProcessSnapshotService capacity {MaximumProcessCount} was exceeded by {observedProcessCount} processes.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        SnapshotAvailable = null;
        _refreshWake.Set();
        if (Volatile.Read(ref _started) != 0 && !_samplingThread.Join(ShutdownJoinTimeoutMilliseconds))
            TADNLog.Log("ProcessSnapshotService sampling thread did not stop before the shutdown timeout.");

        _refreshWake.Dispose();
        _history.Clear();
    }

    private readonly record struct ProcessHistoryEntry(
        string Name,
        long TotalProcessorTicks,
        int LastSeenGeneration);

    private sealed class ProcessRowComparer : IComparer<ProcessSnapshotRow>
    {
        public int Compare(ProcessSnapshotRow left, ProcessSnapshotRow right)
        {
            int nameComparison = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            return nameComparison != 0 ? nameComparison : left.ProcessID.CompareTo(right.ProcessID);
        }
    }
}
