using System.Globalization;
using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Batches visible GPU and NPU process counters through one persistent PDH query.</summary>
internal sealed unsafe class AcceleratorPerformanceSampler : IDisposable
{
    private const string UtilizationPath = @"\GPU Engine(*)\Utilization Percentage";
    private const string DedicatedMemoryPath = @"\GPU Process Memory(*)\Dedicated Usage";
    private const string SharedMemoryPath = @"\GPU Process Memory(*)\Shared Usage";
    private const uint PdhSuccess = 0;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhValidData = 0;
    private const uint PdhNewData = 1;
    private const uint PdhFormatDouble = 0x00000200;
    private const uint PdhFormatLarge = 0x00000400;
    private const uint PdhFormatNoCap100 = 0x00008000;
    private const int MaximumCounterInstanceNameLength = 1_024;

    private readonly bool _needsGPU;
    private readonly bool _needsNPU;
    private readonly bool _needsGPUEngine;
    private readonly bool _needsNPUEngine;
    private readonly Dictionary<int, ProcessAcceleratorSample> _samples = new(256);
    private readonly AcceleratorAdapterClassifier _adapterClassifier = new();
    private IntPtr _query;
    private IntPtr _utilizationCounter;
    private IntPtr _dedicatedMemoryCounter;
    private IntPtr _sharedMemoryCounter;
    private IntPtr _counterBuffer;
    private uint _counterBufferSize;
    private bool _disposed;

    public AcceleratorPerformanceSampler(
        bool needsGPU,
        bool needsNPU,
        bool needsUtilization,
        bool needsGPUEngine,
        bool needsNPUEngine,
        bool needsDedicatedMemory,
        bool needsSharedMemory)
    {
        _needsGPU = needsGPU;
        _needsNPU = needsNPU;
        _needsGPUEngine = needsGPUEngine;
        _needsNPUEngine = needsNPUEngine;

        if (PdhOpenQueryW(dataSource: null, IntPtr.Zero, out _query) != PdhSuccess) return;
        if (needsUtilization || needsGPUEngine || needsNPUEngine)
            _utilizationCounter = AddCounter(UtilizationPath);
        if (needsDedicatedMemory)
            _dedicatedMemoryCounter = AddCounter(DedicatedMemoryPath);
        if (needsSharedMemory)
            _sharedMemoryCounter = AddCounter(SharedMemoryPath);

        if (_utilizationCounter != IntPtr.Zero
            || _dedicatedMemoryCounter != IntPtr.Zero
            || _sharedMemoryCounter != IntPtr.Zero)
            return;

        _ = PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }

    public bool HasUtilizationData { get; private set; }
    public bool HasDedicatedMemoryData { get; private set; }
    public bool HasSharedMemoryData { get; private set; }

    /// <summary>Collects once, then retains only samples required by the viewport or active sort.</summary>
    public void Sample(int[] processIDs, int processCount, bool sampleEveryProcess)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(processIDs);
        if ((uint)processCount > (uint)processIDs.Length)
            throw new ArgumentOutOfRangeException(nameof(processCount));

        _samples.Clear();
        HasUtilizationData = false;
        HasDedicatedMemoryData = false;
        HasSharedMemoryData = false;
        if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != PdhSuccess) return;

        if (_utilizationCounter != IntPtr.Zero
            && TryReadCounterArray(
                _utilizationCounter,
                PdhFormatDouble | PdhFormatNoCap100,
                out PDH_FORMATTED_COUNTER_VALUE_ITEM* utilizationItems,
                out uint utilizationItemCount))
        {
            HasUtilizationData = true;
            ReadUtilizationItems(
                utilizationItems,
                utilizationItemCount,
                processIDs,
                processCount,
                sampleEveryProcess);
        }

        if (_dedicatedMemoryCounter != IntPtr.Zero
            && TryReadCounterArray(
                _dedicatedMemoryCounter,
                PdhFormatLarge,
                out PDH_FORMATTED_COUNTER_VALUE_ITEM* dedicatedItems,
                out uint dedicatedItemCount))
        {
            HasDedicatedMemoryData = true;
            ReadMemoryItems(
                dedicatedItems,
                dedicatedItemCount,
                processIDs,
                processCount,
                sampleEveryProcess,
                isDedicated: true);
        }

        if (_sharedMemoryCounter != IntPtr.Zero
            && TryReadCounterArray(
                _sharedMemoryCounter,
                PdhFormatLarge,
                out PDH_FORMATTED_COUNTER_VALUE_ITEM* sharedItems,
                out uint sharedItemCount))
        {
            HasSharedMemoryData = true;
            ReadMemoryItems(
                sharedItems,
                sharedItemCount,
                processIDs,
                processCount,
                sampleEveryProcess,
                isDedicated: false);
        }
    }

    public bool TryGetSample(int processID, out ProcessAcceleratorSample sample) =>
        _samples.TryGetValue(processID, out sample);

    private IntPtr AddCounter(string path)
    {
        uint status = PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out IntPtr counter);
        return status == PdhSuccess ? counter : IntPtr.Zero;
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

    private void ReadUtilizationItems(
        PDH_FORMATTED_COUNTER_VALUE_ITEM* items,
        uint itemCount,
        int[] processIDs,
        int processCount,
        bool sampleEveryProcess)
    {
        for (uint itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            PDH_FORMATTED_COUNTER_VALUE_ITEM item = items[itemIndex];
            if (!HasValidStatus(item.Value.Status)
                || double.IsNaN(item.Value.DoubleValue)
                || double.IsInfinity(item.Value.DoubleValue)
                || item.Name == IntPtr.Zero)
                continue;

            ReadOnlySpan<char> instanceName = ReadNullTerminatedSpan((char*)item.Name);
            if (!AcceleratorCounterInstanceParser.TryParseEngine(instanceName, out AcceleratorCounterInstance instance)
                || !ShouldSampleProcess(instance.ProcessID, processIDs, processCount, sampleEveryProcess))
                continue;

            AcceleratorAdapter descriptor = _adapterClassifier.GetAdapter(instance.AdapterLUID);
            if (!NeedsAdapter(descriptor.Kind)) continue;

            double utilization = Math.Clamp(item.Value.DoubleValue, min: 0, max: 100);
            _samples.TryGetValue(instance.ProcessID, out ProcessAcceleratorSample sample);
            switch (descriptor.Kind)
            {
                case AcceleratorKind.GPU:
                    if (!sample.HasGPUUtilization || utilization > sample.GPUUtilization)
                    {
                        sample.HasGPUUtilization = true;
                        sample.GPUUtilization = utilization;
                        if (_needsGPUEngine)
                        {
                            sample.GPUEngine = _adapterClassifier.GetEngineLabel(
                                descriptor,
                                instance,
                                instanceName[instance.EngineTypeStart..]);
                        }
                    }

                    break;
                case AcceleratorKind.NPU:
                    if (!sample.HasNPUUtilization || utilization > sample.NPUUtilization)
                    {
                        sample.HasNPUUtilization = true;
                        sample.NPUUtilization = utilization;
                        if (_needsNPUEngine)
                        {
                            sample.NPUEngine = _adapterClassifier.GetEngineLabel(
                                descriptor,
                                instance,
                                instanceName[instance.EngineTypeStart..]);
                        }
                    }

                    break;
            }

            _samples[instance.ProcessID] = sample;
        }
    }

    private void ReadMemoryItems(
        PDH_FORMATTED_COUNTER_VALUE_ITEM* items,
        uint itemCount,
        int[] processIDs,
        int processCount,
        bool sampleEveryProcess,
        bool isDedicated)
    {
        for (uint itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            PDH_FORMATTED_COUNTER_VALUE_ITEM item = items[itemIndex];
            if (!HasValidStatus(item.Value.Status)
                || item.Value.LargeValue < 0
                || item.Name == IntPtr.Zero)
                continue;

            ReadOnlySpan<char> instanceName = ReadNullTerminatedSpan((char*)item.Name);
            if (!AcceleratorCounterInstanceParser.TryParseMemory(instanceName, out AcceleratorCounterInstance instance)
                || !ShouldSampleProcess(instance.ProcessID, processIDs, processCount, sampleEveryProcess))
                continue;

            AcceleratorAdapter descriptor = _adapterClassifier.GetAdapter(instance.AdapterLUID);
            if (!NeedsAdapter(descriptor.Kind)) continue;

            _samples.TryGetValue(instance.ProcessID, out ProcessAcceleratorSample sample);
            switch (descriptor.Kind, isDedicated)
            {
                case (AcceleratorKind.GPU, true):
                    sample.DedicatedGPUMemory = SaturatingAdd(
                        sample.DedicatedGPUMemory,
                        item.Value.LargeValue);
                    break;
                case (AcceleratorKind.GPU, false):
                    sample.SharedGPUMemory = SaturatingAdd(sample.SharedGPUMemory, item.Value.LargeValue);
                    break;
                case (AcceleratorKind.NPU, true):
                    sample.DedicatedNPUMemory = SaturatingAdd(
                        sample.DedicatedNPUMemory,
                        item.Value.LargeValue);
                    break;
                case (AcceleratorKind.NPU, false):
                    sample.SharedNPUMemory = SaturatingAdd(sample.SharedNPUMemory, item.Value.LargeValue);
                    break;
            }

            _samples[instance.ProcessID] = sample;
        }
    }

    private bool NeedsAdapter(AcceleratorKind kind) => kind switch
    {
        AcceleratorKind.GPU => _needsGPU,
        AcceleratorKind.NPU => _needsNPU,
        _ => false
    };

    private static bool ShouldSampleProcess(
        int processID,
        int[] processIDs,
        int processCount,
        bool sampleEveryProcess)
    {
        if (sampleEveryProcess) return true;

        int lowerBound = 0;
        int upperBound = processCount - 1;
        while (lowerBound <= upperBound)
        {
            int middle = lowerBound + (upperBound - lowerBound) / 2;
            int candidate = processIDs[middle];
            if (candidate == processID) return true;
            if (candidate < processID)
                lowerBound = middle + 1;
            else
                upperBound = middle - 1;
        }

        return false;
    }

    private static ReadOnlySpan<char> ReadNullTerminatedSpan(char* value)
    {
        int length = 0;
        while (length < MaximumCounterInstanceNameLength && value[length] != '\0')
            length++;
        return new ReadOnlySpan<char>(value, length);
    }

    private static bool HasValidStatus(uint status) => status is PdhValidData or PdhNewData;

    private static long SaturatingAdd(long left, long right) =>
        right > long.MaxValue - left ? long.MaxValue : left + right;

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _samples.Clear();
        _adapterClassifier.Dispose();
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
}

internal struct ProcessAcceleratorSample
{
    public double GPUUtilization;
    public double NPUUtilization;
    public long DedicatedGPUMemory;
    public long SharedGPUMemory;
    public long DedicatedNPUMemory;
    public long SharedNPUMemory;
    public string? GPUEngine;
    public string? NPUEngine;
    public bool HasGPUUtilization;
    public bool HasNPUUtilization;
}

internal readonly record struct AcceleratorCounterInstance(
    int ProcessID,
    ulong AdapterLUID,
    int PhysicalAdapterIndex,
    int EngineIndex,
    int EngineTypeStart);

/// <summary>Allocation-free parser for GPU performance-counter instance identities.</summary>
internal static class AcceleratorCounterInstanceParser
{
    private const string ProcessPrefix = "pid_";
    private const string LUIDDelimiter = "_luid_0x";
    private const string LowLUIDDelimiter = "_0x";
    private const string PhysicalAdapterDelimiter = "_phys_";
    private const string EngineDelimiter = "_eng_";
    private const string EngineTypeDelimiter = "_engtype_";

    public static bool TryParseEngine(ReadOnlySpan<char> value, out AcceleratorCounterInstance instance) =>
        TryParse(value, hasEngine: true, out instance);

    public static bool TryParseMemory(ReadOnlySpan<char> value, out AcceleratorCounterInstance instance) =>
        TryParse(value, hasEngine: false, out instance);

    private static bool TryParse(
        ReadOnlySpan<char> value,
        bool hasEngine,
        out AcceleratorCounterInstance instance)
    {
        instance = default;
        if (!value.StartsWith(ProcessPrefix, StringComparison.OrdinalIgnoreCase)) return false;

        int position = ProcessPrefix.Length;
        if (!TryReadDecimal(value, ref position, LUIDDelimiter, out int processID)
            || !TryReadHexadecimal(value, ref position, LowLUIDDelimiter, out uint highLUID)
            || !TryReadHexadecimal(value, ref position, PhysicalAdapterDelimiter, out uint lowLUID))
            return false;

        int physicalAdapterIndex;
        int engineIndex = -1;
        int engineTypeStart = -1;
        if (hasEngine)
        {
            if (!TryReadDecimal(value, ref position, EngineDelimiter, out physicalAdapterIndex)
                || !TryReadDecimal(value, ref position, EngineTypeDelimiter, out engineIndex)
                || position >= value.Length)
                return false;

            engineTypeStart = position;
        }
        else if (!int.TryParse(
                     value[position..],
                     NumberStyles.None,
                     CultureInfo.InvariantCulture,
                     out physicalAdapterIndex))
            return false;

        ulong adapterLUID = ((ulong)highLUID << 32) | lowLUID;
        instance = new AcceleratorCounterInstance(
            processID,
            adapterLUID,
            physicalAdapterIndex,
            engineIndex,
            engineTypeStart);
        return true;
    }

    private static bool TryReadDecimal(
        ReadOnlySpan<char> value,
        ref int position,
        string delimiter,
        out int result)
    {
        int delimiterOffset = value[position..].IndexOf(delimiter, StringComparison.OrdinalIgnoreCase);
        if (delimiterOffset < 1)
        {
            result = 0;
            return false;
        }

        ReadOnlySpan<char> number = value.Slice(position, delimiterOffset);
        position += delimiterOffset + delimiter.Length;
        return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryReadHexadecimal(
        ReadOnlySpan<char> value,
        ref int position,
        string delimiter,
        out uint result)
    {
        int delimiterOffset = value[position..].IndexOf(delimiter, StringComparison.OrdinalIgnoreCase);
        if (delimiterOffset < 1)
        {
            result = 0;
            return false;
        }

        ReadOnlySpan<char> number = value.Slice(position, delimiterOffset);
        position += delimiterOffset + delimiter.Length;
        return uint.TryParse(number, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
    }
}

internal enum AcceleratorKind : byte
{
    GPU,
    NPU
}

internal readonly record struct AcceleratorAdapter(ulong LUID, AcceleratorKind Kind, int DisplayIndex);

/// <summary>Classifies counter LUIDs through DXCore and shares engine labels across every process.</summary>
internal sealed unsafe class AcceleratorAdapterClassifier : IDisposable
{
    private static readonly Guid FactoryInterfaceID = new("78ee5945-c36e-4b13-a669-005dd11c0f06");
    private static readonly Guid AdapterInterfaceID = new("f0db4c7f-fe5a-42a2-bd62-f2a6cf6fc83e");
    private static readonly Guid NPUAttribute = new("d46140c4-add7-451b-9e56-06fe8c3b58ed");
    private static readonly Guid MediaAcceleratorAttribute = new("66bdb96a-050b-44c7-a4fd-d144ce0ab443");
    private static readonly Guid CoreComputeAttribute = new("248e2800-a793-4724-abaa-23a6de1be090");
    private static readonly Guid GenericMachineLearningAttribute = new("b71b0d41-1088-422f-a27c-0250b7d3a988");
    private static readonly Guid D3D12GraphicsAttribute = new("0c9ece4d-2f6e-4f01-8c96-e89e331b47b1");

    private readonly Dictionary<ulong, AcceleratorAdapter> _adapters = new(8);
    private readonly Dictionary<AcceleratorEngineKey, string> _engineLabels = new(64);
    private IntPtr _factory;
    private int _nextGPUDisplayIndex;
    private int _nextNPUDisplayIndex;
    private bool _disposed;

    public AcceleratorAdapterClassifier()
    {
        try
        {
            Guid interfaceID = FactoryInterfaceID;
            if (DXCoreCreateAdapterFactory(ref interfaceID, out _factory) < 0)
                _factory = IntPtr.Zero;
        }
        catch (Exception exception) when (exception is DllNotFoundException
                                              or EntryPointNotFoundException
                                              or BadImageFormatException)
        {
            _factory = IntPtr.Zero;
        }
    }

    public AcceleratorAdapter GetAdapter(ulong adapterLUID)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_adapters.TryGetValue(adapterLUID, out AcceleratorAdapter adapter)) return adapter;

        AcceleratorKind kind = IsNPU(adapterLUID) ? AcceleratorKind.NPU : AcceleratorKind.GPU;
        int displayIndex;
        switch (kind)
        {
            case AcceleratorKind.GPU:
                displayIndex = _nextGPUDisplayIndex;
                _nextGPUDisplayIndex++;
                break;
            case AcceleratorKind.NPU:
                displayIndex = _nextNPUDisplayIndex;
                _nextNPUDisplayIndex++;
                break;
            default:
                displayIndex = 0;
                break;
        }

        adapter = new AcceleratorAdapter(adapterLUID, kind, displayIndex);
        _adapters.Add(adapterLUID, adapter);
        return adapter;
    }

    public string GetEngineLabel(
        AcceleratorAdapter adapter,
        AcceleratorCounterInstance instance,
        ReadOnlySpan<char> engineType)
    {
        AcceleratorEngineKey key = new(
            adapter.LUID,
            instance.PhysicalAdapterIndex,
            instance.EngineIndex);
        if (_engineLabels.TryGetValue(key, out string? existing)) return existing;

        string acceleratorName = adapter.Kind == AcceleratorKind.NPU ? "NPU" : "GPU";
        string normalizedEngineType = NormalizeEngineType(engineType);
        string label = string.Concat(
            acceleratorName,
            " ",
            adapter.DisplayIndex.ToString(CultureInfo.InvariantCulture),
            " - ",
            normalizedEngineType);
        _engineLabels.Add(key, label);
        return label;
    }

    private bool IsNPU(ulong adapterLUID)
    {
        if (_factory == IntPtr.Zero) return false;

        NATIVE_LUID nativeLUID = new() { LowPart = (uint)adapterLUID, HighPart = unchecked((int)(adapterLUID >> 32)) };
        Guid adapterInterfaceID = AdapterInterfaceID;
        IntPtr adapter = IntPtr.Zero;
        IntPtr* factoryVTable = *(IntPtr**)_factory;
        delegate* unmanaged[Stdcall]<IntPtr, NATIVE_LUID*, Guid*, IntPtr*, int> getAdapterByLUID =
            (delegate* unmanaged[Stdcall]<IntPtr, NATIVE_LUID*, Guid*, IntPtr*, int>)factoryVTable[4];
        int result = getAdapterByLUID(_factory, &nativeLUID, &adapterInterfaceID, &adapter);
        if (result < 0 || adapter == IntPtr.Zero) return false;

        try
        {
            bool isGraphics = SupportsAttribute(adapter, D3D12GraphicsAttribute);
            return SupportsAttribute(adapter, NPUAttribute)
                   || SupportsAttribute(adapter, MediaAcceleratorAttribute)
                   || (SupportsAttribute(adapter, CoreComputeAttribute) && !isGraphics)
                   || (SupportsAttribute(adapter, GenericMachineLearningAttribute) && !isGraphics);
        }
        finally
        {
            Release(adapter);
        }
    }

    private static bool SupportsAttribute(IntPtr adapter, Guid attribute)
    {
        IntPtr* adapterVTable = *(IntPtr**)adapter;
        delegate* unmanaged[Stdcall]<IntPtr, Guid*, byte> isAttributeSupported =
            (delegate* unmanaged[Stdcall]<IntPtr, Guid*, byte>)adapterVTable[4];
        return isAttributeSupported(adapter, &attribute) != 0;
    }

    private static string NormalizeEngineType(ReadOnlySpan<char> engineType)
    {
        if (engineType.Equals(other: "3d", StringComparison.OrdinalIgnoreCase)) return "3D";
        if (engineType.Equals(other: "copy", StringComparison.OrdinalIgnoreCase)) return "Copy";
        if (engineType.Equals(other: "compute", StringComparison.OrdinalIgnoreCase)) return "Compute";
        if (engineType.Equals(other: "videodecode", StringComparison.OrdinalIgnoreCase)) return "Video Decode";
        if (engineType.Equals(other: "videoencode", StringComparison.OrdinalIgnoreCase)) return "Video Encode";
        if (engineType.Equals(other: "videoprocessing", StringComparison.OrdinalIgnoreCase)) return "Video Processing";
        if (engineType.Equals(other: "legacyoverlay", StringComparison.OrdinalIgnoreCase)) return "Legacy Overlay";
        if (engineType.Equals(other: "sceneassembly", StringComparison.OrdinalIgnoreCase)) return "Scene Assembly";
        if (engineType.Equals(other: "opticalflow", StringComparison.OrdinalIgnoreCase)) return "Optical Flow";
        if (engineType.Equals(other: "security", StringComparison.OrdinalIgnoreCase)) return "Security";
        return engineType.ToString();
    }

    private static void Release(IntPtr instance)
    {
        IntPtr* vTable = *(IntPtr**)instance;
        delegate* unmanaged[Stdcall]<IntPtr, uint> release =
            (delegate* unmanaged[Stdcall]<IntPtr, uint>)vTable[2];
        _ = release(instance);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _adapters.Clear();
        _engineLabels.Clear();
        if (_factory == IntPtr.Zero) return;

        Release(_factory);
        _factory = IntPtr.Zero;
    }

    [DllImport("dxcore.dll", ExactSpelling = true)]
    private static extern int DXCoreCreateAdapterFactory(ref Guid interfaceID, out IntPtr factory);

    [StructLayout(LayoutKind.Sequential)]
    private struct NATIVE_LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    private readonly record struct AcceleratorEngineKey(
        ulong AdapterLUID,
        int PhysicalAdapterIndex,
        int EngineIndex);
}
