using System.Diagnostics;
using Avalonia.Threading;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Retains and publishes direct-OS performance snapshots for the application lifetime.</summary>
internal sealed class PerformanceSnapshotService : IDisposable
{
    private const int DiskMetadataRefreshSeconds = 30;

    private static readonly long DiskMetadataRefreshIntervalTicks =
        checked(Stopwatch.Frequency * DiskMetadataRefreshSeconds);

    private readonly Lock _lifecycleGate = new();
    private readonly Lock _samplingGate = new();
    private readonly Lock _historyGate = new();
    private readonly AutoResetEvent _refreshWake = new(false);
    private readonly SystemPerformanceSampler _systemSampler = new();
    private readonly SystemPerformanceMetadataReader _metadataReader = new();
    private readonly CPUCCDTopology _cpuCCDTopology = CPUCCDTopologyReader.Read();
    private readonly MemoryCompositionSampler _memoryCompositionSampler = new();
    private readonly PhysicalMemoryMetadataReader _physicalMemoryMetadataReader = new();
    private readonly NetworkPerformanceSampler _networkSampler = new();
    private readonly DiskPerformanceSampler _diskSampler = new();
    private readonly DiskDeviceMetadataReader _diskMetadataReader = new();
    private readonly GPUPerformanceSampler _gpuSampler = new();
    private readonly GPUPerformanceDetailsReader _gpuDetailsReader = new();
    private readonly Thread _samplingThread;
    private readonly Action _notifySnapshotUpdated;
    private PerformanceSnapshot[] _snapshotHistory;
    private DiskDeviceMetadataSnapshot[] _diskMetadata = [];
    private PerformanceSnapshot _latestSnapshot = PerformanceSnapshot.Empty;
    private int _historyStartIndex;
    private int _historyCount;
    private int _sampleIntervalMilliseconds;
    private int _includeMemorySerialNumbers;
    private int _notificationPending;
    private int _refreshRequested;
    private int _resetBaselinesPending;
    private int _started;
    private int _disposed;
    private int _resourcesDisposed;
    private long _nextDiskMetadataRefreshTimestamp;
    private ulong _highestRecordedCPUSpeedHertz;
    private bool _systemFailureLogged;
    private bool _metadataFailureLogged;
    private bool _memoryCompositionFailureLogged;
    private bool _physicalMemoryFailureLogged;
    private bool _networkFailureLogged;
    private bool _diskFailureLogged;
    private bool _diskMetadataFailureLogged;
    private bool _gpuFailureLogged;
    private bool _gpuDetailsFailureLogged;

    public PerformanceSnapshotService()
        : this(
            PerformanceSamplingSettings.DefaultSampleIntervalMilliseconds,
            PerformanceSamplingSettings.CalculateMaximumHistoryCount(
                PerformanceSamplingSettings.DefaultHistoryLengthMinutes,
                PerformanceSamplingSettings.DefaultSampleIntervalMilliseconds),
            includeMemorySerialNumbers: false)
    {
    }

    public PerformanceSnapshotService(
        int sampleIntervalMilliseconds,
        int maximumHistoryCount,
        bool includeMemorySerialNumbers = false)
    {
        ValidateMaximumHistoryCount(maximumHistoryCount);
        _sampleIntervalMilliseconds =
            PerformanceSamplingSettings.NormalizeSampleIntervalMilliseconds(
                sampleIntervalMilliseconds);
        _includeMemorySerialNumbers = includeMemorySerialNumbers ? 1 : 0;
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

            Interlocked.Exchange(ref _resetBaselinesPending, value: 1);
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
            if (_historyCount == 0) return [];

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
            if (matchingCount == 0) return [];

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
    public void UpdateConfiguration(int sampleIntervalMilliseconds, int maximumHistoryCount) =>
        UpdateConfiguration(
            sampleIntervalMilliseconds,
            maximumHistoryCount,
            Volatile.Read(ref _includeMemorySerialNumbers) != 0);

    /// <summary>Applies live sampling, retention, and memory-privacy settings.</summary>
    public void UpdateConfiguration(
        int sampleIntervalMilliseconds,
        int maximumHistoryCount,
        bool includeMemorySerialNumbers)
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
            int serialNumberSetting = includeMemorySerialNumbers ? 1 : 0;
            int previousSerialNumberSetting = Interlocked.Exchange(
                ref _includeMemorySerialNumbers,
                serialNumberSetting);
            if (_started != 0
                && (previousSampleInterval != normalizedSampleInterval
                    || previousSerialNumberSetting != serialNumberSetting))
            {
                Interlocked.Exchange(ref _refreshRequested, value: 1);
                _refreshWake.Set();
            }
        }
    }

    /// <summary>Wakes the worker for an early refresh without queuing another worker.</summary>
    public void RequestRefresh()
    {
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _disposed) != 0 || _started == 0) return;
            Interlocked.Exchange(ref _refreshRequested, value: 1);
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
                bool refreshRequested = Interlocked.Exchange(ref _refreshRequested, value: 0) != 0;
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
                            if (Interlocked.Exchange(ref _resetBaselinesPending, value: 0) != 0)
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
            Interlocked.Exchange(ref _disposed, value: 1);
            SnapshotUpdated = null;
            DisposeSamplingResources();
        }
    }

    private PerformanceSnapshot CaptureSnapshot(long samplingTimestamp)
    {
        SystemPerformanceSample systemSample = SampleSystem();
        SystemPerformanceMetadataSample metadata = SampleMetadata();
        MemoryCompositionSample memoryComposition = SampleMemoryComposition();
        PhysicalMemoryHardwareMetadata physicalMemory = SamplePhysicalMemoryMetadata();
        CPUPerformanceSnapshot cpu = CreateCPUSnapshot(systemSample, metadata);
        MemoryPerformanceSnapshot memory = CreateMemorySnapshot(
            metadata,
            memoryComposition,
            physicalMemory);
        GPUPerformanceSnapshot[] gpus = SampleGPUs();
        NetworkPerformanceSnapshot[] networks = SampleNetworks();
        DiskPerformanceSnapshot[] disks = SampleDisks(samplingTimestamp);
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
        _gpuDetailsReader.Clear();
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
            LogFailureOnce(ref _systemFailureLogged, provider: "system", exception);
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
            LogFailureOnce(ref _metadataFailureLogged, provider: "metadata", exception);
            return default;
        }
    }

    private MemoryCompositionSample SampleMemoryComposition()
    {
        try
        {
            MemoryCompositionSample sample = _memoryCompositionSampler.Sample();
            _memoryCompositionFailureLogged = false;
            return sample;
        }
        catch (Exception exception) when (IsRecoverableProviderException(exception))
        {
            LogFailureOnce(ref _memoryCompositionFailureLogged, provider: "memory composition", exception);
            return default;
        }
    }

    private PhysicalMemoryHardwareMetadata SamplePhysicalMemoryMetadata()
    {
        bool includeSerialNumbers = Volatile.Read(ref _includeMemorySerialNumbers) != 0;
        PhysicalMemoryHardwareMetadata metadata = _physicalMemoryMetadataReader.Get(
            includeSerialNumbers,
            out string? error);
        if (string.IsNullOrWhiteSpace(error))
        {
            _physicalMemoryFailureLogged = false;
            return metadata;
        }

        if (!_physicalMemoryFailureLogged)
        {
            _physicalMemoryFailureLogged = true;
            TADNLog.Log($"PerformanceSnapshotService physical memory provider: {error}");
        }

        return metadata;
    }

    private GPUPerformanceSnapshot[] SampleGPUs()
    {
        try
        {
            GPUPerformanceSnapshot[] snapshots = _gpuSampler.Sample();
            _gpuFailureLogged = false;
            bool hasDetailFailure = false;
            for (int GPUIndex = 0; GPUIndex < snapshots.Length; GPUIndex++)
            {
                GPUPerformanceSnapshot snapshot = snapshots[GPUIndex];
                try
                {
                    GPUPerformanceDetailsSnapshot details = _gpuDetailsReader.Sample(
                        snapshot,
                        out string? error);
                    snapshots[GPUIndex] = snapshot with { Details = details };
                    if (string.IsNullOrWhiteSpace(error)) continue;

                    hasDetailFailure = true;
                    if (!_gpuDetailsFailureLogged)
                    {
                        _gpuDetailsFailureLogged = true;
                        TADNLog.Log($"PerformanceSnapshotService GPU details provider: {error}");
                    }
                }
                catch (Exception exception) when (IsRecoverableProviderException(exception))
                {
                    hasDetailFailure = true;
                    snapshots[GPUIndex] = snapshot with { Details = GPUPerformanceDetailsSnapshot.Empty };
                    LogFailureOnce(ref _gpuDetailsFailureLogged, provider: "GPU details", exception);
                }
            }

            if (!hasDetailFailure) _gpuDetailsFailureLogged = false;
            return snapshots;
        }
        catch (Exception exception) when (IsRecoverableProviderException(exception))
        {
            LogFailureOnce(ref _gpuFailureLogged, provider: "GPU", exception);
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
            LogFailureOnce(ref _networkFailureLogged, provider: "network", exception);
            return [];
        }
    }

    private DiskPerformanceSnapshot[] SampleDisks(long samplingTimestamp)
    {
        DiskPerformanceSnapshot[] snapshots;
        try
        {
            snapshots = _diskSampler.Sample();
            _diskFailureLogged = false;
        }
        catch (Exception exception) when (IsRecoverableProviderException(exception))
        {
            LogFailureOnce(ref _diskFailureLogged, provider: "disk", exception);
            return [];
        }

        DiskDeviceMetadataSnapshot[] metadata = ReadDiskMetadata(samplingTimestamp);
        for (int diskIndex = 0; diskIndex < snapshots.Length; diskIndex++)
        {
            DiskPerformanceSnapshot snapshot = snapshots[diskIndex];
            DiskDeviceMetadataSnapshot matchingMetadata = FindDiskMetadata(
                metadata,
                snapshot.SortKey);
            snapshots[diskIndex] = snapshot with
            {
                Details = DiskPerformanceDetailsFactory.Create(snapshot, matchingMetadata)
            };
        }

        return snapshots;
    }

    private DiskDeviceMetadataSnapshot[] ReadDiskMetadata(long samplingTimestamp)
    {
        if (samplingTimestamp < _nextDiskMetadataRefreshTimestamp)
            return _diskMetadata;

        _nextDiskMetadataRefreshTimestamp = samplingTimestamp
                                            <= long.MaxValue - DiskMetadataRefreshIntervalTicks
            ? samplingTimestamp + DiskMetadataRefreshIntervalTicks
            : long.MaxValue;
        try
        {
            _diskMetadata = _diskMetadataReader.Read();
            _diskMetadataFailureLogged = false;
        }
        catch (Exception exception) when (IsRecoverableProviderException(exception))
        {
            LogFailureOnce(ref _diskMetadataFailureLogged, provider: "disk metadata", exception);
        }

        return _diskMetadata;
    }

    private static DiskDeviceMetadataSnapshot FindDiskMetadata(
        ReadOnlySpan<DiskDeviceMetadataSnapshot> metadata,
        int physicalDiskNumber)
    {
        if (physicalDiskNumber < 0)
            return DiskDeviceMetadataSnapshot.Unavailable(0);

        uint expectedDiskNumber = checked((uint)physicalDiskNumber);
        for (int metadataIndex = 0; metadataIndex < metadata.Length; metadataIndex++)
        {
            if (metadata[metadataIndex].PhysicalDiskNumber == expectedDiskNumber)
                return metadata[metadataIndex];
        }

        return DiskDeviceMetadataSnapshot.Unavailable(expectedDiskNumber);
    }

    private CPUPerformanceSnapshot CreateCPUSnapshot(
        SystemPerformanceSample systemSample,
        SystemPerformanceMetadataSample metadata)
    {
        int logicalProcessorCount = _systemSampler.LastLogicalProcessorCount;
        if (logicalProcessorCount <= 0)
            logicalProcessorCount = Math.Max(val1: 0, metadata.LogicalProcessorCount);

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
            SortKey: 0,
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
            metadata.Uptime) { CCDTopology = _cpuCCDTopology };
    }

    private MemoryPerformanceSnapshot CreateMemorySnapshot(
        SystemPerformanceMetadataSample metadata,
        MemoryCompositionSample compositionSample,
        PhysicalMemoryHardwareMetadata physicalMemory)
    {
        SystemMemoryStatus memoryStatus = _systemSampler.GetLastMemoryStatus();
        NormalizedMemoryComposition composition = MemoryCompositionSampler.Normalize(
            memoryStatus.TotalPhysicalBytes,
            memoryStatus.AvailablePhysicalBytes,
            compositionSample);
        double utilizationPercent = memoryStatus.TotalPhysicalBytes > 0
            ? composition.InUseBytes / (double)memoryStatus.TotalPhysicalBytes * 100.0
            : 0;
        SystemPerformanceInformation information = metadata.PerformanceInformation;
        ulong cachedBytes = composition.HasCompositionData
            ? composition.CachedBytes
            : metadata.HasPerformanceInformation
                ? information.CachedBytes
                : 0;
        ulong hardwareReservedBytes = metadata.InstalledPhysicalMemoryBytes
                                      >= memoryStatus.TotalPhysicalBytes
            ? metadata.InstalledPhysicalMemoryBytes - memoryStatus.TotalPhysicalBytes
            : 0;
        return new MemoryPerformanceSnapshot(
            MemoryPerformanceSnapshot.StableDeviceID,
            PerformanceDeviceKind.Memory,
            SortKey: 0,
            _systemSampler.LastMemorySampleAvailable,
            utilizationPercent,
            memoryStatus.TotalPhysicalBytes,
            composition.AvailableBytes,
            composition.InUseBytes,
            metadata.InstalledPhysicalMemoryBytes,
            metadata.HasPerformanceInformation ? information.CommittedBytes : 0,
            metadata.HasPerformanceInformation ? information.CommitLimitBytes : 0,
            cachedBytes,
            metadata.HasPerformanceInformation ? information.PagedPoolBytes : 0,
            metadata.HasPerformanceInformation ? information.NonPagedPoolBytes : 0,
            hardwareReservedBytes,
            new MemoryCompositionSnapshot(
                composition.HasCompositionData,
                composition.ModifiedBytes,
                composition.StandbyBytes,
                composition.FreeBytes,
                composition.HasCompressionData,
                composition.CompressedBytes,
                composition.EstimatedDataBytes,
                composition.SavedBytes),
            new PhysicalMemoryHardwareSnapshot(
                physicalMemory.SpeedMegatransfersPerSecond,
                physicalMemory.UsedSlotCount,
                physicalMemory.TotalSlotCount,
                physicalMemory.FormFactor,
                physicalMemory.Modules));
    }

    private void Publish(PerformanceSnapshot snapshot)
    {
        StoreSnapshot(snapshot);
        if (SnapshotUpdated == null) return;
        if (Interlocked.Exchange(ref _notificationPending, value: 1) != 0) return;
        Dispatcher.UIThread.Post(_notifySnapshotUpdated, DispatcherPriority.Background);
    }

    private void NotifySnapshotUpdated()
    {
        Interlocked.Exchange(ref _notificationPending, value: 0);
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
        return Math.Max(val1: 1, (int)Math.Ceiling(remainingMilliseconds));
    }

    public void Dispose()
    {
        bool disposeResourcesSynchronously;
        bool waitForSamplingThread;
        lock (_lifecycleGate)
        {
            if (Interlocked.Exchange(ref _disposed, value: 1) != 0) return;

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
        if (Interlocked.Exchange(ref _resourcesDisposed, value: 1) != 0) return;

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
                _memoryCompositionSampler.Dispose();
            }
            catch (Exception exception)
            {
                TADNLog.Log($"PerformanceSnapshotService memory composition disposal: {exception}");
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
