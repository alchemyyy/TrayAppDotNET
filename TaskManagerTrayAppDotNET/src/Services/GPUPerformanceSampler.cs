using System.Globalization;
using System.Runtime.InteropServices;

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

    private readonly Dictionary<GPUAdapterKey, GPUCounterAccumulator> _accumulators = [];
    private IntPtr _query;
    private IntPtr _utilizationCounter;
    private IntPtr _dedicatedMemoryCounter;
    private IntPtr _sharedMemoryCounter;
    private IntPtr _counterBuffer;
    private uint _counterBufferSize;
    private bool _queryPrimed;
    private bool _disposed;

    /// <summary>Forces the next PDH collection to establish a fresh one-second baseline.</summary>
    internal void ResetCounterBaseline()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _queryPrimed = false;
        _accumulators.Clear();
    }

    public GPUPerformanceSampler()
    {
        if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != PdhSuccess) return;

        _utilizationCounter = AddCounter(UtilizationPath);
        _dedicatedMemoryCounter = AddCounter(DedicatedMemoryPath);
        _sharedMemoryCounter = AddCounter(SharedMemoryPath);
        if (_utilizationCounter != IntPtr.Zero
            || _dedicatedMemoryCounter != IntPtr.Zero
            || _sharedMemoryCounter != IntPtr.Zero)
        {
            return;
        }

        _ = PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }

    /// <summary>Captures one card per hardware PNP display device and attaches raw PDH values.</summary>
    public GPUPerformanceSnapshot[] Sample()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _accumulators.Clear();

        GPUAdapterMetadata[] adapters = DXGIAdapterEnumerator.Enumerate();
        bool hasUtilizationQuery = false;
        bool hasDedicatedMemoryQuery = false;
        bool hasSharedMemoryQuery = false;
        if (_query != IntPtr.Zero && PdhCollectQueryData(_query) == PdhSuccess)
        {
            bool canReadFormattedValues = _queryPrimed;
            _queryPrimed = true;
            if (canReadFormattedValues)
            {
                hasUtilizationQuery = ReadUtilizationCounters();
                hasDedicatedMemoryQuery = ReadMemoryCounters(_dedicatedMemoryCounter, true);
                hasSharedMemoryQuery = ReadMemoryCounters(_sharedMemoryCounter, false);
            }
        }

        Dictionary<ulong, GPUAdapterMetadata> metadataByLUID = [];
        for (int adapterIndex = 0; adapterIndex < adapters.Length; adapterIndex++)
        {
            GPUAdapterMetadata adapter = adapters[adapterIndex];
            if (!metadataByLUID.TryGetValue(adapter.LUID, out GPUAdapterMetadata existing)
                || adapter.DisplayIndex < existing.DisplayIndex)
            {
                metadataByLUID[adapter.LUID] = adapter;
            }
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
                baseIdentity = D3DKMTAdapterIdentityReader.Read(new GPUAdapterKey(key.LUID, 0));
                baseIdentitiesByLUID.Add(key.LUID, baseIdentity);
            }

            GPUAdapterPersistentIdentity physicalIdentity = key.PhysicalAdapterIndex == 0
                ? baseIdentity
                : D3DKMTAdapterIdentityReader.Read(key);
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
                0);
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
            {
                continue;
            }

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
                Math.Clamp(pair.Value.UtilizationPercent, 0, 100));
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
            resultKeys.Add(new GPUAdapterKey(adapters[adapterIndex].LUID, 0));

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

    private IntPtr AddCounter(string path)
    {
        uint status = PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out IntPtr counter);
        return status == PdhSuccess ? counter : IntPtr.Zero;
    }

    private bool ReadUtilizationCounters()
    {
        if (_utilizationCounter == IntPtr.Zero
            || !TryReadCounterArray(
                _utilizationCounter,
                PdhFormatDouble | PdhFormatNoCap100,
                out PDH_FORMATTED_COUNTER_VALUE_ITEM* items,
                out uint itemCount))
        {
            return false;
        }

        for (uint itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            PDH_FORMATTED_COUNTER_VALUE_ITEM item = items[itemIndex];
            if (!HasValidStatus(item.Value.Status)
                || !double.IsFinite(item.Value.DoubleValue)
                || item.Value.DoubleValue < 0
                || item.Name == IntPtr.Zero)
            {
                continue;
            }

            ReadOnlySpan<char> instanceName = ReadNullTerminatedSpan((char*)item.Name);
            if (!AcceleratorCounterInstanceParser.TryParseEngine(
                    instanceName,
                    out AcceleratorCounterInstance instance))
            {
                continue;
            }

            GPUAdapterKey adapterKey = new(instance.AdapterLUID, instance.PhysicalAdapterIndex);
            GPUCounterAccumulator accumulator = GetOrCreateAccumulator(adapterKey);
            string engineName = NormalizeEngineType(instanceName[instance.EngineTypeStart..]);
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
        {
            return false;
        }

        for (uint itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            PDH_FORMATTED_COUNTER_VALUE_ITEM item = items[itemIndex];
            if (!HasValidStatus(item.Value.Status)
                || item.Value.LargeValue < 0
                || item.Name == IntPtr.Zero)
            {
                continue;
            }

            ReadOnlySpan<char> instanceName = ReadNullTerminatedSpan((char*)item.Name);
            if (!GPUAdapterCounterInstanceParser.TryParse(
                    instanceName,
                    out ulong adapterLUID,
                    out int physicalAdapterIndex))
            {
                continue;
            }

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

        uint capacity = Math.Max(4_096U, _counterBufferSize);
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

    private static string NormalizeEngineType(ReadOnlySpan<char> engineType)
    {
        int duplicateSuffixIndex = engineType.IndexOf('#');
        if (duplicateSuffixIndex >= 0)
            engineType = engineType[..duplicateSuffixIndex];
        if (engineType.Equals("3d", StringComparison.OrdinalIgnoreCase)) return "3D";
        if (engineType.Equals("copy", StringComparison.OrdinalIgnoreCase)) return "Copy";
        if (engineType.Equals("compute", StringComparison.OrdinalIgnoreCase)) return "Compute";
        if (engineType.Equals("videodecode", StringComparison.OrdinalIgnoreCase)) return "Video Decode";
        if (engineType.Equals("videoencode", StringComparison.OrdinalIgnoreCase)) return "Video Encode";
        if (engineType.Equals("videoprocessing", StringComparison.OrdinalIgnoreCase)) return "Video Processing";
        if (engineType.Equals("opticalflow", StringComparison.OrdinalIgnoreCase)) return "Optical Flow";
        return engineType.ToString();
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
            ? canonicalKey.AsSpan(0, separatorIndex)
            : canonicalKey.AsSpan();
        return enumerator.Equals("root", StringComparison.OrdinalIgnoreCase)
               || enumerator.Equals("swd", StringComparison.OrdinalIgnoreCase);
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

        if (_query == IntPtr.Zero) return;
        _ = PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(
        IntPtr query,
        string fullCounterPath,
        IntPtr userData,
        out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(
        IntPtr counter,
        uint format,
        ref uint bufferSize,
        out uint itemCount,
        IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

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
        D3DKMT_OPENADAPTERFROMLUID openAdapter = new()
        {
            AdapterLUID = new NATIVE_LUID
            {
                LowPart = (uint)key.LUID,
                HighPart = unchecked((int)(key.LUID >> 32))
            }
        };

        int openStatus;
        try
        {
            openStatus = D3DKMTOpenAdapterFromLuid(ref openAdapter);
        }
        catch (Exception exception) when (exception is DllNotFoundException
                                          or EntryPointNotFoundException
                                          or BadImageFormatException)
        {
            return default;
        }

        if (openStatus < 0 || openAdapter.AdapterHandle == 0) return default;

        try
        {
            string hardwarePNPKey = ReadHardwarePNPKey(
                openAdapter.AdapterHandle,
                key.PhysicalAdapterIndex);
            Guid uniqueAdapterGUID = ReadUniqueAdapterGUID(openAdapter.AdapterHandle);
            return new GPUAdapterPersistentIdentity(hardwarePNPKey, uniqueAdapterGUID);
        }
        finally
        {
            D3DKMT_CLOSEADAPTER closeAdapter = new()
            {
                AdapterHandle = openAdapter.AdapterHandle
            };
            _ = D3DKMTCloseAdapter(ref closeAdapter);
        }
    }

    /// <summary>Removes volatile registry-control-set components from a hardware PNP key.</summary>
    internal static string CanonicalizeHardwarePNPKey(string? hardwarePNPKey)
    {
        if (string.IsNullOrWhiteSpace(hardwarePNPKey)) return string.Empty;

        string canonicalKey = hardwarePNPKey.Trim().Replace('/', '\\');
        int enumPathIndex = canonicalKey.IndexOf(EnumPathSegment, StringComparison.OrdinalIgnoreCase);
        if (enumPathIndex >= 0)
            canonicalKey = canonicalKey[(enumPathIndex + EnumPathSegment.Length)..];
        if (canonicalKey.EndsWith(DeviceParametersSuffix, StringComparison.OrdinalIgnoreCase))
            canonicalKey = canonicalKey[..^DeviceParametersSuffix.Length];

        return canonicalKey
            .Trim('\\')
            .Replace('\\', '/')
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
            D3DKMT_QUERYADAPTERINFO query = new()
            {
                AdapterHandle = adapterHandle,
                Type = PhysicalAdapterPNPKeyInformation,
                PrivateDriverData = (IntPtr)(&pnpKeyQuery),
                PrivateDriverDataSize = (uint)sizeof(D3DKMT_QUERY_PHYSICAL_ADAPTER_PNP_KEY)
            };
            if (D3DKMTQueryAdapterInfo(ref query) < 0) return string.Empty;
        }

        return CanonicalizeHardwarePNPKey(ReadNullTerminatedString(destination));
    }

    private static Guid ReadUniqueAdapterGUID(uint adapterHandle)
    {
        char[] destination = new char[AdapterUniqueGUIDBufferLength];
        fixed (char* destinationPointer = destination)
        {
            D3DKMT_QUERYADAPTERINFO query = new()
            {
                AdapterHandle = adapterHandle,
                Type = AdapterUniqueGUIDInformation,
                PrivateDriverData = (IntPtr)destinationPointer,
                PrivateDriverDataSize = checked((uint)(destination.Length * sizeof(char)))
            };
            if (D3DKMTQueryAdapterInfo(ref query) < 0) return Guid.Empty;
        }

        string uniqueGUID = ReadNullTerminatedString(destination);
        return Guid.TryParse(uniqueGUID, out Guid parsedGUID) ? parsedGUID : Guid.Empty;
    }

    private static string ReadNullTerminatedString(char[] value)
    {
        int length = Array.IndexOf(value, '\0');
        if (length < 0) length = value.Length;
        return length == 0 ? string.Empty : new string(value, 0, length);
    }

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTOpenAdapterFromLuid(
        ref D3DKMT_OPENADAPTERFROMLUID openAdapter);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryAdapterInfo(
        ref D3DKMT_QUERYADAPTERINFO queryAdapterInfo);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(
        ref D3DKMT_CLOSEADAPTER closeAdapter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NATIVE_LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_OPENADAPTERFROMLUID
    {
        public NATIVE_LUID AdapterLUID;
        public uint AdapterHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERYADAPTERINFO
    {
        public uint AdapterHandle;
        public int Type;
        public IntPtr PrivateDriverData;
        public uint PrivateDriverDataSize;
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
        {
            return false;
        }

        position += highEnd + LUIDPartDelimiter.Length;
        int lowEnd = value[position..].IndexOf(PhysicalAdapterDelimiter, StringComparison.OrdinalIgnoreCase);
        if (lowEnd <= 0
            || !uint.TryParse(
                value.Slice(position, lowEnd),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out uint lowLUID))
        {
            return false;
        }

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

internal static unsafe class DXGIAdapterEnumerator
{
    private const int DXGIErrorNotFound = unchecked((int)0x887A0002);
    private const uint DXGIAdapterFlagSoftware = 0x2;
    private static readonly Guid FactoryInterfaceID = new("770aae78-f26f-4dba-a829-253c83d1b387");

    public static GPUAdapterMetadata[] Enumerate()
    {
        Guid interfaceID = FactoryInterfaceID;
        int result;
        IntPtr factory;
        try
        {
            result = CreateDXGIFactory1(ref interfaceID, out factory);
        }
        catch (Exception exception) when (exception is DllNotFoundException
                                          or EntryPointNotFoundException
                                          or BadImageFormatException)
        {
            return [];
        }

        if (result < 0 || factory == IntPtr.Zero) return [];

        List<GPUAdapterMetadata> adapters = [];
        try
        {
            IntPtr* factoryVTable = *(IntPtr**)factory;
            delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int> enumerateAdapters =
                (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)factoryVTable[12];
            for (uint adapterIndex = 0; adapterIndex < 256; adapterIndex++)
            {
                IntPtr adapter = IntPtr.Zero;
                int enumerateResult = enumerateAdapters(factory, adapterIndex, &adapter);
                if (enumerateResult == DXGIErrorNotFound) break;
                if (enumerateResult < 0 || adapter == IntPtr.Zero) continue;

                try
                {
                    IntPtr* adapterVTable = *(IntPtr**)adapter;
                    delegate* unmanaged[Stdcall]<IntPtr, DXGI_ADAPTER_DESC1*, int> getDescription =
                        (delegate* unmanaged[Stdcall]<IntPtr, DXGI_ADAPTER_DESC1*, int>)adapterVTable[10];
                    DXGI_ADAPTER_DESC1 description = default;
                    if (getDescription(adapter, &description) < 0
                        || (description.Flags & DXGIAdapterFlagSoftware) != 0)
                    {
                        continue;
                    }

                    adapters.Add(new GPUAdapterMetadata(
                        ToLUID(description.AdapterLUID),
                        checked((int)adapterIndex),
                        ReadDescription(description),
                        description.VendorID,
                        description.DeviceID,
                        description.SubsystemID,
                        description.Revision,
                        description.DedicatedVideoMemory,
                        description.SharedSystemMemory,
                        true));
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }

        return [.. adapters];
    }

    private static string ReadDescription(DXGI_ADAPTER_DESC1 description)
    {
        char* descriptionPointer = description.Description;
        return new string(descriptionPointer).Trim();
    }

    private static ulong ToLUID(NATIVE_LUID value) =>
        ((ulong)(uint)value.HighPart << 32) | value.LowPart;

    private static void Release(IntPtr instance)
    {
        IntPtr* vTable = *(IntPtr**)instance;
        delegate* unmanaged[Stdcall]<IntPtr, uint> release =
            (delegate* unmanaged[Stdcall]<IntPtr, uint>)vTable[2];
        _ = release(instance);
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid interfaceID, out IntPtr factory);

    [StructLayout(LayoutKind.Sequential)]
    private struct NATIVE_LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        public fixed char Description[128];
        public uint VendorID;
        public uint DeviceID;
        public uint SubsystemID;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public NATIVE_LUID AdapterLUID;
        public uint Flags;
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
