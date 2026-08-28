using System.Diagnostics;
using Avalonia.Threading;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Retains and publishes direct-OS performance snapshots for the application lifetime.</summary>
internal sealed class PerformanceSnapshotService : IDisposable
{
    private readonly Lock _lifecycleGate = new();
    private readonly Lock _samplingGate = new();
    private readonly Lock _historyGate = new();
    private readonly AutoResetEvent _refreshWake = new(false);
    private readonly SystemPerformanceSampler _systemSampler = new();
    private readonly SystemPerformanceMetadataReader _metadataReader = new();
    private readonly NetworkPerformanceSampler _networkSampler = new();
    private readonly DiskPerformanceSampler _diskSampler = new();
    private readonly GPUPerformanceSampler _gpuSampler = new();
    private readonly Thread _samplingThread;
    private readonly Action _notifySnapshotUpdated;
    private PerformanceSnapshot[] _snapshotHistory;
    private PerformanceSnapshot _latestSnapshot = PerformanceSnapshot.Empty;
    private int _historyStartIndex;
    private int _historyCount;
    private int _sampleIntervalMilliseconds;
    private int _notificationPending;
    private int _refreshRequested;
    private int _resetBaselinesPending;
    private int _started;
    private int _disposed;
    private int _resourcesDisposed;
    private ulong _highestRecordedCPUSpeedHertz;
    private bool _systemFailureLogged;
    private bool _metadataFailureLogged;
    private bool _networkFailureLogged;
    private bool _diskFailureLogged;
    private bool _gpuFailureLogged;

    public PerformanceSnapshotService()
        : this(
            PerformanceSamplingSettings.DefaultSampleIntervalMilliseconds,
            PerformanceSamplingSettings.CalculateMaximumHistoryCount(
                PerformanceSamplingSettings.DefaultHistoryLengthMinutes,
                PerformanceSamplingSettings.DefaultSampleIntervalMilliseconds))
    {
    }

    public PerformanceSnapshotService(int sampleIntervalMilliseconds, int maximumHistoryCount)
    {
        ValidateMaximumHistoryCount(maximumHistoryCount);
        _sampleIntervalMilliseconds =
            PerformanceSamplingSettings.NormalizeSampleIntervalMilliseconds(
                sampleIntervalMilliseconds);
        _snapshotHistory = CreateHistoryBuffer(maximumHistoryCount);
        _notifySnapshotUpdated = NotifySnapshotUpdated;
        _samplingThread = new Thread(SamplingLoop)
        {
            IsBackground = true,
            Name = Constants.ApplicationName + ".PerformanceSampler",
            Priority = ThreadPriority.BelowNormal
        };
    }

    public event EventHandler<PerformanceSnapshot>? SnapshotUpdated;

    /// <summary>Starts application-lifetime sampling immediately and exactly once.</summary>
    public void Start()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_started != 0) return;

            Interlocked.Exchange(ref _resetBaselinesPending, 1);
            _started = 1;
            try
            {
                _samplingThread.Start();
            }
            catch
            {
                _started = 0;
                throw;
            }
        }
    }

    /// <summary>Returns the latest immutable snapshot without blocking the sampling worker.</summary>
    public PerformanceSnapshot GetLatestSnapshot() => Volatile.Read(ref _latestSnapshot);

    /// <summary>Returns a stable chronological copy of all retained snapshots.</summary>
    public IReadOnlyList<PerformanceSnapshot> GetSnapshotHistory()
    {
        lock (_historyGate)
        {
            if (_historyCount == 0) return Array.Empty<PerformanceSnapshot>();

            PerformanceSnapshot[] snapshots = new PerformanceSnapshot[_historyCount];
            for (int historyIndex = 0; historyIndex < _historyCount; historyIndex++)
            {
                int sourceIndex = (_historyStartIndex + historyIndex) % _snapshotHistory.Length;
                snapshots[historyIndex] = _snapshotHistory[sourceIndex];
            }

            return snapshots;
        }
    }

    /// <summary>Returns retained snapshots newer than a monotonic capture timestamp.</summary>
    public IReadOnlyList<PerformanceSnapshot> GetSnapshotHistoryAfter(long capturedTimestamp)
    {
        lock (_historyGate)
        {
            int firstMatchingIndex = 0;
            while (firstMatchingIndex < _historyCount)
            {
                int sourceIndex = (
                    _historyStartIndex
                    + firstMatchingIndex) % _snapshotHistory.Length;
                if (_snapshotHistory[sourceIndex].CapturedTimestamp > capturedTimestamp) break;
                firstMatchingIndex++;
            }

            int matchingCount = _historyCount - firstMatchingIndex;
            if (matchingCount == 0) return Array.Empty<PerformanceSnapshot>();

            PerformanceSnapshot[] snapshots = new PerformanceSnapshot[matchingCount];
            for (int matchingIndex = 0; matchingIndex < matchingCount; matchingIndex++)
            {
                int sourceIndex = (
                    _historyStartIndex
                    + firstMatchingIndex
                    + matchingIndex) % _snapshotHistory.Length;
                snapshots[matchingIndex] = _snapshotHistory[sourceIndex];
            }

            return snapshots;
        }
    }

    /// <summary>Applies live sampling and retention settings without discarding retained data.</summary>
    public void UpdateConfiguration(int sampleIntervalMilliseconds, int maximumHistoryCount)
    {
        ValidateMaximumHistoryCount(maximumHistoryCount);
        int normalizedSampleInterval =
            PerformanceSamplingSettings.NormalizeSampleIntervalMilliseconds(
                sampleIntervalMilliseconds);

        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _disposed) != 0) return;

            ResizeHistory(maximumHistoryCount);
            int previousSampleInterval = Interlocked.Exchange(
                ref _sampleIntervalMilliseconds,
                normalizedSampleInterval);
            if (_started != 0 && previousSampleInterval != normalizedSampleInterval)
                _refreshWake.Set();
        }
    }

    /// <summary>Wakes the worker for an early refresh without queuing another worker.</summary>
    public void RequestRefresh()
    {
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _disposed) != 0 || _started == 0) return;
            Interlocked.Exchange(ref _refreshRequested, 1);
            _refreshWake.Set();
        }
    }

    /// <summary>Captures synchronously for focused service tests without posting a UI callback.</summary>
    internal PerformanceSnapshot SampleNow()
    {
        PerformanceSnapshot snapshot;
        lock (_samplingGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            snapshot = CaptureSnapshot(Stopwatch.GetTimestamp());
            StoreSnapshot(snapshot);
        }
        return snapshot;
    }

    private void SamplingLoop()
    {
        long previousSamplingTimestamp = 0;
        try
        {
            while (Volatile.Read(ref _disposed) == 0)
            {
                bool refreshRequested = Interlocked.Exchange(ref _refreshRequested, 0) != 0;
                if (!refreshRequested && previousSamplingTimestamp > 0)
                {
                    int sampleIntervalMilliseconds = Volatile.Read(
                        ref _sampleIntervalMilliseconds);
                    int waitMilliseconds = CalculateWaitMilliseconds(
                        previousSamplingTimestamp,
                        sampleIntervalMilliseconds);
                    if (waitMilliseconds > 0)
                    {
                        _refreshWake.WaitOne(waitMilliseconds);
                        continue;
                    }
                }

                try
                {
                    lock (_samplingGate)
                    {
                        if (Volatile.Read(ref _disposed) == 0)
                        {
                            long samplingTimestamp = Stopwatch.GetTimestamp();
                            previousSamplingTimestamp = samplingTimestamp;
                            if (Interlocked.Exchange(ref _resetBaselinesPending, 0) != 0)
                                ResetSamplingBaselines();
                            PerformanceSnapshot snapshot = CaptureSnapshot(samplingTimestamp);
                            if (Volatile.Read(ref _disposed) == 0)
                                Publish(snapshot);
                        }
                    }
                }
                catch (Exception exception)
                {
                    TADNLog.Log($"PerformanceSnapshotService.CaptureSnapshot: {exception}");
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _disposed, 1);
            SnapshotUpdated = null;
            DisposeSamplingResources();
        }
    }

    private PerformanceSnapshot CaptureSnapshot(long samplingTimestamp)
    {
        SystemPerformanceSample systemSample = SampleSystem();
        SystemPerformanceMetadataSample metadata = SampleMetadata();
        CPUPerformanceSnapshot cpu = CreateCPUSnapshot(systemSample, metadata);
        MemoryPerformanceSnapshot memory = CreateMemorySnapshot(metadata);
        GPUPerformanceSnapshot[] gpus = SampleGPUs();
        NetworkPerformanceSnapshot[] networks = SampleNetworks();
        DiskPerformanceSnapshot[] disks = SampleDisks();
        return new PerformanceSnapshot(
            DateTimeOffset.UtcNow,
            samplingTimestamp,
            cpu,
            memory,
            gpus,
            networks,
            disks);
    }

    private void ResetSamplingBaselines()
    {
        _systemSampler.ResetProcessorBaseline();
        _metadataReader.ResetFrequencyBaseline();
        _networkSampler.ResetCounterBaselines();
        _diskSampler.ResetCounterBaselines();
        _gpuSampler.ResetCounterBaseline();
    }

    private SystemPerformanceSample SampleSystem()
    {
        try
        {
            SystemPerformanceSample sample = _systemSampler.Sample();
            _systemFailureLogged = false;
            return sample;
        }
        catch (Exception exception) when (IsRecoverableProviderException(exception))
        {
            LogFailureOnce(ref _systemFailureLogged, "system", exception);
            return SystemPerformanceSample.Empty;
        }
    }

    private SystemPerformanceMetadataSample SampleMetadata()
    {
        try
        {
            SystemPerformanceMetadataSample sample = _metadataReader.Sample();
            _metadataFailureLogged = false;
            return sample;
        }
        catch (Exception exception) when (IsRecoverableProviderException(exception))
        {
            LogFailureOnce(ref _metadataFailureLogged, "metadata", exception);
            return default;
        }
    }

    private GPUPerformanceSnapshot[] SampleGPUs()
    {
        try
        {
            GPUPerformanceSnapshot[] snapshots = _gpuSampler.Sample();
            _gpuFailureLogged = false;
            return snapshots;
        }
        catch (Exception exception) when (IsRecoverableProviderException(exception))
        {
            LogFailureOnce(ref _gpuFailureLogged, "GPU", exception);
            return [];
        }
    }

    private NetworkPerformanceSnapshot[] SampleNetworks()
    {
        try
        {
            NetworkPerformanceSnapshot[] snapshots = _networkSampler.Sample(Stopwatch.GetTimestamp());
            _networkFailureLogged = false;
            return snapshots;
        }
        catch (Exception exception) when (IsRecoverableProviderException(exception))
        {
            LogFailureOnce(ref _networkFailureLogged, "network", exception);
            return [];
        }
    }

    private DiskPerformanceSnapshot[] SampleDisks()
    {
        try
        {
            DiskPerformanceSnapshot[] snapshots = _diskSampler.Sample();
            _diskFailureLogged = false;
            return snapshots;
        }
        catch (Exception exception) when (IsRecoverableProviderException(exception))
        {
            LogFailureOnce(ref _diskFailureLogged, "disk", exception);
            return [];
        }
    }

    private CPUPerformanceSnapshot CreateCPUSnapshot(
        SystemPerformanceSample systemSample,
        SystemPerformanceMetadataSample metadata)
    {
        int logicalProcessorCount = _systemSampler.LastLogicalProcessorCount;
        if (logicalProcessorCount <= 0)
            logicalProcessorCount = Math.Max(0, metadata.LogicalProcessorCount);

        double[] logicalProcessorPercents = new double[logicalProcessorCount];
        int copiedProcessorCount = _systemSampler.CopyLastLogicalProcessorPercents(
            logicalProcessorPercents);
        if (copiedProcessorCount != logicalProcessorPercents.Length)
            Array.Resize(ref logicalProcessorPercents, copiedProcessorCount);

        SystemPerformanceInformation information = metadata.PerformanceInformation;
        if (metadata.HasFrequencyData)
        {
            _highestRecordedCPUSpeedHertz = Math.Max(
                _highestRecordedCPUSpeedHertz,
                metadata.HighestCurrentSpeedHertz);
        }
        return new CPUPerformanceSnapshot(
            CPUPerformanceSnapshot.StableDeviceID,
            PerformanceDeviceKind.CPU,
            0,
            string.IsNullOrWhiteSpace(metadata.ProcessorName) ? "CPU" : metadata.ProcessorName,
            _systemSampler.LastProcessorSampleAvailable,
            systemSample.CPUAveragePercent,
            systemSample.CPUHighestCorePercent,
            logicalProcessorPercents,
            metadata.HasFrequencyData,
            metadata.HighestCurrentSpeedHertz,
            metadata.BaseSpeedHertz,
            _highestRecordedCPUSpeedHertz,
            metadata.SocketCount,
            metadata.CoreCount,
            logicalProcessorCount,
            metadata.IsVirtualizationFirmwareEnabled,
            metadata.L1CacheBytes,
            metadata.L2CacheBytes,
            metadata.L3CacheBytes,
            metadata.HasPerformanceInformation ? information.ProcessCount : 0,
            metadata.HasPerformanceInformation ? information.ThreadCount : 0,
            metadata.HasPerformanceInformation ? information.HandleCount : 0,
            metadata.Uptime);
    }

    private MemoryPerformanceSnapshot CreateMemorySnapshot(SystemPerformanceMetadataSample metadata)
    {
        SystemMemoryStatus memoryStatus = _systemSampler.GetLastMemoryStatus();
        ulong usedPhysicalBytes = memoryStatus.TotalPhysicalBytes >= memoryStatus.AvailablePhysicalBytes
            ? memoryStatus.TotalPhysicalBytes - memoryStatus.AvailablePhysicalBytes
            : 0;
        SystemPerformanceInformation information = metadata.PerformanceInformation;
        return new MemoryPerformanceSnapshot(
            MemoryPerformanceSnapshot.StableDeviceID,
            PerformanceDeviceKind.Memory,
            0,
            _systemSampler.LastMemorySampleAvailable,
            memoryStatus.UtilizationPercent,
            memoryStatus.TotalPhysicalBytes,
            memoryStatus.AvailablePhysicalBytes,
            usedPhysicalBytes,
            metadata.InstalledPhysicalMemoryBytes,
            metadata.HasPerformanceInformation ? information.CommittedBytes : 0,
            metadata.HasPerformanceInformation ? information.CommitLimitBytes : 0,
            metadata.HasPerformanceInformation ? information.CachedBytes : 0,
            metadata.HasPerformanceInformation ? information.PagedPoolBytes : 0,
            metadata.HasPerformanceInformation ? information.NonPagedPoolBytes : 0);
    }

    private void Publish(PerformanceSnapshot snapshot)
    {
        StoreSnapshot(snapshot);
        if (SnapshotUpdated == null) return;
        if (Interlocked.Exchange(ref _notificationPending, 1) != 0) return;
        Dispatcher.UIThread.Post(_notifySnapshotUpdated, DispatcherPriority.Background);
    }

    private void NotifySnapshotUpdated()
    {
        Interlocked.Exchange(ref _notificationPending, 0);
        if (Volatile.Read(ref _disposed) != 0) return;

        try
        {
            SnapshotUpdated?.Invoke(this, GetLatestSnapshot());
        }
        catch (Exception exception)
        {
            TADNLog.Log($"PerformanceSnapshotService.SnapshotUpdated: {exception}");
        }
    }

    private void StoreSnapshot(PerformanceSnapshot snapshot)
    {
        lock (_historyGate)
        {
            int destinationIndex;
            if (_historyCount < _snapshotHistory.Length)
            {
                destinationIndex = (_historyStartIndex + _historyCount) % _snapshotHistory.Length;
                _historyCount++;
            }
            else
            {
                destinationIndex = _historyStartIndex;
                _historyStartIndex = (_historyStartIndex + 1) % _snapshotHistory.Length;
            }

            _snapshotHistory[destinationIndex] = snapshot;
            Volatile.Write(ref _latestSnapshot, snapshot);
        }
    }

    private void ResizeHistory(int maximumHistoryCount)
    {
        lock (_historyGate)
        {
            if (_snapshotHistory.Length == maximumHistoryCount) return;

            PerformanceSnapshot[] resizedHistory = CreateHistoryBuffer(maximumHistoryCount);
            int retainedCount = Math.Min(_historyCount, maximumHistoryCount);
            int skippedCount = _historyCount - retainedCount;
            for (int historyIndex = 0; historyIndex < retainedCount; historyIndex++)
            {
                int sourceIndex = (
                    _historyStartIndex
                    + skippedCount
                    + historyIndex) % _snapshotHistory.Length;
                resizedHistory[historyIndex] = _snapshotHistory[sourceIndex];
            }

            _snapshotHistory = resizedHistory;
            _historyStartIndex = 0;
            _historyCount = retainedCount;
        }
    }

    private static PerformanceSnapshot[] CreateHistoryBuffer(int maximumHistoryCount)
    {
        PerformanceSnapshot[] history = new PerformanceSnapshot[maximumHistoryCount];
        Array.Fill(history, PerformanceSnapshot.Empty);
        return history;
    }

    private static void ValidateMaximumHistoryCount(int maximumHistoryCount)
    {
        int supportedMaximum = PerformanceSamplingSettings.CalculateMaximumHistoryCount(
            PerformanceSamplingSettings.MaximumHistoryLengthMinutes,
            PerformanceSamplingSettings.MinimumSampleIntervalMilliseconds);
        if (maximumHistoryCount < 1 || maximumHistoryCount > supportedMaximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumHistoryCount),
                maximumHistoryCount,
                $"History capacity must be between 1 and {supportedMaximum} snapshots.");
        }
    }

    private static bool IsRecoverableProviderException(Exception exception) =>
        exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception
            or System.Runtime.InteropServices.ExternalException
            or PlatformNotSupportedException
            or NotSupportedException
            or DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException;

    private static void LogFailureOnce(ref bool failureLogged, string provider, Exception exception)
    {
        if (failureLogged) return;
        failureLogged = true;
        TADNLog.Log($"PerformanceSnapshotService {provider} provider: {exception}");
    }

    private static int CalculateWaitMilliseconds(
        long samplingTimestamp,
        int sampleIntervalMilliseconds)
    {
        if (samplingTimestamp <= 0) return sampleIntervalMilliseconds;

        long elapsedTicks = Stopwatch.GetTimestamp() - samplingTimestamp;
        long intervalTicks = (long)Math.Ceiling(
            Stopwatch.Frequency * sampleIntervalMilliseconds / 1_000.0);
        if (elapsedTicks >= intervalTicks) return 0;

        double remainingMilliseconds = (intervalTicks - elapsedTicks)
                                       * 1_000.0
                                       / Stopwatch.Frequency;
        return Math.Max(1, (int)Math.Ceiling(remainingMilliseconds));
    }

    public void Dispose()
    {
        bool disposeResourcesSynchronously;
        bool waitForSamplingThread;
        lock (_lifecycleGate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            SnapshotUpdated = null;
            disposeResourcesSynchronously = _started == 0;
            waitForSamplingThread = !disposeResourcesSynchronously
                                    && Thread.CurrentThread != _samplingThread;
            if (!disposeResourcesSynchronously) _refreshWake.Set();
        }

        // A running worker owns provider cleanup so native handles cannot be freed mid-sample
        if (disposeResourcesSynchronously)
        {
            DisposeSamplingResources();
            return;
        }

        if (waitForSamplingThread && !_samplingThread.Join(TimeSpan.FromSeconds(5)))
            TADNLog.Log("PerformanceSnapshotService worker did not stop within five seconds.");
    }

    private void DisposeSamplingResources()
    {
        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0) return;

        lock (_samplingGate)
        {
            try
            {
                _gpuSampler.Dispose();
            }
            catch (Exception exception)
            {
                TADNLog.Log($"PerformanceSnapshotService GPU disposal: {exception}");
            }

            try
            {
                _systemSampler.Dispose();
            }
            catch (Exception exception)
            {
                TADNLog.Log($"PerformanceSnapshotService system disposal: {exception}");
            }

            try
            {
                _metadataReader.Dispose();
            }
            catch (Exception exception)
            {
                TADNLog.Log($"PerformanceSnapshotService metadata disposal: {exception}");
            }

            try
            {
                _refreshWake.Dispose();
            }
            catch (Exception exception)
            {
                TADNLog.Log($"PerformanceSnapshotService wake disposal: {exception}");
            }
        }
    }
}
