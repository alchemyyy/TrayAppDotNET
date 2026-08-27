using System.Diagnostics;
using Avalonia.Threading;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Publishes one-second direct-OS snapshots for the Performance page.</summary>
internal sealed class PerformanceSnapshotService : IDisposable
{
    private const int RefreshIntervalMilliseconds = 1_000;

    private readonly Lock _lifecycleGate = new();
    private readonly Lock _samplingGate = new();
    private readonly AutoResetEvent _refreshWake = new(false);
    private readonly SystemPerformanceSampler _systemSampler = new();
    private readonly SystemPerformanceMetadataReader _metadataReader = new();
    private readonly NetworkPerformanceSampler _networkSampler = new();
    private readonly DiskPerformanceSampler _diskSampler = new();
    private readonly GPUPerformanceSampler _gpuSampler = new();
    private readonly Thread _samplingThread;
    private readonly Action _notifySnapshotAvailable;
    private PerformanceSnapshot _latestSnapshot = PerformanceSnapshot.Empty;
    private int _latestSnapshotGeneration;
    private int _notificationPending;
    private int _resetBaselinesPending;
    private int _samplingActive;
    private int _samplingGeneration;
    private int _started;
    private int _disposed;
    private int _resourcesDisposed;
    private bool _systemFailureLogged;
    private bool _metadataFailureLogged;
    private bool _networkFailureLogged;
    private bool _diskFailureLogged;
    private bool _gpuFailureLogged;

    public PerformanceSnapshotService()
    {
        _notifySnapshotAvailable = NotifySnapshotAvailable;
        _samplingThread = new Thread(SamplingLoop)
        {
            IsBackground = true,
            Name = Constants.ApplicationName + ".PerformanceSampler",
            Priority = ThreadPriority.BelowNormal
        };
    }

    public event Action? SnapshotAvailable;

    /// <summary>Starts the pre-created sampling thread once.</summary>
    public void Start()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_started != 0) return;

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

    /// <summary>Starts or pauses periodic sampling without terminating the page-owned worker.</summary>
    public void SetActive(bool isActive)
    {
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _disposed) != 0) return;

            int activeValue = isActive ? 1 : 0;
            if (Interlocked.Exchange(ref _samplingActive, activeValue) == activeValue) return;
            Interlocked.Increment(ref _samplingGeneration);
            if (isActive) Interlocked.Exchange(ref _resetBaselinesPending, 1);
            if (_started != 0) _refreshWake.Set();
        }
    }

    /// <summary>Wakes the worker for an early refresh without queuing another worker.</summary>
    public void RequestRefresh()
    {
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _disposed) != 0 || _started == 0) return;
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
        }
        Volatile.Write(ref _latestSnapshot, snapshot);
        return snapshot;
    }

    private void SamplingLoop()
    {
        try
        {
            while (Volatile.Read(ref _disposed) == 0)
            {
                if (Volatile.Read(ref _samplingActive) == 0)
                {
                    _refreshWake.WaitOne();
                    continue;
                }

                long samplingTimestamp = 0;
                try
                {
                    PerformanceSnapshot? snapshot = null;
                    int samplingGeneration = Volatile.Read(ref _samplingGeneration);
                    lock (_samplingGate)
                    {
                        if (Volatile.Read(ref _disposed) == 0
                            && Volatile.Read(ref _samplingActive) != 0
                            && samplingGeneration == Volatile.Read(ref _samplingGeneration))
                        {
                            if (Interlocked.Exchange(ref _resetBaselinesPending, 0) != 0)
                                ResetSamplingBaselines();
                            samplingTimestamp = Stopwatch.GetTimestamp();
                            snapshot = CaptureSnapshot(samplingTimestamp);
                        }
                    }

                    if (snapshot != null
                        && Volatile.Read(ref _disposed) == 0
                        && Volatile.Read(ref _samplingActive) != 0
                        && samplingGeneration == Volatile.Read(ref _samplingGeneration))
                    {
                        Publish(snapshot, samplingGeneration);
                    }
                }
                catch (Exception exception)
                {
                    TADNLog.Log($"PerformanceSnapshotService.CaptureSnapshot: {exception}");
                }

                if (Volatile.Read(ref _disposed) != 0) continue;
                int waitMilliseconds = CalculateWaitMilliseconds(samplingTimestamp);
                _refreshWake.WaitOne(waitMilliseconds);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _disposed, 1);
            SnapshotAvailable = null;
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
            metadata.CurrentSpeedHertz,
            metadata.MaximumSpeedHertz,
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

    private void Publish(PerformanceSnapshot snapshot, int samplingGeneration)
    {
        Volatile.Write(ref _latestSnapshot, snapshot);
        Volatile.Write(ref _latestSnapshotGeneration, samplingGeneration);
        if (Interlocked.Exchange(ref _notificationPending, 1) != 0) return;
        Dispatcher.UIThread.Post(_notifySnapshotAvailable, DispatcherPriority.Background);
    }

    private void NotifySnapshotAvailable()
    {
        Interlocked.Exchange(ref _notificationPending, 0);
        if (Volatile.Read(ref _disposed) != 0
            || Volatile.Read(ref _samplingActive) == 0
            || Volatile.Read(ref _latestSnapshotGeneration)
            != Volatile.Read(ref _samplingGeneration))
        {
            return;
        }

        try
        {
            SnapshotAvailable?.Invoke();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"PerformanceSnapshotService.SnapshotAvailable: {exception}");
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

    private static int CalculateWaitMilliseconds(long samplingTimestamp)
    {
        if (samplingTimestamp <= 0) return RefreshIntervalMilliseconds;

        long elapsedTicks = Stopwatch.GetTimestamp() - samplingTimestamp;
        long intervalTicks = Stopwatch.Frequency;
        if (elapsedTicks >= intervalTicks) return 0;

        double remainingMilliseconds = (intervalTicks - elapsedTicks)
                                       * 1_000.0
                                       / Stopwatch.Frequency;
        return Math.Max(1, (int)Math.Ceiling(remainingMilliseconds));
    }

    public void Dispose()
    {
        bool disposeResourcesSynchronously;
        lock (_lifecycleGate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            SnapshotAvailable = null;
            Interlocked.Exchange(ref _samplingActive, 0);
            disposeResourcesSynchronously = _started == 0;
            if (!disposeResourcesSynchronously) _refreshWake.Set();
        }

        // A running worker owns provider cleanup so native handles cannot be freed mid-sample
        if (disposeResourcesSynchronously) DisposeSamplingResources();
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
                _refreshWake.Dispose();
            }
            catch (Exception exception)
            {
                TADNLog.Log($"PerformanceSnapshotService wake disposal: {exception}");
            }
        }
    }
}
