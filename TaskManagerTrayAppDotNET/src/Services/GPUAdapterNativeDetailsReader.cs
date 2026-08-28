using System.Globalization;
using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Queries GPU metadata exposed by DXGI, D3D12, and the graphics kernel.</summary>
internal static unsafe class GPUAdapterNativeDetailsReader
{
    private const int AdapterAddressInformation = 6;
    private const int GetSegmentSizeInformation = 3;
    private const int NodeMetadataInformation = 25;
    private const int AdapterPerformanceDataInformation = 62;
    private const int DXGIErrorNotFound = unchecked((int)0x887A0002);
    private const int MaximumEngineNodeCount = 256;
    private const uint DXGIAdapterFlagSoftware = 0x2;
    private const uint MaximumTemperatureDeciCelsius = 2_000;

    private static readonly Guid DXGIFactoryInterfaceID =
        new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid D3D12DeviceInterfaceID =
        new("189819f1-1db6-4b57-be54-1821339b85f7");
    private static readonly DirectXFeatureLevel[] D3D12FeatureLevels =
    [
        new(0xC200, "12.2"),
        new(0xC100, "12.1"),
        new(0xC000, "12.0"),
        new(0xB100, "11.1"),
        new(0xB000, "11.0")
    ];

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
        if (TryOpenAdapter(key.LUID, out uint adapterHandle))
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
                {
                    visibleDedicatedMemoryBytes = segmentSizeInformation.DedicatedVideoMemorySize;
                }

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
                CloseAdapter(adapterHandle);
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
        error = errors.Count == 0 ? null : string.Join(" ", errors);
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
        if (!TryOpenAdapter(key.LUID, out uint adapterHandle)) return false;

        try
        {
            D3DKMT_ADAPTER_PERFDATA performanceData = new()
            {
                PhysicalAdapterIndex = checked((uint)key.PhysicalAdapterIndex)
            };
            if (!TryQueryAdapterInformation(
                    adapterHandle,
                    AdapterPerformanceDataInformation,
                    ref performanceData)
                || performanceData.Temperature == 0
                || performanceData.Temperature > MaximumTemperatureDeciCelsius)
            {
                return false;
            }

            temperatureCelsius = performanceData.Temperature / 10.0;
            return true;
        }
        finally
        {
            CloseAdapter(adapterHandle);
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
        address.BusNumber != uint.MaxValue
        && address.DeviceNumber <= 31
        && address.FunctionNumber <= 7;

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
            if (!TryQueryAdapterInformation(
                    adapterHandle,
                    NodeMetadataInformation,
                    ref metadata))
            {
                break;
            }

            string engineName = ReadFriendlyName(metadata.NodeData);
            if (engineName.Length == 0)
                engineName = GetEngineTypeName(metadata.NodeData.EngineType);
            engines.Add(new GPUAdapterEngineIdentity(
                engineIndex,
                GPUPerformanceDetailsReader.NormalizeEngineName(engineName)));
        }

        return [.. engines];
    }

    private static string ReadFriendlyName(DXGK_NODEMETADATA metadata)
    {
        char* name = metadata.FriendlyName;
        int length = 0;
        while (length < 32 && name[length] != '\0')
            length++;
        return length == 0 ? string.Empty : new string(name, 0, length).Trim();
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

    private static (string DirectXVersion, string FeatureLevel) ReadDirectXFeatureLevel(
        ulong adapterLUID)
    {
        IntPtr adapter = FindDXGIAdapter(adapterLUID);
        if (adapter == IntPtr.Zero) return (string.Empty, string.Empty);

        try
        {
            for (int featureIndex = 0; featureIndex < D3D12FeatureLevels.Length; featureIndex++)
            {
                DirectXFeatureLevel featureLevel = D3D12FeatureLevels[featureIndex];
                Guid interfaceID = D3D12DeviceInterfaceID;
                IntPtr device = IntPtr.Zero;
                int result;
                try
                {
                    result = D3D12CreateDevice(
                        adapter,
                        featureLevel.Value,
                        ref interfaceID,
                        out device);
                }
                catch (Exception exception) when (exception is DllNotFoundException
                                                  or EntryPointNotFoundException
                                                  or BadImageFormatException)
                {
                    return (string.Empty, string.Empty);
                }

                if (result < 0 || device == IntPtr.Zero)
                {
                    if (device != IntPtr.Zero) ReleaseCOMObject(device);
                    continue;
                }

                ReleaseCOMObject(device);
                return ("12", featureLevel.Name);
            }

            return (string.Empty, string.Empty);
        }
        finally
        {
            ReleaseCOMObject(adapter);
        }
    }

    private static IntPtr FindDXGIAdapter(ulong adapterLUID)
    {
        Guid interfaceID = DXGIFactoryInterfaceID;
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
            return IntPtr.Zero;
        }
        if (result < 0 || factory == IntPtr.Zero) return IntPtr.Zero;

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

                IntPtr* adapterVTable = *(IntPtr**)adapter;
                delegate* unmanaged[Stdcall]<IntPtr, DXGI_ADAPTER_DESC1*, int> getDescription =
                    (delegate* unmanaged[Stdcall]<IntPtr, DXGI_ADAPTER_DESC1*, int>)adapterVTable[10];
                DXGI_ADAPTER_DESC1 description = default;
                if (getDescription(adapter, &description) >= 0
                    && (description.Flags & DXGIAdapterFlagSoftware) == 0
                    && ToUInt64(description.AdapterLUID) == adapterLUID)
                {
                    return adapter;
                }

                ReleaseCOMObject(adapter);
            }

            return IntPtr.Zero;
        }
        finally
        {
            ReleaseCOMObject(factory);
        }
    }

    private static bool TryOpenAdapter(ulong adapterLUID, out uint adapterHandle)
    {
        D3DKMT_OPENADAPTERFROMLUID openAdapter = new()
        {
            AdapterLUID = ToNativeLUID(adapterLUID)
        };
        int result;
        try
        {
            result = D3DKMTOpenAdapterFromLuid(ref openAdapter);
        }
        catch (Exception exception) when (exception is DllNotFoundException
                                          or EntryPointNotFoundException
                                          or BadImageFormatException)
        {
            adapterHandle = 0;
            return false;
        }

        adapterHandle = result >= 0 ? openAdapter.AdapterHandle : 0;
        return adapterHandle != 0;
    }

    private static void CloseAdapter(uint adapterHandle)
    {
        if (adapterHandle == 0) return;
        D3DKMT_CLOSEADAPTER closeAdapter = new() { AdapterHandle = adapterHandle };
        _ = D3DKMTCloseAdapter(ref closeAdapter);
    }

    private static bool TryReadAdapterInformation<T>(
        uint adapterHandle,
        int informationType,
        out T information)
        where T : unmanaged
    {
        information = default;
        return TryQueryAdapterInformation(adapterHandle, informationType, ref information);
    }

    private static bool TryQueryAdapterInformation<T>(
        uint adapterHandle,
        int informationType,
        ref T information)
        where T : unmanaged
    {
        fixed (T* informationPointer = &information)
        {
            D3DKMT_QUERYADAPTERINFO query = new()
            {
                AdapterHandle = adapterHandle,
                Type = informationType,
                PrivateDriverData = (IntPtr)informationPointer,
                PrivateDriverDataSize = checked((uint)sizeof(T))
            };
            return D3DKMTQueryAdapterInfo(ref query) >= 0;
        }
    }

    private static NATIVE_LUID ToNativeLUID(ulong value) => new()
    {
        LowPart = (uint)value,
        HighPart = unchecked((int)(value >> 32))
    };

    private static ulong ToUInt64(NATIVE_LUID value) =>
        ((ulong)(uint)value.HighPart << 32) | value.LowPart;

    private static void ReleaseCOMObject(IntPtr instance)
    {
        if (instance == IntPtr.Zero) return;
        IntPtr* vTable = *(IntPtr**)instance;
        delegate* unmanaged[Stdcall]<IntPtr, uint> release =
            (delegate* unmanaged[Stdcall]<IntPtr, uint>)vTable[2];
        _ = release(instance);
    }

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTOpenAdapterFromLuid(
        ref D3DKMT_OPENADAPTERFROMLUID openAdapter);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryAdapterInfo(
        ref D3DKMT_QUERYADAPTERINFO queryAdapterInformation);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(
        ref D3DKMT_CLOSEADAPTER closeAdapter);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(
        ref Guid interfaceID,
        out IntPtr factory);

    [DllImport("d3d12.dll", ExactSpelling = true)]
    private static extern int D3D12CreateDevice(
        IntPtr adapter,
        int minimumFeatureLevel,
        ref Guid interfaceID,
        out IntPtr device);

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
    private struct D3DKMT_CLOSEADAPTER
    {
        public uint AdapterHandle;
    }

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

    private readonly record struct DirectXFeatureLevel(int Value, string Name);
}
