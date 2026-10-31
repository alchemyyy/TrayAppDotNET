using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using TaskManagerTrayAppDotNET.Interop;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Samples raw Windows GPU engine and adapter-memory counters grouped by adapter LUID.</summary>
internal sealed unsafe class GPUPerformanceSampler : IDisposable
{
    private const string UtilizationPath = @"\GPU Engine(*)\Utilization Percentage";
    private const string DedicatedMemoryPath = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private const string SharedMemoryPath = @"\GPU Adapter Memory(*)\Shared Usage";
    private const uint PdhSuccess = 0;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhValidData = 0;
    private const uint PdhNewData = 1;
    private const uint PdhFormatDouble = 0x00000200;
    private const uint PdhFormatLarge = 0x00000400;
    private const uint PdhFormatNoCap100 = 0x00008000;
    private const int MaximumCounterInstanceNameLength = 1_024;
    private static readonly TimeSpan AdapterRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<GPUAdapterKey, GPUCounterAccumulator> _accumulators = [];
    private readonly PdhQuery _query;
    private IntPtr _utilizationCounter;
    private IntPtr _dedicatedMemoryCounter;
    private IntPtr _sharedMemoryCounter;
    private IntPtr _counterBuffer;
    private uint _counterBufferSize;
    private bool _queryPrimed;
    private readonly HashSet<ulong> _unlistedCounterLUIDs = [];
    private readonly HashSet<ulong> _listedLUIDs = [];
    private readonly Dictionary<GPUAdapterKey, GPUAdapterPersistentIdentity> _identities = [];
    private HashSet<ulong> _previousReportingLUIDs = [];
    private HashSet<ulong> _currentReportingLUIDs = [];
    private GPUAdapterMetadata[] _adapters = [];
    private long _adaptersReadTimestamp;
    private bool _disposed;

    public GPUPerformanceSampler() => _query = new PdhQuery(nameof(GPUPerformanceSampler), AddCounters);

    private uint AddCounters(PdhQuery query)
    {
        uint failureStatus = PdhSuccess;
        _queryPrimed = false;
        _utilizationCounter = AddCounter(query, UtilizationPath, ref failureStatus);
        _dedicatedMemoryCounter = AddCounter(query, DedicatedMemoryPath, ref failureStatus);
        _sharedMemoryCounter = AddCounter(query, SharedMemoryPath, ref failureStatus);
        return _utilizationCounter != IntPtr.Zero
               || _dedicatedMemoryCounter != IntPtr.Zero
               || _sharedMemoryCounter != IntPtr.Zero
            ? PdhSuccess
            : failureStatus;
    }

    /// <summary>
    /// Returns the DXGI hardware adapters, reusing the previous enumeration: creating a factory and enumerating
    /// adapters loads driver components, so it does not run every tick. The list is re-read every
    /// <see cref="AdapterRefreshInterval"/> and as soon as the counters show a change (<see cref="NeedsAdapterRefresh"/>):
    /// a new LUID (hot-plug or driver restart) or a listed LUID that stopped reporting (unplug or disable).
    /// </summary>
    private GPUAdapterMetadata[] GetAdapters(bool countersRead)
    {
        _currentReportingLUIDs.Clear();
        foreach (GPUAdapterKey key in _accumulators.Keys)
            _currentReportingLUIDs.Add(key.LUID);

        long now = Stopwatch.GetTimestamp();
        bool refresh = _adaptersReadTimestamp == 0
                       || Stopwatch.GetElapsedTime(_adaptersReadTimestamp, now) >= AdapterRefreshInterval
                       || NeedsAdapterRefresh(
                           countersRead,
                           _listedLUIDs,
                           _unlistedCounterLUIDs,
                           _previousReportingLUIDs,
                           _currentReportingLUIDs);
        if (refresh)
        {
            _adapters = DXGIAdapterEnumerator.Enumerate();
            _adaptersReadTimestamp = now;
            _identities.Clear();
            _listedLUIDs.Clear();
            for (int adapterIndex = 0; adapterIndex < _adapters.Length; adapterIndex++)
                _listedLUIDs.Add(_adapters[adapterIndex].LUID);

            _unlistedCounterLUIDs.Clear();
            foreach (ulong adapterLUID in _currentReportingLUIDs)
            {
                if (!_listedLUIDs.Contains(adapterLUID)) _unlistedCounterLUIDs.Add(adapterLUID);
            }
        }

        // A tick without counters keeps the previous reporters, so it does not hide or fake a disappearance
        if (countersRead)
            (_previousReportingLUIDs, _currentReportingLUIDs) = (_currentReportingLUIDs, _previousReportingLUIDs);

        return _adapters;
    }

    /// <summary>
    /// Reads a kernel identity once per adapter key until the next adapter enumeration. A failed read is not
    /// cached, so a transient failure does not leave the card under a fallback ID until the next enumeration.
    /// </summary>
    private GPUAdapterPersistentIdentity ReadIdentity(GPUAdapterKey key)
    {
        if (_identities.TryGetValue(key, out GPUAdapterPersistentIdentity identity)) return identity;

        identity = D3DKMTAdapterIdentityReader.Read(key);
        if (IsKnownIdentity(identity)) _identities.Add(key, identity);
        return identity;
    }

    internal static bool IsKnownIdentity(GPUAdapterPersistentIdentity identity) =>
        !string.IsNullOrWhiteSpace(identity.HardwarePNPKey) || identity.UniqueAdapterGUID != Guid.Empty;

    /// <summary>
    /// Whether the adapter list must be read again now: a counter reports a LUID the list does not have (and that
    /// a fresh enumeration has not already shown to be unlisted), or a listed adapter that reported counters on
    /// the previous tick no longer does (unplugged or disabled). Only ticks that read counters can tell.
    /// </summary>
    internal static bool NeedsAdapterRefresh(
        bool countersRead,
        HashSet<ulong> listedLUIDs,
        HashSet<ulong> knownUnlistedLUIDs,
        HashSet<ulong> previousReportingLUIDs,
        HashSet<ulong> currentReportingLUIDs)
    {
        if (!countersRead) return false;

        foreach (ulong adapterLUID in currentReportingLUIDs)
        {
            if (!listedLUIDs.Contains(adapterLUID) && !knownUnlistedLUIDs.Contains(adapterLUID)) return true;
        }

        foreach (ulong adapterLUID in previousReportingLUIDs)
        {
            if (listedLUIDs.Contains(adapterLUID) && !currentReportingLUIDs.Contains(adapterLUID)) return true;
        }

        return false;
    }

    /// <summary>Captures one card per hardware PNP display device and attaches raw PDH values.</summary>
    public GPUPerformanceSnapshot[] Sample()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _accumulators.Clear();

        bool hasUtilizationQuery = false;
        bool hasDedicatedMemoryQuery = false;
        bool hasSharedMemoryQuery = false;
        if (_query.TryCollect())
        {
            bool canReadFormattedValues = _queryPrimed;
            _queryPrimed = true;
            if (canReadFormattedValues)
            {
                hasUtilizationQuery = ReadUtilizationCounters();
                hasDedicatedMemoryQuery = ReadMemoryCounters(_dedicatedMemoryCounter, isDedicated: true);
                hasSharedMemoryQuery = ReadMemoryCounters(_sharedMemoryCounter, isDedicated: false);
            }
        }

        GPUAdapterMetadata[] adapters = GetAdapters(
            countersRead: hasUtilizationQuery || hasDedicatedMemoryQuery || hasSharedMemoryQuery);

        Dictionary<ulong, GPUAdapterMetadata> metadataByLUID = [];
        for (int adapterIndex = 0; adapterIndex < adapters.Length; adapterIndex++)
        {
            GPUAdapterMetadata adapter = adapters[adapterIndex];
            if (!metadataByLUID.TryGetValue(adapter.LUID, out GPUAdapterMetadata existing)
                || adapter.DisplayIndex < existing.DisplayIndex)
                metadataByLUID[adapter.LUID] = adapter;
        }

        GPUAdapterKey[] displayDeviceKeys = ResolveDisplayDeviceKeys(adapters, _accumulators.Keys);
        Dictionary<ulong, GPUAdapterPersistentIdentity> baseIdentitiesByLUID = [];
        Dictionary<string, GPUDeviceGroupBuilder> groups = new(StringComparer.OrdinalIgnoreCase);
        for (int keyIndex = 0; keyIndex < displayDeviceKeys.Length; keyIndex++)
        {
            GPUAdapterKey key = displayDeviceKeys[keyIndex];
            if (!metadataByLUID.TryGetValue(key.LUID, out GPUAdapterMetadata metadata)) continue;

            if (!baseIdentitiesByLUID.TryGetValue(
                    key.LUID,
                    out GPUAdapterPersistentIdentity baseIdentity))
            {
                baseIdentity = ReadIdentity(key with { PhysicalAdapterIndex = 0 });
                baseIdentitiesByLUID.Add(key.LUID, baseIdentity);
            }

            GPUAdapterPersistentIdentity physicalIdentity = key.PhysicalAdapterIndex == 0
                ? baseIdentity
                : ReadIdentity(key);
            string? hardwarePNPKey = !string.IsNullOrWhiteSpace(physicalIdentity.HardwarePNPKey)
                ? physicalIdentity.HardwarePNPKey
                : baseIdentity.HardwarePNPKey;
            Guid uniqueAdapterGUID = physicalIdentity.UniqueAdapterGUID != Guid.Empty
                ? physicalIdentity.UniqueAdapterGUID
                : baseIdentity.UniqueAdapterGUID;
            if (IsVirtualDisplayPNPKey(hardwarePNPKey)) continue;

            string fallbackDeviceID = CreatePCIFallbackDeviceID(
                metadata.VendorID,
                metadata.DeviceID,
                metadata.SubsystemID,
                metadata.Revision,
                metadata.DisplayIndex,
                physicalAdapterIndex: 0);
            GPUDeviceIdentity identity = new(
                key,
                hardwarePNPKey,
                uniqueAdapterGUID,
                fallbackDeviceID);
            string deviceID = ResolveStableDeviceID(identity);
            if (!groups.TryGetValue(deviceID, out GPUDeviceGroupBuilder? group))
            {
                group = new GPUDeviceGroupBuilder(deviceID);
                groups.Add(deviceID, group);
            }

            _accumulators.TryGetValue(key, out GPUCounterAccumulator? accumulator);
            group.Add(key, metadata, accumulator);
        }

        List<GPUDeviceCandidate> candidates = new(groups.Count);
        foreach (GPUDeviceGroupBuilder group in groups.Values)
        {
            candidates.Add(group.CreateCandidate(
                hasUtilizationQuery,
                hasDedicatedMemoryQuery,
                hasSharedMemoryQuery));
        }

        candidates.Sort(static (left, right) =>
        {
            int displayComparison = left.DisplayIndex.CompareTo(right.DisplayIndex);
            return displayComparison != 0
                ? displayComparison
                : StringComparer.OrdinalIgnoreCase.Compare(left.DeviceID, right.DeviceID);
        });

        GPUPerformanceSnapshot[] snapshots = new GPUPerformanceSnapshot[candidates.Count];
        for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            GPUDeviceCandidate candidate = candidates[candidateIndex];
            string displayName = candidate.Name == "GPU"
                ? string.Create(CultureInfo.InvariantCulture, $"GPU {candidateIndex}")
                : candidate.Name;
            snapshots[candidateIndex] = new GPUPerformanceSnapshot(
                candidate.DeviceID,
                PerformanceDeviceKind.GPU,
                candidateIndex,
                displayName,
                candidate.Key.LUID,
                candidate.Key.PhysicalAdapterIndex,
                candidate.HasUtilizationSample,
                candidate.UtilizationPercent,
                candidate.Engines,
                candidate.HasDedicatedMemoryData,
                candidate.DedicatedMemoryBytes,
                candidate.DedicatedMemoryCapacityBytes,
                candidate.HasSharedMemoryData,
                candidate.SharedMemoryBytes,
                candidate.SharedMemoryCapacityBytes);
        }

        return snapshots;
    }

    /// <summary>Aggregates all process instances belonging to the same physical GPU engine.</summary>
    internal static GPUPerformanceEngineSnapshot[] AggregateEngineSamples(
        IEnumerable<GPUEngineCounterSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        Dictionary<int, MutableEngineSample> byEngineIndex = [];
        foreach (GPUEngineCounterSample sample in samples)
        {
            if (sample.EngineIndex < 0
                || !double.IsFinite(sample.UtilizationPercent)
                || sample.UtilizationPercent < 0)
                continue;

            if (!byEngineIndex.TryGetValue(sample.EngineIndex, out MutableEngineSample? aggregate))
            {
                aggregate = new MutableEngineSample(sample.Name);
                byEngineIndex.Add(sample.EngineIndex, aggregate);
            }

            aggregate.UtilizationPercent += sample.UtilizationPercent;
        }

        GPUPerformanceEngineSnapshot[] result = new GPUPerformanceEngineSnapshot[byEngineIndex.Count];
        int resultIndex = 0;
        foreach (KeyValuePair<int, MutableEngineSample> pair in byEngineIndex.OrderBy(static pair => pair.Key))
        {
            result[resultIndex] = new GPUPerformanceEngineSnapshot(
                pair.Key,
                pair.Value.Name,
                Math.Clamp(pair.Value.UtilizationPercent, min: 0, max: 100));
            resultIndex++;
        }

        return result;
    }

    /// <summary>Retains only counter tuples belonging to hardware adapters exposed by DXGI.</summary>
    internal static GPUAdapterKey[] ResolveDisplayDeviceKeys(
        IReadOnlyList<GPUAdapterMetadata> adapters,
        IEnumerable<GPUAdapterKey> counterKeys)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(counterKeys);

        HashSet<ulong> displayAdapterLUIDs = [];
        for (int adapterIndex = 0; adapterIndex < adapters.Count; adapterIndex++)
            displayAdapterLUIDs.Add(adapters[adapterIndex].LUID);

        HashSet<GPUAdapterKey> resultKeys = [];
        foreach (GPUAdapterKey counterKey in counterKeys)
        {
            if (displayAdapterLUIDs.Contains(counterKey.LUID))
                resultKeys.Add(counterKey);
        }

        for (int adapterIndex = 0; adapterIndex < adapters.Count; adapterIndex++)
            resultKeys.Add(new GPUAdapterKey(adapters[adapterIndex].LUID, PhysicalAdapterIndex: 0));

        GPUAdapterKey[] result = new GPUAdapterKey[resultKeys.Count];
        resultKeys.CopyTo(result);
        Array.Sort(result, static (left, right) =>
        {
            int luidComparison = left.LUID.CompareTo(right.LUID);
            return luidComparison != 0
                ? luidComparison
                : left.PhysicalAdapterIndex.CompareTo(right.PhysicalAdapterIndex);
        });
        return result;
    }

    private static IntPtr AddCounter(PdhQuery query, string path, ref uint failureStatus)
    {
        uint status = query.AddCounter(path, out IntPtr counter);
        if (status == PdhSuccess) return counter;
        failureStatus = status;
        return IntPtr.Zero;
    }

    private bool ReadUtilizationCounters()
    {
        if (_utilizationCounter == IntPtr.Zero
            || !TryReadCounterArray(
                _utilizationCounter,
                PdhFormatDouble | PdhFormatNoCap100,
                out PDH_FORMATTED_COUNTER_VALUE_ITEM* items,
                out uint itemCount))
            return false;

        for (uint itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            PDH_FORMATTED_COUNTER_VALUE_ITEM item = items[itemIndex];
            if (!HasValidStatus(item.Value.Status)
                || !double.IsFinite(item.Value.DoubleValue)
                || item.Value.DoubleValue < 0
                || item.Name == IntPtr.Zero)
                continue;

            ReadOnlySpan<char> instanceName = ReadNullTerminatedSpan((char*)item.Name);
            if (!AcceleratorCounterInstanceParser.TryParseEngine(
                    instanceName,
                    out AcceleratorCounterInstance instance))
                continue;

            GPUAdapterKey adapterKey = new(instance.AdapterLUID, instance.PhysicalAdapterIndex);
            GPUCounterAccumulator accumulator = GetOrCreateAccumulator(adapterKey);
            string engineName = GPUEngineNames.Normalize(instanceName[instance.EngineTypeStart..]);
            accumulator.EngineSamples.Add(new GPUEngineCounterSample(
                instance.EngineIndex,
                engineName,
                item.Value.DoubleValue));
        }

        return true;
    }

    private bool ReadMemoryCounters(IntPtr counter, bool isDedicated)
    {
        if (counter == IntPtr.Zero
            || !TryReadCounterArray(
                counter,
                PdhFormatLarge,
                out PDH_FORMATTED_COUNTER_VALUE_ITEM* items,
                out uint itemCount))
            return false;

        for (uint itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            PDH_FORMATTED_COUNTER_VALUE_ITEM item = items[itemIndex];
            if (!HasValidStatus(item.Value.Status)
                || item.Value.LargeValue < 0
                || item.Name == IntPtr.Zero)
                continue;

            ReadOnlySpan<char> instanceName = ReadNullTerminatedSpan((char*)item.Name);
            if (!GPUAdapterCounterInstanceParser.TryParse(
                    instanceName,
                    out ulong adapterLUID,
                    out int physicalAdapterIndex))
                continue;

            GPUCounterAccumulator accumulator = GetOrCreateAccumulator(
                new GPUAdapterKey(adapterLUID, physicalAdapterIndex));
            ulong memoryBytes = (ulong)item.Value.LargeValue;
            if (isDedicated)
            {
                accumulator.HasDedicatedMemoryData = true;
                accumulator.DedicatedMemoryBytes = SaturatingAdd(accumulator.DedicatedMemoryBytes, memoryBytes);
            }
            else
            {
                accumulator.HasSharedMemoryData = true;
                accumulator.SharedMemoryBytes = SaturatingAdd(accumulator.SharedMemoryBytes, memoryBytes);
            }
        }

        return true;
    }

    private GPUCounterAccumulator GetOrCreateAccumulator(GPUAdapterKey key)
    {
        if (_accumulators.TryGetValue(key, out GPUCounterAccumulator? accumulator)) return accumulator;
        accumulator = new GPUCounterAccumulator();
        _accumulators.Add(key, accumulator);
        return accumulator;
    }

    private bool TryReadCounterArray(
        IntPtr counter,
        uint format,
        out PDH_FORMATTED_COUNTER_VALUE_ITEM* items,
        out uint itemCount)
    {
        uint requiredSize = _counterBufferSize;
        uint status = PdhGetFormattedCounterArrayW(
            counter,
            format,
            ref requiredSize,
            out itemCount,
            _counterBuffer);
        if (status == PdhMoreData)
        {
            EnsureCounterBuffer(requiredSize);
            requiredSize = _counterBufferSize;
            status = PdhGetFormattedCounterArrayW(
                counter,
                format,
                ref requiredSize,
                out itemCount,
                _counterBuffer);
        }

        if (status != PdhSuccess)
        {
            items = null;
            itemCount = 0;
            return false;
        }

        items = (PDH_FORMATTED_COUNTER_VALUE_ITEM*)_counterBuffer;
        return true;
    }

    private void EnsureCounterBuffer(uint requiredSize)
    {
        if (requiredSize <= _counterBufferSize) return;

        uint capacity = Math.Max(val1: 4_096U, _counterBufferSize);
        while (capacity < requiredSize)
            capacity = checked(capacity * 2);
        _counterBuffer = _counterBuffer == IntPtr.Zero
            ? Marshal.AllocHGlobal(checked((int)capacity))
            : Marshal.ReAllocHGlobal(_counterBuffer, checked((IntPtr)capacity));
        _counterBufferSize = capacity;
    }

    private static ReadOnlySpan<char> ReadNullTerminatedSpan(char* value)
    {
        int length = 0;
        while (length < MaximumCounterInstanceNameLength && value[length] != '\0')
            length++;
        return new ReadOnlySpan<char>(value, length);
    }

    /// <summary>Creates a best-effort fallback when Windows exposes no persistent adapter identity.</summary>
    internal static string CreatePCIFallbackDeviceID(
        uint vendorID,
        uint deviceID,
        uint subsystemID,
        uint revision,
        int displayIndex,
        int physicalAdapterIndex) => string.Create(
        CultureInfo.InvariantCulture,
        $"gpu:pci:{vendorID:X4}:{deviceID:X4}:{subsystemID:X8}:{revision:X2}:{displayIndex}:{physicalAdapterIndex}");

    /// <summary>Resolves persistent adapter identities so tuples for one PNP device share one ID.</summary>
    internal static string[] ResolveStableDeviceIDs(IReadOnlyList<GPUDeviceIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);

        string[] deviceIDs = new string[identities.Count];
        for (int identityIndex = 0; identityIndex < identities.Count; identityIndex++)
            deviceIDs[identityIndex] = ResolveStableDeviceID(identities[identityIndex]);

        return deviceIDs;
    }

    /// <summary>Identifies software-enumerated display adapters from their canonical PNP bus.</summary>
    internal static bool IsVirtualDisplayPNPKey(string? hardwarePNPKey)
    {
        string canonicalKey = D3DKMTAdapterIdentityReader.CanonicalizeHardwarePNPKey(hardwarePNPKey);
        if (canonicalKey.Length == 0) return false;

        int separatorIndex = canonicalKey.IndexOf('/');
        ReadOnlySpan<char> enumerator = separatorIndex >= 0
            ? canonicalKey.AsSpan(start: 0, separatorIndex)
            : canonicalKey.AsSpan();
        return enumerator.Equals(other: "root", StringComparison.OrdinalIgnoreCase)
               || enumerator.Equals(other: "swd", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveStableDeviceID(GPUDeviceIdentity identity)
    {
        string hardwarePNPKey = D3DKMTAdapterIdentityReader.CanonicalizeHardwarePNPKey(
            identity.HardwarePNPKey);
        if (hardwarePNPKey.Length > 0)
            return "gpu:pnp:" + hardwarePNPKey;
        if (identity.UniqueAdapterGUID != Guid.Empty)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"gpu:guid:{identity.UniqueAdapterGUID:N}");
        }

        return identity.FallbackDeviceID;
    }

    private static bool HasValidStatus(uint status) => status is PdhValidData or PdhNewData;

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        left > ulong.MaxValue - right ? ulong.MaxValue : left + right;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _accumulators.Clear();
        if (_counterBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_counterBuffer);
            _counterBuffer = IntPtr.Zero;
            _counterBufferSize = 0;
        }

        _query.Dispose();
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(
        IntPtr counter,
        uint format,
        ref uint bufferSize,
        out uint itemCount,
        IntPtr itemBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FORMATTED_COUNTER_VALUE_ITEM
    {
        public IntPtr Name;
        public PDH_FORMATTED_COUNTER_VALUE Value;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PDH_FORMATTED_COUNTER_VALUE
    {
        [FieldOffset(0)]
        public uint Status;

        [FieldOffset(8)]
        public long LargeValue;

        [FieldOffset(8)]
        public double DoubleValue;
    }

    private sealed class GPUCounterAccumulator
    {
        public List<GPUEngineCounterSample> EngineSamples { get; } = [];
        public bool HasDedicatedMemoryData { get; set; }
        public ulong DedicatedMemoryBytes { get; set; }
        public bool HasSharedMemoryData { get; set; }
        public ulong SharedMemoryBytes { get; set; }
    }

    private sealed class MutableEngineSample(string name)
    {
        public string Name { get; } = name;
        public double UtilizationPercent { get; set; }
    }

    private sealed class GPUDeviceGroupBuilder(string deviceID)
    {
        private readonly HashSet<GPUAdapterKey> _keys = [];
        private readonly List<GPUPerformanceEngineSnapshot> _engines = [];
        private GPUAdapterKey _representativeKey;
        private int _displayIndex = int.MaxValue;
        private string _name = "GPU";
        private bool _hasDedicatedMemoryData;
        private ulong _dedicatedMemoryBytes;
        private ulong _dedicatedMemoryCapacityBytes;
        private bool _hasSharedMemoryData;
        private ulong _sharedMemoryBytes;
        private ulong _sharedMemoryCapacityBytes;

        public void Add(
            GPUAdapterKey key,
            GPUAdapterMetadata metadata,
            GPUCounterAccumulator? accumulator)
        {
            if (!_keys.Add(key)) return;

            if (metadata.DisplayIndex < _displayIndex)
            {
                _displayIndex = metadata.DisplayIndex;
                _representativeKey = key;
                _name = metadata.Name.Length > 0 ? metadata.Name : "GPU";
            }

            _dedicatedMemoryCapacityBytes = Math.Max(
                _dedicatedMemoryCapacityBytes,
                metadata.DedicatedMemoryCapacityBytes);
            _sharedMemoryCapacityBytes = Math.Max(
                _sharedMemoryCapacityBytes,
                metadata.SharedMemoryCapacityBytes);
            if (accumulator == null) return;

            GPUPerformanceEngineSnapshot[] keyEngines = AggregateEngineSamples(accumulator.EngineSamples);
            for (int engineIndex = 0; engineIndex < keyEngines.Length; engineIndex++)
                _engines.Add(keyEngines[engineIndex]);

            if (accumulator.HasDedicatedMemoryData)
            {
                _hasDedicatedMemoryData = true;
                _dedicatedMemoryBytes = SaturatingAdd(
                    _dedicatedMemoryBytes,
                    accumulator.DedicatedMemoryBytes);
            }

            if (accumulator.HasSharedMemoryData)
            {
                _hasSharedMemoryData = true;
                _sharedMemoryBytes = SaturatingAdd(
                    _sharedMemoryBytes,
                    accumulator.SharedMemoryBytes);
            }
        }

        public GPUDeviceCandidate CreateCandidate(
            bool hasUtilizationQuery,
            bool hasDedicatedMemoryQuery,
            bool hasSharedMemoryQuery)
        {
            GPUPerformanceEngineSnapshot[] engines = [.. _engines];
            double utilizationPercent = 0;
            for (int engineIndex = 0; engineIndex < engines.Length; engineIndex++)
                utilizationPercent = Math.Max(utilizationPercent, engines[engineIndex].UtilizationPercent);

            return new GPUDeviceCandidate(
                deviceID,
                _representativeKey,
                _displayIndex,
                _name,
                hasUtilizationQuery && engines.Length > 0,
                utilizationPercent,
                engines,
                hasDedicatedMemoryQuery && _hasDedicatedMemoryData,
                _dedicatedMemoryBytes,
                _dedicatedMemoryCapacityBytes,
                hasSharedMemoryQuery && _hasSharedMemoryData,
                _sharedMemoryBytes,
                _sharedMemoryCapacityBytes);
        }
    }

    private readonly record struct GPUDeviceCandidate(
        string DeviceID,
        GPUAdapterKey Key,
        int DisplayIndex,
        string Name,
        bool HasUtilizationSample,
        double UtilizationPercent,
        GPUPerformanceEngineSnapshot[] Engines,
        bool HasDedicatedMemoryData,
        ulong DedicatedMemoryBytes,
        ulong DedicatedMemoryCapacityBytes,
        bool HasSharedMemoryData,
        ulong SharedMemoryBytes,
        ulong SharedMemoryCapacityBytes);
}

internal readonly record struct GPUEngineCounterSample(
    int EngineIndex,
    string Name,
    double UtilizationPercent);

internal readonly record struct GPUAdapterKey(ulong LUID, int PhysicalAdapterIndex);

internal readonly record struct GPUAdapterPersistentIdentity(
    string? HardwarePNPKey,
    Guid UniqueAdapterGUID);

internal readonly record struct GPUDeviceIdentity(
    GPUAdapterKey Key,
    string? HardwarePNPKey,
    Guid UniqueAdapterGUID,
    string FallbackDeviceID);

/// <summary>Maps boot-local adapter LUIDs to persistent graphics-kernel identities.</summary>
internal static unsafe class D3DKMTAdapterIdentityReader
{
    private const int PhysicalAdapterPNPKeyInformation = 41;
    private const int AdapterUniqueGUIDInformation = 60;
    private const int HardwarePNPKey = 1;
    private const int PNPKeyBufferLength = 2_048;
    private const int AdapterUniqueGUIDBufferLength = 40;
    private const string EnumPathSegment = @"\Enum\";
    private const string DeviceParametersSuffix = @"\Device Parameters";

    /// <summary>Reads the physical PNP key and unique adapter GUID for one adapter key.</summary>
    public static GPUAdapterPersistentIdentity Read(GPUAdapterKey key)
    {
        if (!D3DKMT.TryOpenAdapter(key.LUID, out uint adapterHandle)) return default;

        try
        {
            string hardwarePNPKey = ReadHardwarePNPKey(adapterHandle, key.PhysicalAdapterIndex);
            Guid uniqueAdapterGUID = ReadUniqueAdapterGUID(adapterHandle);
            return new GPUAdapterPersistentIdentity(hardwarePNPKey, uniqueAdapterGUID);
        }
        finally
        {
            D3DKMT.CloseAdapter(adapterHandle);
        }
    }

    /// <summary>Removes volatile registry-control-set components from a hardware PNP key.</summary>
    internal static string CanonicalizeHardwarePNPKey(string? hardwarePNPKey)
    {
        if (string.IsNullOrWhiteSpace(hardwarePNPKey)) return string.Empty;

        string canonicalKey = hardwarePNPKey.Trim().Replace(oldChar: '/', newChar: '\\');
        int enumPathIndex = canonicalKey.IndexOf(EnumPathSegment, StringComparison.OrdinalIgnoreCase);
        if (enumPathIndex >= 0)
            canonicalKey = canonicalKey[(enumPathIndex + EnumPathSegment.Length)..];
        if (canonicalKey.EndsWith(DeviceParametersSuffix, StringComparison.OrdinalIgnoreCase))
            canonicalKey = canonicalKey[..^DeviceParametersSuffix.Length];

        return canonicalKey
            .Trim('\\')
            .Replace(oldChar: '\\', newChar: '/')
            .ToLowerInvariant();
    }

    private static string ReadHardwarePNPKey(uint adapterHandle, int physicalAdapterIndex)
    {
        if (physicalAdapterIndex < 0) return string.Empty;

        char[] destination = new char[PNPKeyBufferLength];
        uint destinationLength = (uint)destination.Length;
        fixed (char* destinationPointer = destination)
        {
            D3DKMT_QUERY_PHYSICAL_ADAPTER_PNP_KEY pnpKeyQuery = new()
            {
                PhysicalAdapterIndex = (uint)physicalAdapterIndex,
                PNPKeyType = HardwarePNPKey,
                Destination = (IntPtr)destinationPointer,
                DestinationCharacterCount = (IntPtr)(&destinationLength)
            };
            if (!D3DKMT.TryQueryAdapterInfo(adapterHandle, PhysicalAdapterPNPKeyInformation, ref pnpKeyQuery))
                return string.Empty;
        }

        return CanonicalizeHardwarePNPKey(ReadNullTerminatedString(destination));
    }

    private static Guid ReadUniqueAdapterGUID(uint adapterHandle)
    {
        char[] destination = new char[AdapterUniqueGUIDBufferLength];
        fixed (char* destinationPointer = destination)
        {
            if (!D3DKMT.TryQueryAdapterInfo(
                    adapterHandle,
                    AdapterUniqueGUIDInformation,
                    destinationPointer,
                    checked((uint)(destination.Length * sizeof(char)))))
                return Guid.Empty;
        }

        string uniqueGUID = ReadNullTerminatedString(destination);
        return Guid.TryParse(uniqueGUID, out Guid parsedGUID) ? parsedGUID : Guid.Empty;
    }

    private static string ReadNullTerminatedString(char[] value)
    {
        int length = Array.IndexOf(value, value: '\0');
        if (length < 0) length = value.Length;
        return length == 0 ? string.Empty : new string(value, startIndex: 0, length);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERY_PHYSICAL_ADAPTER_PNP_KEY
    {
        public uint PhysicalAdapterIndex;
        public int PNPKeyType;
        public IntPtr Destination;
        public IntPtr DestinationCharacterCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_CLOSEADAPTER
    {
        public uint AdapterHandle;
    }
}

/// <summary>Parses raw GPU Adapter Memory counter identities without allocating substrings.</summary>
internal static class GPUAdapterCounterInstanceParser
{
    private const string LUIDPrefix = "luid_0x";
    private const string LUIDPartDelimiter = "_0x";
    private const string PhysicalAdapterDelimiter = "_phys_";

    public static bool TryParse(
        ReadOnlySpan<char> value,
        out ulong adapterLUID,
        out int physicalAdapterIndex)
    {
        adapterLUID = 0;
        physicalAdapterIndex = -1;
        if (!value.StartsWith(LUIDPrefix, StringComparison.OrdinalIgnoreCase)) return false;

        int position = LUIDPrefix.Length;
        int highEnd = value[position..].IndexOf(LUIDPartDelimiter, StringComparison.OrdinalIgnoreCase);
        if (highEnd <= 0
            || !uint.TryParse(
                value.Slice(position, highEnd),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out uint highLUID))
            return false;

        position += highEnd + LUIDPartDelimiter.Length;
        int lowEnd = value[position..].IndexOf(PhysicalAdapterDelimiter, StringComparison.OrdinalIgnoreCase);
        if (lowEnd <= 0
            || !uint.TryParse(
                value.Slice(position, lowEnd),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out uint lowLUID))
            return false;

        position += lowEnd + PhysicalAdapterDelimiter.Length;
        int suffixIndex = value[position..].IndexOf('#');
        ReadOnlySpan<char> physicalAdapter = suffixIndex >= 0
            ? value.Slice(position, suffixIndex)
            : value[position..];
        if (!int.TryParse(
                physicalAdapter,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out physicalAdapterIndex)
            || physicalAdapterIndex < 0)
        {
            physicalAdapterIndex = -1;
            return false;
        }

        adapterLUID = ((ulong)highLUID << 32) | lowLUID;
        return true;
    }
}

internal static class DXGIAdapterEnumerator
{
    public static GPUAdapterMetadata[] Enumerate()
    {
        List<DXGI.HardwareAdapter> hardwareAdapters = DXGI.EnumerateHardwareAdapters();
        GPUAdapterMetadata[] adapters = new GPUAdapterMetadata[hardwareAdapters.Count];
        for (int adapterIndex = 0; adapterIndex < adapters.Length; adapterIndex++)
        {
            DXGI.HardwareAdapter adapter = hardwareAdapters[adapterIndex];
            DXGI.DXGI_ADAPTER_DESC1 description = adapter.Description;
            adapters[adapterIndex] = new GPUAdapterMetadata(
                description.AdapterLUID.ToUInt64(),
                checked((int)adapter.Index),
                description.ReadDescription(),
                description.VendorID,
                description.DeviceID,
                description.SubsystemID,
                description.Revision,
                description.DedicatedVideoMemory,
                description.SharedSystemMemory,
                HasValue: true);
        }

        return adapters;
    }
}

internal readonly record struct GPUAdapterMetadata(
    ulong LUID,
    int DisplayIndex,
    string Name,
    uint VendorID,
    uint DeviceID,
    uint SubsystemID,
    uint Revision,
    ulong DedicatedMemoryCapacityBytes,
    ulong SharedMemoryCapacityBytes,
    bool HasValue);
