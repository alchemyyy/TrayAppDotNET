using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;
using TaskManagerTrayAppDotNET.UI;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Samples only the active Details schema and publishes compact right-sized snapshots.</summary>
internal sealed class ProcessSnapshotService : IDisposable
{
    public const int MaximumProcessCount = 8_192;
    private const int RefreshIntervalMilliseconds = 1_000;
    private const int ShutdownJoinTimeoutMilliseconds = 2_000;
    private const int InitialProcessPathCapacity = 1_024;
    private const int MaximumProcessPathCapacity = 32_767;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVMRead = 0x0010;

    private static readonly ulong ProcessorColumnsMask = ColumnMask(ProcessTableColumnKind.CPU)
                                                        | ColumnMask(ProcessTableColumnKind.CPUTime);
    private static readonly ulong ThreadColumnsMask = ColumnMask(ProcessTableColumnKind.Status)
                                                     | ColumnMask(ProcessTableColumnKind.Threads);
    private static readonly ulong MemoryColumnsMask = ColumnMask(ProcessTableColumnKind.WorkingSet)
                                                     | ColumnMask(ProcessTableColumnKind.PeakWorkingSet)
                                                     | ColumnMask(ProcessTableColumnKind.WorkingSetDelta)
                                                     | ColumnMask(ProcessTableColumnKind.ActivePrivateWorkingSet)
                                                     | ColumnMask(ProcessTableColumnKind.PrivateMemory)
                                                     | ColumnMask(ProcessTableColumnKind.SharedWorkingSet)
                                                     | ColumnMask(ProcessTableColumnKind.CommitSize)
                                                     | ColumnMask(ProcessTableColumnKind.PagedPool)
                                                     | ColumnMask(ProcessTableColumnKind.NonPagedPool)
                                                     | ColumnMask(ProcessTableColumnKind.PageFaults)
                                                     | ColumnMask(ProcessTableColumnKind.PageFaultDelta);
    private static readonly ulong IOColumnsMask = ColumnMask(ProcessTableColumnKind.IOReads)
                                                 | ColumnMask(ProcessTableColumnKind.IOWrites)
                                                 | ColumnMask(ProcessTableColumnKind.IOOther)
                                                 | ColumnMask(ProcessTableColumnKind.IOReadBytes)
                                                 | ColumnMask(ProcessTableColumnKind.IOWriteBytes)
                                                 | ColumnMask(ProcessTableColumnKind.IOOtherBytes);
    private static readonly ulong GPUColumnsMask = ColumnMask(ProcessTableColumnKind.GPU)
                                                  | ColumnMask(ProcessTableColumnKind.GPUEngine)
                                                  | ColumnMask(ProcessTableColumnKind.DedicatedGPUMemory)
                                                  | ColumnMask(ProcessTableColumnKind.SharedGPUMemory);
    private static readonly ulong NPUColumnsMask = ColumnMask(ProcessTableColumnKind.NPU)
                                                  | ColumnMask(ProcessTableColumnKind.NPUEngine)
                                                  | ColumnMask(ProcessTableColumnKind.DedicatedNPUMemory)
                                                  | ColumnMask(ProcessTableColumnKind.SharedNPUMemory);
    private static readonly ulong ProcessHandleStaticColumnsMask =
        ColumnMask(ProcessTableColumnKind.Name)
        | ColumnMask(ProcessTableColumnKind.UserName)
        | ColumnMask(ProcessTableColumnKind.ImagePath)
        | ColumnMask(ProcessTableColumnKind.CommandLine)
        | ColumnMask(ProcessTableColumnKind.Platform)
        | ColumnMask(ProcessTableColumnKind.Elevated)
        | ColumnMask(ProcessTableColumnKind.Description)
        | ColumnMask(ProcessTableColumnKind.DataExecutionPrevention)
        | ColumnMask(ProcessTableColumnKind.PackageName)
        | ColumnMask(ProcessTableColumnKind.Architecture)
        | ColumnMask(ProcessTableColumnKind.HardwareStackProtection)
        | ColumnMask(ProcessTableColumnKind.ExtendedControlFlowGuard)
        | ColumnMask(ProcessTableColumnKind.Isolation);
    private static readonly ulong ProcessHandleDynamicColumnsMask =
        ColumnMask(ProcessTableColumnKind.UserObjects)
        | ColumnMask(ProcessTableColumnKind.GDIObjects)
        | ColumnMask(ProcessTableColumnKind.UACVirtualization)
        | ColumnMask(ProcessTableColumnKind.IOPriority)
        | ColumnMask(ProcessTableColumnKind.PowerThrottling)
        | ColumnMask(ProcessTableColumnKind.DPIAwareness);

    private readonly Lock _publishGate = new();
    private readonly Lock _samplingPolicyGate = new();
    private readonly AutoResetEvent _refreshWake = new(false);
    private readonly Thread _samplingThread;
    private readonly SystemProcessSnapshot _systemProcessSnapshot = new();
    private readonly SystemPerformanceSampler _systemPerformanceSampler = new();
    private readonly Action _notifySnapshotAvailable;
    private readonly Dictionary<int, ProcessHistoryEntry> _history = new(1_024);
    private readonly Dictionary<string, ProcessImageIdentity> _imageIdentities =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SharedUserName> _sharedUserNames =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, SystemProcessData> _systemProcessData = new(1_024);
    private readonly List<int> _staleProcessIDs = new(256);
    private readonly StringBuilder _processPathBuffer = new(InitialProcessPathCapacity);
    private readonly StringBuilder _applicationUserModelIDBuffer = new(IconExtraction.MAX_AUMID_LEN);
    private AcceleratorPerformanceSampler? _acceleratorSampler;
    private EnterpriseContextReader? _enterpriseContextReader;
    private ProcessNetworkUsageSampler? _networkUsageSampler;
    private ProcessSnapshotBuffer _publishedBuffer = new();
    private ProcessSnapshotBuffer _stagingBuffer = new();
    private SystemPerformanceSample _latestSystemPerformanceSample = SystemPerformanceSample.Empty;
    private ProcessDataSchema _activeSchema = ProcessDataSchema.Create([]);
    private int[] _warmProcessIDs = [];
    private int[] _sampleWarmProcessIDs = [];
    private int _warmProcessCount;
    private long _publishedVersion;
    private ulong _historySchemaMask = ulong.MaxValue;
    private ulong _acceleratorSamplerMask;
    private double _nominalProcessorCycleCapacity;
    private int _historyGeneration;
    private int _notificationPending;
    private int _started;
    private int _disposed;
    private bool _sampleEveryProcess;
    private bool _acceleratorSamplesEveryProcess;
    private bool _capacityWarningLogged;

    public ProcessSnapshotService()
    {
        _notifySnapshotAvailable = NotifySnapshotAvailable;
        _samplingThread = new Thread(SamplingLoop)
        {
            IsBackground = true,
            Name = Constants.ApplicationName + ".ProcessSampler",
            Priority = ThreadPriority.BelowNormal
        };
    }

    public event Action? SnapshotAvailable;

    /// <summary>Replaces the active storage schema; inactive columns are discarded on the next sample.</summary>
    public void SetActiveSchema(ProcessDataSchema schema)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(schema);

        bool changed;
        lock (_samplingPolicyGate)
        {
            changed = _activeSchema.VisibleMask != schema.VisibleMask;
            _activeSchema = schema;
            if (changed)
            {
                _warmProcessCount = 0;
                _sampleEveryProcess = false;
            }
        }

        if (changed) RequestRefresh();
    }

    /// <summary>Publishes the viewport's warm process set without allocating per scroll.</summary>
    public void SetWarmProcesses(
        ulong schemaMask,
        int[] processIDs,
        int count,
        bool sampleEveryProcess)
    {
        ArgumentNullException.ThrowIfNull(processIDs);
        if ((uint)count > (uint)processIDs.Length || count > MaximumProcessCount)
            throw new ArgumentOutOfRangeException(nameof(count));

        bool wakeSampler;
        lock (_samplingPolicyGate)
        {
            if (_activeSchema.VisibleMask != schemaMask) return;

            EnsurePolicyCapacity(ref _warmProcessIDs, count);
            wakeSampler = sampleEveryProcess && !_sampleEveryProcess;
            Array.Copy(processIDs, _warmProcessIDs, count);
            _warmProcessCount = count;
            _sampleEveryProcess = sampleEveryProcess;
        }

        if (wakeSampler) RequestRefresh();
    }

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

    /// <summary>Copies the latest compatible published snapshot into caller-owned storage.</summary>
    public int CopyLatest(
        ProcessSnapshotBuffer destination,
        ulong expectedSchemaMask,
        out long version)
    {
        _ = TryCopyLatest(
            destination,
            expectedSchemaMask,
            out int count,
            out version);
        return count;
    }

    /// <summary>Copies the latest snapshot only when its schema matches the caller.</summary>
    public bool TryCopyLatest(
        ProcessSnapshotBuffer destination,
        ulong expectedSchemaMask,
        out int count,
        out long version)
    {
        ArgumentNullException.ThrowIfNull(destination);
        lock (_publishGate)
        {
            version = _publishedVersion;
            if (_publishedBuffer.Schema?.VisibleMask != expectedSchemaMask)
            {
                count = 0;
                return false;
            }

            destination.CopyFrom(_publishedBuffer);
            count = destination.Count;
            return true;
        }
    }

    /// <summary>Copies the latest snapshot when it contains every requested column.</summary>
    public bool TryCopyLatestContaining(
        ProcessSnapshotBuffer destination,
        ulong requiredSchemaMask,
        out int count,
        out long version)
    {
        ArgumentNullException.ThrowIfNull(destination);
        lock (_publishGate)
        {
            version = _publishedVersion;
            ProcessDataSchema? schema = _publishedBuffer.Schema;
            if (schema == null || (schema.VisibleMask & requiredSchemaMask) != requiredSchemaMask)
            {
                count = 0;
                return false;
            }

            destination.CopyFrom(_publishedBuffer);
            count = destination.Count;
            return true;
        }
    }

    /// <summary>Returns the system-performance sample published with the latest process snapshot.</summary>
    public SystemPerformanceSample GetLatestSystemPerformanceSample()
    {
        lock (_publishGate)
            return _latestSystemPerformanceSample;
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
        long sampleTimeTicks = DateTime.UtcNow.ToFileTimeUtc();
        SystemPerformanceSample systemPerformanceSample = _systemPerformanceSampler.Sample();
        CopySamplingPolicy(
            out ProcessDataSchema schema,
            out int warmProcessCount,
            out bool sampleEveryProcess);
        bool schemaChanged = _historySchemaMask != schema.VisibleMask;
        if (schemaChanged)
            ResetHistoryForSchema(schema.VisibleMask);

        ConfigureOptionalCollectors(schema);
        if (schema.VisibleMask == 0)
        {
            // The tray graph needs the system sample, but hidden/non-process pages do not need a process walk
            _stagingBuffer.BeginWrite(schema, 0);
            _stagingBuffer.CompleteWrite(0);
            Publish(systemPerformanceSample);
            return;
        }

        _enterpriseContextReader?.BeginSample();
        _acceleratorSamplesEveryProcess = schemaChanged || sampleEveryProcess;
        _acceleratorSampler?.Sample(
            _sampleWarmProcessIDs,
            warmProcessCount,
            _acceleratorSamplesEveryProcess);

        bool hasSystemSnapshot = _systemProcessSnapshot.TryCapture(
            _systemProcessData,
            schema.IsVisible(ProcessTableColumnKind.JobObjectID));
        int generation = NextHistoryGeneration();
        int count = hasSystemSnapshot
            ? RefreshFromSystemSnapshot(
                schema,
                warmProcessCount,
                sampleEveryProcess,
                sampleTimestamp,
                sampleTimeTicks,
                generation)
            : RefreshFromProcessObjects(
                schema,
                warmProcessCount,
                sampleEveryProcess,
                sampleTimestamp,
                sampleTimeTicks,
                generation);

        _stagingBuffer.CompleteWrite(count);
        RemoveStaleHistory(generation);
        Publish(systemPerformanceSample);
    }

    private int RefreshFromSystemSnapshot(
        ProcessDataSchema schema,
        int warmProcessCount,
        bool sampleEveryProcess,
        long sampleTimestamp,
        long sampleTimeTicks,
        int generation)
    {
        int requestedCapacity = Math.Min(_systemProcessData.Count, MaximumProcessCount);
        _stagingBuffer.BeginWrite(schema, requestedCapacity);
        int count = 0;
        foreach (KeyValuePair<int, SystemProcessData> pair in _systemProcessData)
        {
            if (count >= MaximumProcessCount)
            {
                LogCapacityWarningOnce(_systemProcessData.Count);
                break;
            }

            if (SampleAndStoreProcess(
                    null,
                    pair.Key,
                    true,
                    pair.Value,
                    schema,
                    warmProcessCount,
                    sampleEveryProcess,
                    sampleTimestamp,
                    sampleTimeTicks,
                    generation,
                    count))
            {
                count++;
            }
        }

        return count;
    }

    private int RefreshFromProcessObjects(
        ProcessDataSchema schema,
        int warmProcessCount,
        bool sampleEveryProcess,
        long sampleTimestamp,
        long sampleTimeTicks,
        int generation)
    {
        Process[] processes = Process.GetProcesses();
        int requestedCapacity = Math.Min(processes.Length, MaximumProcessCount);
        _stagingBuffer.BeginWrite(schema, requestedCapacity);
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
                    break;
                }

                int processID = ReadProcessID(process);
                if (processID < 0) continue;
                if (SampleAndStoreProcess(
                        process,
                        processID,
                        false,
                        default,
                        schema,
                        warmProcessCount,
                        sampleEveryProcess,
                        sampleTimestamp,
                        sampleTimeTicks,
                        generation,
                        count))
                {
                    count++;
                }
            }
        }
        finally
        {
            for (int processIndex = processedProcessCount; processIndex < processes.Length; processIndex++)
                processes[processIndex].Dispose();
        }

        return count;
    }

    private bool SampleAndStoreProcess(
        Process? process,
        int processID,
        bool hasSystemProcessData,
        SystemProcessData systemProcessData,
        ProcessDataSchema schema,
        int warmProcessCount,
        bool sampleEveryProcess,
        long sampleTimestamp,
        long sampleTimeTicks,
        int generation,
        int rowIndex)
    {
        if (processID < 0) return false;

        bool isWarm = sampleEveryProcess
                      || Array.BinarySearch(
                          _sampleWarmProcessIDs,
                          0,
                          warmProcessCount,
                          processID) >= 0;
        bool historyMatches = _history.TryGetValue(processID, out ProcessHistoryEntry? existingHistory)
                              && (!hasSystemProcessData
                                  || existingHistory.StaticData.InstanceKey.CreationTimeTicks
                                  == systemProcessData.CreationTimeTicks);
        bool sampleDynamicValues = !historyMatches || isWarm;
        bool requiresProcessHandle = !hasSystemProcessData
                                     || (!historyMatches
                                         && HasAnyColumn(
                                             schema.VisibleMask,
                                             ProcessHandleStaticColumnsMask))
                                     || (sampleDynamicValues
                                         && HasAnyColumn(
                                             schema.VisibleMask,
                                             ProcessHandleDynamicColumnsMask));
        IntPtr processHandle = requiresProcessHandle ? OpenQueryHandle(processID) : IntPtr.Zero;
        try
        {
            ProcessHistoryEntry history = ResolveHistory(
                process,
                processHandle,
                processID,
                sampleTimeTicks,
                hasSystemProcessData,
                systemProcessData,
                schema,
                generation);
            if (!history.HasDynamicSample || isWarm)
            {
                SampleDynamicValues(
                    process,
                    processHandle,
                    history,
                    hasSystemProcessData,
                    systemProcessData,
                    sampleTimestamp,
                    sampleTimeTicks,
                    schema,
                    isWarm);
            }

            history.LastSeenGeneration = generation;
            _stagingBuffer.SetRow(
                rowIndex,
                history.StaticData,
                history.DynamicNumericValues,
                history.DynamicTextValues);
            return true;
        }
        finally
        {
            if (processHandle != IntPtr.Zero) Kernel32.CloseHandle(processHandle);
        }
    }

    private void CopySamplingPolicy(
        out ProcessDataSchema schema,
        out int warmProcessCount,
        out bool sampleEveryProcess)
    {
        lock (_samplingPolicyGate)
        {
            schema = _activeSchema;
            warmProcessCount = _warmProcessCount;
            sampleEveryProcess = _sampleEveryProcess;
            EnsurePolicyCapacity(ref _sampleWarmProcessIDs, warmProcessCount);
            Array.Copy(_warmProcessIDs, _sampleWarmProcessIDs, warmProcessCount);
        }

        Array.Sort(_sampleWarmProcessIDs, 0, warmProcessCount);
    }

    private void ConfigureOptionalCollectors(ProcessDataSchema schema)
    {
        ulong acceleratorMask = schema.VisibleMask & (GPUColumnsMask | NPUColumnsMask);
        if (_acceleratorSamplerMask != acceleratorMask)
        {
            _acceleratorSampler?.Dispose();
            _acceleratorSampler = null;
            _acceleratorSamplerMask = acceleratorMask;
            if (acceleratorMask != 0)
            {
                bool needsGPU = HasAnyColumn(acceleratorMask, GPUColumnsMask);
                bool needsNPU = HasAnyColumn(acceleratorMask, NPUColumnsMask);
                bool needsUtilization = schema.IsVisible(ProcessTableColumnKind.GPU)
                                        || schema.IsVisible(ProcessTableColumnKind.NPU);
                bool needsDedicatedMemory = schema.IsVisible(ProcessTableColumnKind.DedicatedGPUMemory)
                                            || schema.IsVisible(ProcessTableColumnKind.DedicatedNPUMemory);
                bool needsSharedMemory = schema.IsVisible(ProcessTableColumnKind.SharedGPUMemory)
                                         || schema.IsVisible(ProcessTableColumnKind.SharedNPUMemory);
                _acceleratorSampler = new AcceleratorPerformanceSampler(
                    needsGPU,
                    needsNPU,
                    needsUtilization,
                    schema.IsVisible(ProcessTableColumnKind.GPUEngine),
                    schema.IsVisible(ProcessTableColumnKind.NPUEngine),
                    needsDedicatedMemory,
                    needsSharedMemory);
            }
        }

        bool needsEnterpriseContext = schema.IsVisible(ProcessTableColumnKind.EnterpriseContext);
        if (needsEnterpriseContext && _enterpriseContextReader == null)
            _enterpriseContextReader = new EnterpriseContextReader();
        else if (!needsEnterpriseContext && _enterpriseContextReader != null)
        {
            _enterpriseContextReader.Dispose();
            _enterpriseContextReader = null;
        }

        if (schema.IsVisible(ProcessTableColumnKind.CPUUtility))
        {
            if (_nominalProcessorCycleCapacity <= 0)
                _nominalProcessorCycleCapacity = NativeProcessInfo.ReadNominalProcessorCycleCapacity();
        }
        else
        {
            _nominalProcessorCycleCapacity = 0;
        }

        bool needsNetworkUsage = schema.IsVisible(ProcessTableColumnKind.Network);
        if (needsNetworkUsage && _networkUsageSampler == null)
            _networkUsageSampler = new ProcessNetworkUsageSampler();
        else if (!needsNetworkUsage && _networkUsageSampler != null)
        {
            _networkUsageSampler.Dispose();
            _networkUsageSampler = null;
        }
    }

    private ProcessHistoryEntry ResolveHistory(
        Process? process,
        IntPtr processHandle,
        int processID,
        long sampleTimeTicks,
        bool hasSystemProcessData,
        SystemProcessData systemProcessData,
        ProcessDataSchema schema,
        int generation)
    {
        bool hadHistory = _history.TryGetValue(processID, out ProcessHistoryEntry? history);
        long fallbackCreationTime = hadHistory
            ? history!.StaticData.InstanceKey.CreationTimeTicks
            : sampleTimeTicks;
        long creationTime = hasSystemProcessData
            ? systemProcessData.CreationTimeTicks
            : processHandle == IntPtr.Zero
                ? fallbackCreationTime
                : NativeProcessInfo.ReadCreationTimeTicks(processHandle, fallbackCreationTime);
        if (hadHistory && history!.StaticData.InstanceKey.CreationTimeTicks == creationTime)
        {
            history.LastSeenGeneration = generation;
            return history;
        }

        if (hadHistory)
        {
            ReleaseImageIdentity(history!.StaticData.Image);
            ReleaseUserName(history.StaticData.UserName);
            _history.Remove(processID);
        }

        ProcessStaticData staticData = CreateStaticData(
            process,
            processHandle,
            processID,
            creationTime,
            hasSystemProcessData,
            systemProcessData,
            schema);
        history = new ProcessHistoryEntry
        {
            StaticData = staticData,
            DynamicNumericValues = schema.DynamicNumericCount == 0
                ? []
                : new long[schema.DynamicNumericCount],
            DynamicTextValues = schema.DynamicTextCount == 0
                ? []
                : new string?[schema.DynamicTextCount],
            LastSeenGeneration = generation
        };
        _history.Add(processID, history);
        return history;
    }

    private ProcessStaticData CreateStaticData(
        Process? process,
        IntPtr processHandle,
        int processID,
        long creationTime,
        bool hasSystemProcessData,
        SystemProcessData systemProcessData,
        ProcessDataSchema schema)
    {
        bool needsIcon = schema.IsVisible(ProcessTableColumnKind.Name);
        bool needsProcessName = needsIcon;
        string processName = !needsProcessName
            ? string.Empty
            : hasSystemProcessData
                ? NormalizeProcessName(_systemProcessSnapshot.ReadImageName(systemProcessData), processID)
                : process == null
                    ? NormalizeProcessName(string.Empty, processID)
                    : ReadProcessName(process, processID);
        bool needsImagePath = needsIcon
                              || schema.IsVisible(ProcessTableColumnKind.ImagePath)
                              || schema.IsVisible(ProcessTableColumnKind.Description);
        string imagePath = processHandle == IntPtr.Zero || !needsImagePath
            ? string.Empty
            : ReadExecutablePath(processHandle);
        ProcessImageIdentity image = AcquireImageIdentity(
            processName,
            imagePath,
            processHandle,
            needsIcon,
            schema.IsVisible(ProcessTableColumnKind.Description));
        bool needsUserName = schema.IsVisible(ProcessTableColumnKind.UserName);
        string userName = !needsUserName
            ? string.Empty
            : processHandle == IntPtr.Zero
                ? NativeProcessInfo.Unavailable
                : NativeProcessInfo.ReadUserName(processHandle);
        long[] numericValues = schema.StaticNumericCount == 0
            ? []
            : new long[schema.StaticNumericCount];
        string?[] textValues = schema.StaticTextCount == 0
            ? []
            : new string?[schema.StaticTextCount];

        if (schema.IsVisible(ProcessTableColumnKind.SessionID))
        {
            int sessionID = hasSystemProcessData
                ? systemProcessData.SessionID
                : process == null ? -1 : ReadSessionID(process);
            SetStaticNumeric(schema, numericValues, ProcessTableColumnKind.SessionID, sessionID);
        }
        if (schema.IsVisible(ProcessTableColumnKind.CommandLine))
        {
            string commandLine = processHandle == IntPtr.Zero
                ? string.Empty
                : NativeProcessInfo.ReadCommandLine(processHandle);
            SetStaticText(schema, textValues, ProcessTableColumnKind.CommandLine, commandLine);
        }
        SetStaticCode(
            schema,
            numericValues,
            ProcessTableColumnKind.OperatingSystemContext,
            ProcessDisplayCode.Windows);

        bool needsArchitecture = schema.IsVisible(ProcessTableColumnKind.Platform)
                                 || schema.IsVisible(ProcessTableColumnKind.Architecture);
        ProcessDisplayCode architecture = needsArchitecture && processHandle != IntPtr.Zero
            ? NativeProcessInfo.ReadArchitecture(processHandle)
            : ProcessDisplayCode.Unavailable;
        SetStaticCode(
            schema,
            numericValues,
            ProcessTableColumnKind.Platform,
            NativeProcessInfo.GetPlatform(architecture));
        if (schema.IsVisible(ProcessTableColumnKind.Elevated))
        {
            SetStaticCode(
                schema,
                numericValues,
                ProcessTableColumnKind.Elevated,
                processHandle == IntPtr.Zero
                    ? ProcessDisplayCode.Unavailable
                    : NativeProcessInfo.ReadElevation(processHandle));
        }

        if (schema.IsVisible(ProcessTableColumnKind.DataExecutionPrevention))
        {
            SetStaticCode(
                schema,
                numericValues,
                ProcessTableColumnKind.DataExecutionPrevention,
                processHandle == IntPtr.Zero
                    ? ProcessDisplayCode.Unavailable
                    : NativeProcessInfo.ReadDataExecutionPrevention(processHandle));
        }

        bool needsPackageName = schema.IsVisible(ProcessTableColumnKind.PackageName)
                                || schema.IsVisible(ProcessTableColumnKind.Isolation);
        string packageName = needsPackageName && processHandle != IntPtr.Zero
            ? NativeProcessInfo.ReadPackageName(processHandle)
            : string.Empty;
        SetStaticText(schema, textValues, ProcessTableColumnKind.PackageName, packageName);
        SetStaticCode(schema, numericValues, ProcessTableColumnKind.Architecture, architecture);

        if (schema.IsVisible(ProcessTableColumnKind.HardwareStackProtection))
        {
            SetStaticCode(
                schema,
                numericValues,
                ProcessTableColumnKind.HardwareStackProtection,
                processHandle == IntPtr.Zero
                    ? ProcessDisplayCode.Unavailable
                    : NativeProcessInfo.ReadHardwareStackProtection(processHandle));
        }
        if (schema.IsVisible(ProcessTableColumnKind.ExtendedControlFlowGuard))
        {
            SetStaticCode(
                schema,
                numericValues,
                ProcessTableColumnKind.ExtendedControlFlowGuard,
                processHandle == IntPtr.Zero
                    ? ProcessDisplayCode.Unavailable
                    : NativeProcessInfo.ReadExtendedControlFlowGuard(processHandle));
        }
        if (schema.IsVisible(ProcessTableColumnKind.Isolation))
        {
            SetStaticCode(
                schema,
                numericValues,
                ProcessTableColumnKind.Isolation,
                processHandle == IntPtr.Zero
                    ? ProcessDisplayCode.Unavailable
                    : NativeProcessInfo.ReadIsolation(processHandle, packageName.Length > 0));
        }

        return new ProcessStaticData
        {
            InstanceKey = new ProcessInstanceKey(processID, creationTime),
            ParentProcessID = hasSystemProcessData ? systemProcessData.ParentProcessID : -1,
            Image = image,
            UserName = needsUserName ? AcquireUserName(userName) : string.Empty,
            NumericValues = numericValues,
            TextValues = textValues
        };
    }

    private void SampleDynamicValues(
        Process? process,
        IntPtr processHandle,
        ProcessHistoryEntry history,
        bool hasSystemProcessData,
        SystemProcessData systemProcessData,
        long sampleTimestamp,
        long sampleTimeTicks,
        ProcessDataSchema schema,
        bool isWarm)
    {
        ulong activeMask = schema.VisibleMask;
        if (schema.IsVisible(ProcessTableColumnKind.Lifetime))
        {
            long lifetimeTicks = ProcessLifetime.CalculateTicks(
                history.StaticData.InstanceKey.CreationTimeTicks,
                sampleTimeTicks);
            SetDynamicNumeric(schema, history, ProcessTableColumnKind.Lifetime, lifetimeTicks);
        }

        if (HasAnyColumn(activeMask, ProcessorColumnsMask))
        {
            long totalProcessorTicks = hasSystemProcessData
                ? systemProcessData.TotalProcessorTicks
                : ReadTotalProcessorTicks(process);
            double cpuPercent = CalculateCPUPercent(history, totalProcessorTicks, sampleTimestamp);
            SetDynamicDouble(schema, history, ProcessTableColumnKind.CPU, cpuPercent);
            SetDynamicNumeric(schema, history, ProcessTableColumnKind.CPUTime, totalProcessorTicks);
            history.TotalProcessorTicks = totalProcessorTicks;
            history.LastProcessorSampleTimestamp = sampleTimestamp;
            history.HasProcessorSample = true;
        }

        bool needsCycleCount = schema.IsVisible(ProcessTableColumnKind.Cycle)
                               || schema.IsVisible(ProcessTableColumnKind.CPUUtility);
        if (needsCycleCount)
        {
            ulong cycles = hasSystemProcessData
                ? systemProcessData.CycleCount
                : processHandle == IntPtr.Zero ? 0 : NativeProcessInfo.ReadCycleCount(processHandle);
            SetDynamicNumeric(schema, history, ProcessTableColumnKind.Cycle, unchecked((long)cycles));
            if (schema.IsVisible(ProcessTableColumnKind.CPUUtility))
            {
                double utility = CalculateCPUUtility(
                    history,
                    cycles,
                    sampleTimestamp,
                    _nominalProcessorCycleCapacity);
                SetDynamicDouble(schema, history, ProcessTableColumnKind.CPUUtility, utility);
            }

            history.CycleCount = cycles;
            history.LastCycleSampleTimestamp = sampleTimestamp;
            history.HasCycleSample = true;
        }

        if (schema.IsVisible(ProcessTableColumnKind.JobObjectID))
        {
            long jobObjectID = hasSystemProcessData && _systemProcessSnapshot.HasJobObjectIDs
                ? systemProcessData.JobObjectID
                : -1;
            SetDynamicNumeric(schema, history, ProcessTableColumnKind.JobObjectID, jobObjectID);
        }

        if (schema.IsVisible(ProcessTableColumnKind.EnterpriseContext))
        {
            string enterpriseContext = _enterpriseContextReader?.Read(history.StaticData.ProcessID)
                                       ?? EnterpriseContextReader.NotApplicable;
            SetDynamicText(schema, history, ProcessTableColumnKind.EnterpriseContext, enterpriseContext);
        }

        if (HasAnyColumn(activeMask, MemoryColumnsMask))
        {
            NativeProcessInfo.ProcessMemoryCounters memory = hasSystemProcessData
                ? new NativeProcessInfo.ProcessMemoryCounters(
                    systemProcessData.WorkingSetBytes,
                    systemProcessData.PeakWorkingSetBytes,
                    systemProcessData.PrivateWorkingSetBytes,
                    Math.Max(0, systemProcessData.WorkingSetBytes - systemProcessData.PrivateWorkingSetBytes),
                    systemProcessData.CommitSizeBytes,
                    systemProcessData.PagedPoolBytes,
                    systemProcessData.NonPagedPoolBytes,
                    systemProcessData.PageFaultCount)
                : ReadMemoryCounters(process, processHandle);
            bool hasRecentMemorySample = history.HasMemorySample
                                         && sampleTimestamp >= history.LastMemorySampleTimestamp
                                         && sampleTimestamp - history.LastMemorySampleTimestamp
                                         <= Stopwatch.Frequency * 3;
            SetDynamicNumeric(schema, history, ProcessTableColumnKind.WorkingSet, memory.WorkingSetBytes);
            SetDynamicNumeric(schema, history, ProcessTableColumnKind.PeakWorkingSet, memory.PeakWorkingSetBytes);
            SetDynamicNumeric(
                schema,
                history,
                ProcessTableColumnKind.WorkingSetDelta,
                hasRecentMemorySample ? memory.WorkingSetBytes - history.WorkingSetBytes : 0);
            SetDynamicNumeric(
                schema,
                history,
                ProcessTableColumnKind.ActivePrivateWorkingSet,
                memory.PrivateWorkingSetBytes);
            SetDynamicNumeric(schema, history, ProcessTableColumnKind.PrivateMemory, memory.PrivateWorkingSetBytes);
            SetDynamicNumeric(schema, history, ProcessTableColumnKind.SharedWorkingSet, memory.SharedWorkingSetBytes);
            SetDynamicNumeric(schema, history, ProcessTableColumnKind.CommitSize, memory.CommitSizeBytes);
            SetDynamicNumeric(schema, history, ProcessTableColumnKind.PagedPool, memory.PagedPoolBytes);
            SetDynamicNumeric(schema, history, ProcessTableColumnKind.NonPagedPool, memory.NonPagedPoolBytes);
            SetDynamicNumeric(schema, history, ProcessTableColumnKind.PageFaults, memory.PageFaultCount);
            SetDynamicNumeric(
                schema,
                history,
                ProcessTableColumnKind.PageFaultDelta,
                hasRecentMemorySample ? memory.PageFaultCount - history.PageFaultCount : 0);
            history.WorkingSetBytes = memory.WorkingSetBytes;
            history.PageFaultCount = memory.PageFaultCount;
            history.LastMemorySampleTimestamp = sampleTimestamp;
            history.HasMemorySample = true;
        }

        if (schema.IsVisible(ProcessTableColumnKind.Disk))
        {
            double bytesPerSecond = -1;
            if (hasSystemProcessData && systemProcessData.HasDiskCounters)
            {
                bytesPerSecond = CalculateTransferRate(
                    history.HasDiskSample,
                    history.DiskBytes,
                    history.LastDiskSampleTimestamp,
                    systemProcessData.DiskBytes,
                    sampleTimestamp);
                history.DiskBytes = systemProcessData.DiskBytes;
                history.LastDiskSampleTimestamp = sampleTimestamp;
                history.HasDiskSample = true;
            }
            else
            {
                history.HasDiskSample = false;
            }

            SetDynamicDouble(schema, history, ProcessTableColumnKind.Disk, bytesPerSecond);
        }

        if (schema.IsVisible(ProcessTableColumnKind.Network))
        {
            if (_networkUsageSampler?.TryReadSample(
                    history.StaticData.ProcessID,
                    out ProcessNetworkUsageSample networkSample) == true)
            {
                if (!history.HasNetworkSample)
                {
                    SetDynamicDouble(schema, history, ProcessTableColumnKind.Network, 0);
                    UpdateNetworkBaseline(history, networkSample);
                }
                else if (networkSample.Generation != history.LastNetworkSampleGeneration)
                {
                    double bytesPerSecond = CalculateTransferRate(
                        true,
                        history.NetworkBytes,
                        history.LastNetworkSampleTimestamp,
                        networkSample.CumulativeBytes,
                        networkSample.Timestamp);
                    SetDynamicDouble(
                        schema,
                        history,
                        ProcessTableColumnKind.Network,
                        bytesPerSecond);
                    UpdateNetworkBaseline(history, networkSample);
                }
            }
            else
            {
                history.HasNetworkSample = false;
                SetDynamicDouble(schema, history, ProcessTableColumnKind.Network, -1);
            }
        }

        if (HasAnyColumn(activeMask, ThreadColumnsMask))
        {
            ProcessExecutionState state;
            int threadCount;
            if (hasSystemProcessData)
            {
                state = _systemProcessSnapshot.ReadExecutionState(systemProcessData);
                threadCount = systemProcessData.ThreadCount;
            }
            else
            {
                ReadThreadState(process, out state, out threadCount);
            }

            SetDynamicCode(
                schema,
                history,
                ProcessTableColumnKind.Status,
                state == ProcessExecutionState.Suspended
                    ? ProcessDisplayCode.Suspended
                    : ProcessDisplayCode.Running);
            SetDynamicNumeric(schema, history, ProcessTableColumnKind.Threads, threadCount);
        }

        if (schema.IsVisible(ProcessTableColumnKind.BasePriority))
        {
            int value = hasSystemProcessData ? systemProcessData.BasePriority : ReadBasePriority(process);
            SetDynamicNumeric(schema, history, ProcessTableColumnKind.BasePriority, value);
        }
        if (schema.IsVisible(ProcessTableColumnKind.Handles))
        {
            int value = hasSystemProcessData ? systemProcessData.HandleCount : ReadHandleCount(process);
            SetDynamicNumeric(schema, history, ProcessTableColumnKind.Handles, value);
        }
        if (schema.IsVisible(ProcessTableColumnKind.UserObjects))
        {
            int value = processHandle == IntPtr.Zero ? 0 : NativeProcessInfo.ReadUserObjectCount(processHandle);
            SetDynamicNumeric(schema, history, ProcessTableColumnKind.UserObjects, value);
        }
        if (schema.IsVisible(ProcessTableColumnKind.GDIObjects))
        {
            int value = processHandle == IntPtr.Zero ? 0 : NativeProcessInfo.ReadGDIObjectCount(processHandle);
            SetDynamicNumeric(schema, history, ProcessTableColumnKind.GDIObjects, value);
        }

        if (HasAnyColumn(activeMask, IOColumnsMask))
        {
            NativeProcessInfo.ProcessIOCounters io = hasSystemProcessData
                ? new NativeProcessInfo.ProcessIOCounters(
                    systemProcessData.IOReadOperations,
                    systemProcessData.IOWriteOperations,
                    systemProcessData.IOOtherOperations,
                    systemProcessData.IOReadBytes,
                    systemProcessData.IOWriteBytes,
                    systemProcessData.IOOtherBytes)
                : processHandle == IntPtr.Zero
                    ? default
                    : NativeProcessInfo.ReadIOCounters(processHandle);
            SetDynamicUnsigned(schema, history, ProcessTableColumnKind.IOReads, io.ReadOperations);
            SetDynamicUnsigned(schema, history, ProcessTableColumnKind.IOWrites, io.WriteOperations);
            SetDynamicUnsigned(schema, history, ProcessTableColumnKind.IOOther, io.OtherOperations);
            SetDynamicUnsigned(schema, history, ProcessTableColumnKind.IOReadBytes, io.ReadBytes);
            SetDynamicUnsigned(schema, history, ProcessTableColumnKind.IOWriteBytes, io.WriteBytes);
            SetDynamicUnsigned(schema, history, ProcessTableColumnKind.IOOtherBytes, io.OtherBytes);
        }

        if (schema.IsVisible(ProcessTableColumnKind.UACVirtualization))
        {
            SetDynamicCode(
                schema,
                history,
                ProcessTableColumnKind.UACVirtualization,
                processHandle == IntPtr.Zero
                    ? ProcessDisplayCode.Unavailable
                    : NativeProcessInfo.ReadUACVirtualization(processHandle));
        }
        if (schema.IsVisible(ProcessTableColumnKind.IOPriority))
        {
            SetDynamicCode(
                schema,
                history,
                ProcessTableColumnKind.IOPriority,
                processHandle == IntPtr.Zero
                    ? ProcessDisplayCode.Unavailable
                    : NativeProcessInfo.ReadIOPriority(processHandle));
        }
        if (schema.IsVisible(ProcessTableColumnKind.PowerThrottling))
        {
            SetDynamicCode(
                schema,
                history,
                ProcessTableColumnKind.PowerThrottling,
                processHandle == IntPtr.Zero
                    ? ProcessDisplayCode.Unavailable
                    : NativeProcessInfo.ReadPowerThrottling(processHandle));
        }
        if (schema.IsVisible(ProcessTableColumnKind.DPIAwareness))
        {
            SetDynamicCode(
                schema,
                history,
                ProcessTableColumnKind.DPIAwareness,
                processHandle == IntPtr.Zero
                    ? ProcessDisplayCode.Unavailable
                    : NativeProcessInfo.ReadDPIAwareness(processHandle));
        }

        if (HasAnyColumn(activeMask, GPUColumnsMask))
        {
            ProcessAcceleratorSample acceleratorSample = default;
            bool hasSample = _acceleratorSampler?.TryGetSample(
                history.StaticData.ProcessID,
                out acceleratorSample) == true;
            bool hasCurrentProcessSample = hasSample || _acceleratorSamplesEveryProcess || isWarm;
            double utilization = _acceleratorSampler?.HasUtilizationData == true
                                 && hasCurrentProcessSample
                ? acceleratorSample.GPUUtilization
                : -1;
            SetDynamicDouble(schema, history, ProcessTableColumnKind.GPU, utilization);
            SetDynamicText(
                schema,
                history,
                ProcessTableColumnKind.GPUEngine,
                _acceleratorSampler?.HasUtilizationData == true && hasCurrentProcessSample
                    ? acceleratorSample.GPUEngine ?? string.Empty
                    : NativeProcessInfo.Unavailable);
            SetDynamicNumeric(
                schema,
                history,
                ProcessTableColumnKind.DedicatedGPUMemory,
                _acceleratorSampler?.HasDedicatedMemoryData == true && hasCurrentProcessSample
                    ? acceleratorSample.DedicatedGPUMemory
                    : -1);
            SetDynamicNumeric(
                schema,
                history,
                ProcessTableColumnKind.SharedGPUMemory,
                _acceleratorSampler?.HasSharedMemoryData == true && hasCurrentProcessSample
                    ? acceleratorSample.SharedGPUMemory
                    : -1);
        }
        if (HasAnyColumn(activeMask, NPUColumnsMask))
        {
            ProcessAcceleratorSample acceleratorSample = default;
            bool hasSample = _acceleratorSampler?.TryGetSample(
                history.StaticData.ProcessID,
                out acceleratorSample) == true;
            bool hasCurrentProcessSample = hasSample || _acceleratorSamplesEveryProcess || isWarm;
            double utilization = _acceleratorSampler?.HasUtilizationData == true
                                 && hasCurrentProcessSample
                ? acceleratorSample.NPUUtilization
                : -1;
            SetDynamicDouble(schema, history, ProcessTableColumnKind.NPU, utilization);
            SetDynamicText(
                schema,
                history,
                ProcessTableColumnKind.NPUEngine,
                _acceleratorSampler?.HasUtilizationData == true && hasCurrentProcessSample
                    ? acceleratorSample.NPUEngine ?? string.Empty
                    : NativeProcessInfo.Unavailable);
            SetDynamicNumeric(
                schema,
                history,
                ProcessTableColumnKind.DedicatedNPUMemory,
                _acceleratorSampler?.HasDedicatedMemoryData == true && hasCurrentProcessSample
                    ? acceleratorSample.DedicatedNPUMemory
                    : -1);
            SetDynamicNumeric(
                schema,
                history,
                ProcessTableColumnKind.SharedNPUMemory,
                _acceleratorSampler?.HasSharedMemoryData == true && hasCurrentProcessSample
                    ? acceleratorSample.SharedNPUMemory
                    : -1);
        }

        history.HasDynamicSample = true;
    }

    private ProcessImageIdentity AcquireImageIdentity(
        string processName,
        string imagePath,
        IntPtr processHandle,
        bool needsIcon,
        bool needsDescription)
    {
        string key = imagePath.Length > 0 ? imagePath : string.Concat("\0", processName);
        if (_imageIdentities.TryGetValue(key, out ProcessImageIdentity? existing))
        {
            existing.ReferenceCount++;
            return existing;
        }

        string description = needsDescription ? ReadDescription(imagePath) : string.Empty;
        ProcessIconSource iconSource = processHandle == IntPtr.Zero || !needsIcon
            ? default
            : ReadIconSource(processHandle, imagePath);
        ProcessImageIdentity identity = new(key, processName, imagePath, description, iconSource);
        _imageIdentities.Add(key, identity);
        return identity;
    }

    private void ReleaseImageIdentity(ProcessImageIdentity identity)
    {
        identity.ReferenceCount--;
        if (identity.ReferenceCount > 0) return;
        _imageIdentities.Remove(identity.Key);
    }

    private string AcquireUserName(string userName)
    {
        if (_sharedUserNames.TryGetValue(userName, out SharedUserName? existing))
        {
            existing.ReferenceCount++;
            return existing.Value;
        }

        _sharedUserNames.Add(userName, new SharedUserName(userName));
        return userName;
    }

    private void ReleaseUserName(string userName)
    {
        if (!_sharedUserNames.TryGetValue(userName, out SharedUserName? existing)) return;

        existing.ReferenceCount--;
        if (existing.ReferenceCount <= 0)
            _sharedUserNames.Remove(userName);
    }

    private static string ReadDescription(string imagePath)
    {
        if (imagePath.Length == 0) return string.Empty;

        try
        {
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(imagePath);
            return version.FileDescription ?? string.Empty;
        }
        catch (Exception exception) when (exception is FileNotFoundException
                                          or UnauthorizedAccessException
                                          or Win32Exception
                                          or NotSupportedException)
        {
            return string.Empty;
        }
    }

    private int NextHistoryGeneration()
    {
        int next = unchecked(_historyGeneration + 1);
        if (next != 0)
        {
            _historyGeneration = next;
            return next;
        }

        ResetHistoryForSchema(_historySchemaMask);
        _historyGeneration = 1;
        return 1;
    }

    private void ResetHistoryForSchema(ulong schemaMask)
    {
        _history.Clear();
        _imageIdentities.Clear();
        _sharedUserNames.Clear();
        _historySchemaMask = schemaMask;
    }

    private static IntPtr OpenQueryHandle(int processID)
    {
        if (processID <= 0) return IntPtr.Zero;

        IntPtr handle = Kernel32.OpenProcess(
            Kernel32.PROCESS_QUERY_LIMITED_INFORMATION | ProcessQueryInformation | ProcessVMRead,
            false,
            (uint)processID);
        return handle != IntPtr.Zero
            ? handle
            : Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processID);
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
            return NormalizeProcessName(process.ProcessName, processID);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return NormalizeProcessName(string.Empty, processID);
        }
    }

    private static string NormalizeProcessName(string name, int processID)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return processID switch
            {
                0 => "System Idle Process",
                4 => "System",
                _ => "Process " + processID
            };
        }

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return name;
        if (name[0] is '[' or '<') return name;
        return name switch
        {
            "Registry" or "Memory Compression" or "Secure System" or "System" => name,
            _ => string.Concat(name, ".exe")
        };
    }

    private ProcessIconSource ReadIconSource(IntPtr processHandle, string imagePath)
    {
        string? applicationUserModelID = ReadApplicationUserModelID(processHandle);
        return new ProcessIconSource(imagePath.Length == 0 ? null : imagePath, applicationUserModelID);
    }

    private string ReadExecutablePath(IntPtr processHandle)
    {
        while (true)
        {
            _processPathBuffer.Clear();
            uint characterCount = (uint)_processPathBuffer.Capacity;
            if (Kernel32.QueryFullProcessImageNameW(processHandle, 0, _processPathBuffer, ref characterCount))
                return _processPathBuffer.ToString(0, (int)characterCount);

            int error = Marshal.GetLastPInvokeError();
            if (error != NativeErrors.ERROR_INSUFFICIENT_BUFFER
                || _processPathBuffer.Capacity >= MaximumProcessPathCapacity)
            {
                return string.Empty;
            }

            int nextCapacity = Math.Min(_processPathBuffer.Capacity * 2, MaximumProcessPathCapacity);
            _processPathBuffer.EnsureCapacity(nextCapacity);
        }
    }

    private string? ReadApplicationUserModelID(IntPtr processHandle)
    {
        _applicationUserModelIDBuffer.Clear();
        int characterCount = _applicationUserModelIDBuffer.Capacity;
        int result = IconExtraction.GetApplicationUserModelId(
            processHandle,
            ref characterCount,
            _applicationUserModelIDBuffer);
        return result == NativeErrors.S_OK && _applicationUserModelIDBuffer.Length > 0
            ? _applicationUserModelIDBuffer.ToString()
            : null;
    }

    private static int ReadSessionID(Process process)
    {
        try
        {
            return process.SessionId;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return -1;
        }
    }

    private static long ReadTotalProcessorTicks(Process? process)
    {
        if (process == null) return 0;

        try
        {
            return process.TotalProcessorTime.Ticks;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return 0;
        }
    }

    private static NativeProcessInfo.ProcessMemoryCounters ReadMemoryCounters(
        Process? process,
        IntPtr processHandle)
    {
        if (processHandle != IntPtr.Zero
            && NativeProcessInfo.TryReadMemoryCounters(processHandle, out NativeProcessInfo.ProcessMemoryCounters counters))
        {
            return counters;
        }

        if (process == null) return default;

        long workingSet = ReadWorkingSetBytes(process);
        long privateBytes = ReadPrivateMemoryBytes(process);
        return new NativeProcessInfo.ProcessMemoryCounters(
            workingSet,
            ReadPeakWorkingSetBytes(process),
            Math.Min(workingSet, privateBytes),
            Math.Max(0, workingSet - privateBytes),
            privateBytes,
            0,
            0,
            0);
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

    private static long ReadPeakWorkingSetBytes(Process process)
    {
        try
        {
            return Math.Max(0, process.PeakWorkingSet64);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return 0;
        }
    }

    private static int ReadHandleCount(Process? process)
    {
        if (process == null) return 0;

        try
        {
            return Math.Max(0, process.HandleCount);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return 0;
        }
    }

    private static int ReadBasePriority(Process? process)
    {
        if (process == null) return 0;

        try
        {
            return process.BasePriority;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return 0;
        }
    }

    private static void ReadThreadState(
        Process? process,
        out ProcessExecutionState state,
        out int threadCount)
    {
        state = ProcessExecutionState.Running;
        threadCount = 0;
        if (process == null) return;

        try
        {
            ProcessThreadCollection threads = process.Threads;
            threadCount = threads.Count;
            if (threadCount == 0) return;

            bool allSuspended = true;
            for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
            {
                using ProcessThread thread = threads[threadIndex];
                if (thread.ThreadState == System.Diagnostics.ThreadState.Wait
                    && thread.WaitReason == ThreadWaitReason.Suspended)
                {
                    continue;
                }

                allSuspended = false;
            }

            state = allSuspended ? ProcessExecutionState.Suspended : ProcessExecutionState.Running;
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or Win32Exception
                                          or NotSupportedException
                                          or PlatformNotSupportedException)
        {
            state = ProcessExecutionState.Running;
        }
    }

    private static double CalculateCPUPercent(
        ProcessHistoryEntry history,
        long totalProcessorTicks,
        long sampleTimestamp)
    {
        if (!history.HasProcessorSample
            || sampleTimestamp <= history.LastProcessorSampleTimestamp
            || totalProcessorTicks < history.TotalProcessorTicks)
        {
            return 0;
        }

        double elapsedSeconds = (sampleTimestamp - history.LastProcessorSampleTimestamp)
                                / (double)Stopwatch.Frequency;
        long processorTickDelta = totalProcessorTicks - history.TotalProcessorTicks;
        double processorSeconds = processorTickDelta / (double)TimeSpan.TicksPerSecond;
        double normalized = processorSeconds / elapsedSeconds / Environment.ProcessorCount * 100;
        return Math.Clamp(normalized, 0, 100);
    }

    private static double CalculateCPUUtility(
        ProcessHistoryEntry history,
        ulong cycleCount,
        long sampleTimestamp,
        double nominalProcessorCycleCapacity)
    {
        if (nominalProcessorCycleCapacity <= 0) return -1;
        if (!history.HasCycleSample
            || sampleTimestamp <= history.LastCycleSampleTimestamp
            || cycleCount < history.CycleCount)
        {
            return 0;
        }

        double elapsedSeconds = (sampleTimestamp - history.LastCycleSampleTimestamp)
                                / (double)Stopwatch.Frequency;
        double cycleDelta = cycleCount - history.CycleCount;
        double utility = cycleDelta / elapsedSeconds / nominalProcessorCycleCapacity * 100;
        return Math.Clamp(utility, 0, 1_000);
    }

    /// <summary>Calculates a byte rate while treating first samples and counter resets as baselines.</summary>
    internal static double CalculateTransferRate(
        bool hasPreviousSample,
        ulong previousBytes,
        long previousTimestamp,
        ulong currentBytes,
        long currentTimestamp)
    {
        if (!hasPreviousSample
            || currentTimestamp <= previousTimestamp
            || currentBytes < previousBytes)
        {
            return 0;
        }

        double elapsedSeconds = (currentTimestamp - previousTimestamp)
                                / (double)Stopwatch.Frequency;
        return (currentBytes - previousBytes) / elapsedSeconds;
    }

    private static void UpdateNetworkBaseline(
        ProcessHistoryEntry history,
        ProcessNetworkUsageSample sample)
    {
        history.NetworkBytes = sample.CumulativeBytes;
        history.LastNetworkSampleTimestamp = sample.Timestamp;
        history.LastNetworkSampleGeneration = sample.Generation;
        history.HasNetworkSample = true;
    }

    private void RemoveStaleHistory(int generation)
    {
        _staleProcessIDs.Clear();
        foreach (KeyValuePair<int, ProcessHistoryEntry> pair in _history)
        {
            if (pair.Value.LastSeenGeneration != generation)
                _staleProcessIDs.Add(pair.Key);
        }

        for (int staleIndex = 0; staleIndex < _staleProcessIDs.Count; staleIndex++)
        {
            int processID = _staleProcessIDs[staleIndex];
            ProcessHistoryEntry history = _history[processID];
            _history.Remove(processID);
            ReleaseImageIdentity(history.StaticData.Image);
            ReleaseUserName(history.StaticData.UserName);
        }
    }

    private void Publish(SystemPerformanceSample systemPerformanceSample)
    {
        lock (_publishGate)
        {
            ProcessSnapshotBuffer previousPublished = _publishedBuffer;
            _publishedBuffer = _stagingBuffer;
            _stagingBuffer = previousPublished;
            _latestSystemPerformanceSample = systemPerformanceSample;
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

    private static void EnsurePolicyCapacity(ref int[] values, int count)
    {
        if (values.Length >= count) return;

        int capacity = Math.Max(256, values.Length);
        while (capacity < count)
            capacity = checked(capacity * 2);
        Array.Resize(ref values, capacity);
    }

    private static void SetStaticNumeric(
        ProcessDataSchema schema,
        long[] values,
        ProcessTableColumnKind column,
        long value)
    {
        int slot = schema.GetStaticNumericSlot(column);
        if (slot >= 0) values[slot] = value;
    }

    private static void SetStaticCode(
        ProcessDataSchema schema,
        long[] values,
        ProcessTableColumnKind column,
        ProcessDisplayCode value) =>
        SetStaticNumeric(schema, values, column, (long)value);

    private static void SetStaticText(
        ProcessDataSchema schema,
        string?[] values,
        ProcessTableColumnKind column,
        string value)
    {
        int slot = schema.GetStaticTextSlot(column);
        if (slot >= 0) values[slot] = value;
    }

    private static void SetDynamicNumeric(
        ProcessDataSchema schema,
        ProcessHistoryEntry history,
        ProcessTableColumnKind column,
        long value)
    {
        int slot = schema.GetDynamicNumericSlot(column);
        if (slot >= 0) history.DynamicNumericValues[slot] = value;
    }

    private static void SetDynamicUnsigned(
        ProcessDataSchema schema,
        ProcessHistoryEntry history,
        ProcessTableColumnKind column,
        ulong value) =>
        SetDynamicNumeric(schema, history, column, unchecked((long)value));

    private static void SetDynamicDouble(
        ProcessDataSchema schema,
        ProcessHistoryEntry history,
        ProcessTableColumnKind column,
        double value) =>
        SetDynamicNumeric(schema, history, column, BitConverter.DoubleToInt64Bits(value));

    private static void SetDynamicCode(
        ProcessDataSchema schema,
        ProcessHistoryEntry history,
        ProcessTableColumnKind column,
        ProcessDisplayCode value) =>
        SetDynamicNumeric(schema, history, column, (long)value);

    private static void SetDynamicText(
        ProcessDataSchema schema,
        ProcessHistoryEntry history,
        ProcessTableColumnKind column,
        string value)
    {
        int slot = schema.GetDynamicTextSlot(column);
        if (slot >= 0) history.DynamicTextValues[slot] = value;
    }

    private static ulong ColumnMask(ProcessTableColumnKind column) =>
        ProcessTableColumnCatalog.GetMask(column);

    private static bool HasAnyColumn(ulong activeMask, ulong columns) =>
        (activeMask & columns) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        SnapshotAvailable = null;
        _refreshWake.Set();
        if (Volatile.Read(ref _started) != 0 && !_samplingThread.Join(ShutdownJoinTimeoutMilliseconds))
            TADNLog.Log("ProcessSnapshotService sampling thread did not stop before the shutdown timeout.");

        _refreshWake.Dispose();
        _acceleratorSampler?.Dispose();
        _enterpriseContextReader?.Dispose();
        _networkUsageSampler?.Dispose();
        _systemPerformanceSampler.Dispose();
        _systemProcessSnapshot.Dispose();
        _history.Clear();
        _imageIdentities.Clear();
        _sharedUserNames.Clear();
        _systemProcessData.Clear();
    }

    private sealed class ProcessHistoryEntry
    {
        public required ProcessStaticData StaticData;
        public required long[] DynamicNumericValues;
        public required string?[] DynamicTextValues;
        public long TotalProcessorTicks;
        public long LastProcessorSampleTimestamp;
        public ulong CycleCount;
        public long LastCycleSampleTimestamp;
        public long WorkingSetBytes;
        public long PageFaultCount;
        public long LastMemorySampleTimestamp;
        public ulong DiskBytes;
        public long LastDiskSampleTimestamp;
        public ulong NetworkBytes;
        public long LastNetworkSampleTimestamp;
        public long LastNetworkSampleGeneration;
        public int LastSeenGeneration;
        public bool HasDynamicSample;
        public bool HasProcessorSample;
        public bool HasCycleSample;
        public bool HasMemorySample;
        public bool HasDiskSample;
        public bool HasNetworkSample;
    }

    private sealed class SharedUserName(string value)
    {
        public string Value { get; } = value;
        public int ReferenceCount { get; set; } = 1;
    }
}
