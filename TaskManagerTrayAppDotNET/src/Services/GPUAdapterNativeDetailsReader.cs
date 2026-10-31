using System.Globalization;
using System.Runtime.InteropServices;
using TaskManagerTrayAppDotNET.Interop;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Queries GPU metadata exposed by DXGI, D3D12, and the graphics kernel.</summary>
internal static unsafe class GPUAdapterNativeDetailsReader
{
    private const int AdapterAddressInformation = 6;
    private const int GetSegmentSizeInformation = 3;
    private const int NodeMetadataInformation = 25;
    private const int AdapterPerformanceDataInformation = 62;
    private const int MaximumEngineNodeCount = 256;
    private const uint MaximumTemperatureDeciCelsius = 2_000;
    private const int DXGIErrorDeviceRemoved = unchecked((int)0x887A0005);
    private const int DXGIErrorDeviceHung = unchecked((int)0x887A0006);
    private const int DXGIErrorDeviceReset = unchecked((int)0x887A0007);
    private const int DXGIErrorDriverInternalError = unchecked((int)0x887A0020);
    private const int EOutOfMemory = unchecked((int)0x8007000E);

    private static readonly Guid D3D12DeviceInterfaceID =
        new("189819f1-1db6-4b57-be54-1821339b85f7");

    private static readonly DirectXFeatureLevel[] D3D12FeatureLevels =
    [
        new(Value: 0xC200, Name: "12.2"),
        new(Value: 0xC100, Name: "12.1"),
        new(Value: 0xC000, Name: "12.0"),
        new(Value: 0xB100, Name: "11.1"),
        new(Value: 0xB000, Name: "11.0")
    ];

    private static readonly DirectXFeatureLevelCache FeatureLevelCache = new();

    /// <summary>Reads static adapter metadata suitable for caching by LUID tuple.</summary>
    public static GPUAdapterHardwareMetadata ReadMetadata(
        GPUAdapterKey key,
        ulong fallbackDedicatedMemoryCapacityBytes,
        out string? error)
    {
        GPUDeviceMetadata deviceMetadata = GPUDevicePropertyReader.Read(
            key,
            out string? deviceError);
        List<string> errors = [];
        if (!string.IsNullOrWhiteSpace(deviceError)) errors.Add(deviceError);

        GPUAdapterEngineIdentity[] engineCatalog = [];
        ulong visibleDedicatedMemoryBytes = 0;
        string kernelPhysicalLocation = string.Empty;
        bool hasKernelMetadata = false;
        if (D3DKMT.TryOpenAdapter(key.LUID, out uint adapterHandle))
        {
            hasKernelMetadata = true;
            try
            {
                engineCatalog = ReadEngineCatalog(
                    adapterHandle,
                    key.PhysicalAdapterIndex);
                if (TryReadAdapterInformation(
                        adapterHandle,
                        GetSegmentSizeInformation,
                        out D3DKMT_SEGMENTSIZEINFO segmentSizeInformation))
                    visibleDedicatedMemoryBytes = segmentSizeInformation.DedicatedVideoMemorySize;

                if (TryReadAdapterInformation(
                        adapterHandle,
                        AdapterAddressInformation,
                        out D3DKMT_ADAPTERADDRESS adapterAddress)
                    && IsValidPCIAddress(adapterAddress))
                {
                    kernelPhysicalLocation = FormatPhysicalLocation(
                        adapterAddress.BusNumber,
                        adapterAddress.DeviceNumber,
                        adapterAddress.FunctionNumber);
                }
            }
            finally
            {
                D3DKMT.CloseAdapter(adapterHandle);
            }
        }
        else
        {
            errors.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"D3DKMT could not open adapter LUID 0x{key.LUID:X16}."));
        }

        if (visibleDedicatedMemoryBytes == 0)
            visibleDedicatedMemoryBytes = fallbackDedicatedMemoryCapacityBytes;
        bool hasHardwareReservedMemoryData = deviceMetadata.HasInstalledMemory
                                             && visibleDedicatedMemoryBytes > 0;
        ulong hardwareReservedMemoryBytes = hasHardwareReservedMemoryData
            ? CalculateHardwareReservedMemory(
                deviceMetadata.InstalledMemoryBytes,
                visibleDedicatedMemoryBytes)
            : 0;

        (string directXVersion, string featureLevel) = ReadDirectXFeatureLevel(key.LUID);
        string physicalLocation = kernelPhysicalLocation.Length > 0
            ? kernelPhysicalLocation
            : deviceMetadata.LocationInformation;
        bool hasMetadata = deviceMetadata.HasValue
                           || hasKernelMetadata
                           || directXVersion.Length > 0
                           || engineCatalog.Length > 0;
        error = errors.Count == 0 ? null : string.Join(separator: " ", errors);
        return new GPUAdapterHardwareMetadata(
            hasMetadata,
            deviceMetadata.DriverVersion,
            deviceMetadata.DriverDate,
            directXVersion,
            featureLevel,
            physicalLocation,
            hasHardwareReservedMemoryData,
            hardwareReservedMemoryBytes,
            engineCatalog);
    }

    /// <summary>Reads the optional WDDM adapter temperature, reported in deci-Celsius.</summary>
    public static bool TryReadTemperature(GPUAdapterKey key, out double temperatureCelsius)
    {
        temperatureCelsius = 0;
        if (key.PhysicalAdapterIndex < 0) return false;
        if (!D3DKMT.TryOpenAdapter(key.LUID, out uint adapterHandle)) return false;

        try
        {
            D3DKMT_ADAPTER_PERFDATA performanceData = new()
            {
                PhysicalAdapterIndex = checked((uint)key.PhysicalAdapterIndex)
            };
            if (!D3DKMT.TryQueryAdapterInfo(
                    adapterHandle,
                    AdapterPerformanceDataInformation,
                    ref performanceData)
                || performanceData.Temperature == 0
                || performanceData.Temperature > MaximumTemperatureDeciCelsius)
                return false;

            temperatureCelsius = performanceData.Temperature / 10.0;
            return true;
        }
        finally
        {
            D3DKMT.CloseAdapter(adapterHandle);
        }
    }

    /// <summary>Calculates installed VRAM that is not exposed as a visible memory segment.</summary>
    internal static ulong CalculateHardwareReservedMemory(
        ulong installedMemoryBytes,
        ulong visibleDedicatedMemoryBytes) =>
        installedMemoryBytes > visibleDedicatedMemoryBytes
            ? installedMemoryBytes - visibleDedicatedMemoryBytes
            : 0;

    /// <summary>Formats the graphics-kernel PCI address in Task Manager's field order.</summary>
    internal static string FormatPhysicalLocation(
        uint busNumber,
        uint deviceNumber,
        uint functionNumber) => string.Create(
        CultureInfo.InvariantCulture,
        $"PCI bus {busNumber}, device {deviceNumber}, function {functionNumber}");

    private static bool IsValidPCIAddress(D3DKMT_ADAPTERADDRESS address) =>
        address is
        {
            BusNumber: not uint.MaxValue,
            DeviceNumber: <= 31,
            FunctionNumber: <= 7
        };

    private static GPUAdapterEngineIdentity[] ReadEngineCatalog(
        uint adapterHandle,
        int physicalAdapterIndex)
    {
        if (physicalAdapterIndex is < 0 or > ushort.MaxValue) return [];

        List<GPUAdapterEngineIdentity> engines = [];
        for (int engineIndex = 0; engineIndex < MaximumEngineNodeCount; engineIndex++)
        {
            D3DKMT_NODEMETADATA metadata = new()
            {
                NodeOrdinalAndAdapterIndex =
                    ((uint)physicalAdapterIndex << 16) | (uint)engineIndex
            };
            if (!D3DKMT.TryQueryAdapterInfo(
                    adapterHandle,
                    NodeMetadataInformation,
                    ref metadata))
                break;

            string engineName = ReadFriendlyName(metadata.NodeData);
            if (engineName.Length == 0)
                engineName = GetEngineTypeName(metadata.NodeData.EngineType);
            engines.Add(new GPUAdapterEngineIdentity(
                engineIndex,
                GPUEngineNames.Normalize(engineName)));
        }

        return [.. engines];
    }

    private static string ReadFriendlyName(DXGK_NODEMETADATA metadata)
    {
        char* name = metadata.FriendlyName;
        int length = 0;
        while (length < 32 && name[length] != '\0')
            length++;
        return length == 0 ? string.Empty : new string(name, startIndex: 0, length).Trim();
    }

    private static string GetEngineTypeName(int engineType) => engineType switch
    {
        1 => "3D",
        2 => "Video Decode",
        3 => "Video Encode",
        4 => "Video Processing",
        5 => "Scene Assembly",
        6 => "Copy",
        7 => "Overlay",
        8 => "Crypto",
        9 => "Video Codec",
        _ => "GPU Engine"
    };

    private static (string DirectXVersion, string FeatureLevel) ReadDirectXFeatureLevel(ulong adapterLUID) =>
        FeatureLevelCache.GetOrAdd(adapterLUID, ProbeDirectXFeatureLevel);

    /// <summary>
    /// Tests the highest supported D3D12 feature level without creating a device (a real device loads the vendor
    /// driver and can wake a discrete GPU). A definitive answer cannot change for an adapter LUID (a replaced adapter
    /// has a new LUID), so it is kept for the process lifetime; a missing adapter or a removed, reset or failing device
    /// is retried later instead.
    /// </summary>
    private static DirectXProbeResult ProbeDirectXFeatureLevel(ulong adapterLUID)
    {
        IntPtr adapter = DXGI.FindHardwareAdapter(adapterLUID);
        if (adapter == IntPtr.Zero) return DirectXProbeResult.Transient;

        try
        {
            bool transientFailure = false;
            for (int featureIndex = 0; featureIndex < D3D12FeatureLevels.Length; featureIndex++)
            {
                DirectXFeatureLevel featureLevel = D3D12FeatureLevels[featureIndex];
                Guid interfaceID = D3D12DeviceInterfaceID;
                int result;
                try
                {
                    result = D3D12CreateDevice(adapter, featureLevel.Value, ref interfaceID, device: null);
                }
                catch (Exception exception) when (exception is DllNotFoundException
                                                      or EntryPointNotFoundException
                                                      or BadImageFormatException)
                {
                    // This Windows has no usable D3D12
                    return DirectXProbeResult.Unsupported;
                }

                if (result >= 0) return new DirectXProbeResult("12", featureLevel.Name, IsDefinitive: true);
                transientFailure |= IsTransientProbeFailure(result);
            }

            return transientFailure ? DirectXProbeResult.Transient : DirectXProbeResult.Unsupported;
        }
        finally
        {
            _ = Marshal.Release(adapter);
        }
    }

    /// <summary>Failures that say nothing about the adapter's feature level: the device is gone or busy.</summary>
    internal static bool IsTransientProbeFailure(int hresult) => hresult is
        DXGIErrorDeviceRemoved or DXGIErrorDeviceHung or DXGIErrorDeviceReset or DXGIErrorDriverInternalError
        or EOutOfMemory;

    private static bool TryReadAdapterInformation<T>(
        uint adapterHandle,
        int informationType,
        out T information)
        where T : unmanaged
    {
        information = default;
        return D3DKMT.TryQueryAdapterInfo(adapterHandle, informationType, ref information);
    }

    // A null ppDevice only tests support: no device is created and S_FALSE means the feature level is supported
    [DllImport("d3d12.dll", ExactSpelling = true)]
    private static extern int D3D12CreateDevice(
        IntPtr adapter,
        int minimumFeatureLevel,
        ref Guid interfaceID,
        IntPtr* device);

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_SEGMENTSIZEINFO
    {
        public ulong DedicatedVideoMemorySize;
        public ulong DedicatedSystemMemorySize;
        public ulong SharedSystemMemorySize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTERADDRESS
    {
        public uint BusNumber;
        public uint DeviceNumber;
        public uint FunctionNumber;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct D3DKMT_NODEMETADATA
    {
        public uint NodeOrdinalAndAdapterIndex;
        public DXGK_NODEMETADATA NodeData;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DXGK_NODEMETADATA
    {
        public int EngineType;
        public fixed char FriendlyName[32];
        public uint Flags;
        public byte GPUMMUSupported;
        public byte IOMMUSupported;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTER_PERFDATA
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaximumMemoryFrequency;
        public ulong MaximumOverclockedMemoryFrequency;
        public ulong MemoryBandwidth;
        public ulong PCIEBandwidth;
        public uint FanRPM;
        public uint Power;
        public uint Temperature;
        public byte PowerStateOverride;
    }

    private readonly record struct DirectXFeatureLevel(int Value, string Name);
}
