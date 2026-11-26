using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Reads driver and installed-memory properties for an exact GPU adapter LUID.</summary>
internal static class GPUDevicePropertyReader
{
    private const uint DevpropTypeUInt32 = 0x00000007;
    private const uint DevpropTypeUInt64 = 0x00000009;
    private const uint DevpropTypeFileTime = 0x00000010;
    private const uint DevpropTypeString = 0x00000012;
    private const uint DevpropTypeStringList = 0x00002012;
    private const int ConfigManagerSuccess = 0;
    private const int ConfigManagerBufferSmall = 0x1A;
    private const int MaximumBridgeWalkDepth = 8;
    private const uint MaximumRegistryStringBytes = 4_096;
    private const uint RegString = 1;
    private const uint DicsFlagGlobal = 0x00000001;
    private const uint DiregDriver = 0x00000002;
    private const uint KeyRead = 0x00020019;
    private const uint RegBinary = 3;
    private const uint RegDword = 4;
    private const uint RegQword = 11;
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    private static readonly Guid DisplayClassGUID =
        new("4d36e968-e325-11ce-bfc1-08002be10318");

    private static readonly DEVPROPKEY GPULUIDProperty = new(
        new Guid("60b193cb-5276-4d0f-96fc-f173abad3ec6"),
        propertyID: 2);

    private static readonly DEVPROPKEY GPUPhysicalAdapterIndexProperty = new(
        new Guid("60b193cb-5276-4d0f-96fc-f173abad3ec6"),
        propertyID: 3);

    private static readonly DEVPROPKEY DriverDateProperty = new(
        new Guid("a8b865dd-2e3d-4094-ad97-e593a70c75d6"),
        propertyID: 2);

    private static readonly DEVPROPKEY DriverVersionProperty = new(
        new Guid("a8b865dd-2e3d-4094-ad97-e593a70c75d6"),
        propertyID: 3);

    private static readonly DEVPROPKEY LocationInformationProperty = new(
        new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        propertyID: 15);

    private static readonly DEVPROPKEY HardwareIDsProperty = new(
        new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        propertyID: 3);

    private static readonly DEVPROPKEY InstanceIDProperty = new(
        new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"),
        propertyID: 256);

    private static readonly DEVPROPKEY PCICurrentLinkSpeedProperty = new(
        new Guid("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62"),
        propertyID: 9);

    private static readonly DEVPROPKEY PCICurrentLinkWidthProperty = new(
        new Guid("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62"),
        propertyID: 10);

    private static readonly DEVPROPKEY PCIMaximumLinkSpeedProperty = new(
        new Guid("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62"),
        propertyID: 11);

    private static readonly DEVPROPKEY PCIMaximumLinkWidthProperty = new(
        new Guid("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62"),
        propertyID: 12);

    /// <summary>Finds the display-class device whose graphics-kernel LUID matches the sampler.</summary>
    public static GPUDeviceMetadata Read(GPUAdapterKey key, out string? error)
    {
        Guid classGUID = DisplayClassGUID;
        IntPtr deviceInformationSet = SetupAPI.SetupDiGetClassDevs(
            ref classGUID,
            IntPtr.Zero,
            IntPtr.Zero,
            SetupAPI.DIGCF_PRESENT);
        if (deviceInformationSet == SetupAPI.INVALID_HANDLE_VALUE)
        {
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return GPUDeviceMetadata.Empty;
        }

        try
        {
            for (int deviceIndex = 0;; deviceIndex++)
            {
                SetupAPI.SP_DEVINFO_DATA deviceInformation = new()
                {
                    cbSize = Marshal.SizeOf<SetupAPI.SP_DEVINFO_DATA>()
                };
                if (!SetupAPI.SetupDiEnumDeviceInfo(
                        deviceInformationSet,
                        deviceIndex,
                        ref deviceInformation))
                {
                    int enumerationError = Marshal.GetLastWin32Error();
                    if (enumerationError == SetupAPI.ERROR_NO_MORE_ITEMS) break;

                    error = new Win32Exception(enumerationError).Message;
                    return GPUDeviceMetadata.Empty;
                }

                if (!TryReadUInt64Property(
                        deviceInformationSet,
                        ref deviceInformation,
                        GPULUIDProperty,
                        out ulong adapterLUID)
                    || adapterLUID != key.LUID)
                    continue;

                int physicalAdapterIndex = 0;
                if (TryReadUInt32Property(
                        deviceInformationSet,
                        ref deviceInformation,
                        GPUPhysicalAdapterIndexProperty,
                        out uint physicalAdapterIndexValue))
                    physicalAdapterIndex = checked((int)physicalAdapterIndexValue);
                if (physicalAdapterIndex != key.PhysicalAdapterIndex) continue;

                _ = TryReadStringProperty(
                    deviceInformationSet,
                    ref deviceInformation,
                    DriverVersionProperty,
                    out string driverVersion);
                _ = TryReadDateProperty(
                    deviceInformationSet,
                    ref deviceInformation,
                    DriverDateProperty,
                    out DateOnly? driverDate);
                _ = TryReadStringProperty(
                    deviceInformationSet,
                    ref deviceInformation,
                    LocationInformationProperty,
                    out string locationInformation);
                bool hasInstalledMemory = TryReadDriverKey(
                    deviceInformationSet,
                    ref deviceInformation,
                    out ulong installedMemoryBytes,
                    out string biosString,
                    out string amdSoftwareVersion);
                uint deviceInstance = unchecked((uint)deviceInformation.DevInst);
                _ = TryReadDevNodeString(deviceInstance, InstanceIDProperty, out string deviceInstanceID);
                string hardwareID = ReadFirstHardwareID(deviceInstance);
                GPUPCIeLink link = ReadPCIeLink(deviceInstance, hardwareID);

                error = null;
                return new GPUDeviceMetadata(
                    HasValue: true,
                    driverVersion,
                    driverDate,
                    locationInformation,
                    hasInstalledMemory,
                    installedMemoryBytes,
                    deviceInstanceID,
                    hardwareID,
                    biosString,
                    amdSoftwareVersion,
                    link);
            }

            error = null;
            return GPUDeviceMetadata.Empty;
        }
        finally
        {
            _ = SetupAPI.SetupDiDestroyDeviceInfoList(deviceInformationSet);
        }
    }

    private static bool TryReadUInt32Property(
        IntPtr deviceInformationSet,
        ref SetupAPI.SP_DEVINFO_DATA deviceInformation,
        DEVPROPKEY propertyKey,
        out uint value)
    {
        value = 0;
        if (!TryReadProperty(
                deviceInformationSet,
                ref deviceInformation,
                propertyKey,
                out uint propertyType,
                out byte[] propertyData)
            || propertyType != DevpropTypeUInt32
            || propertyData.Length < sizeof(uint))
            return false;

        value = BinaryPrimitives.ReadUInt32LittleEndian(propertyData);
        return true;
    }

    private static bool TryReadUInt64Property(
        IntPtr deviceInformationSet,
        ref SetupAPI.SP_DEVINFO_DATA deviceInformation,
        DEVPROPKEY propertyKey,
        out ulong value)
    {
        value = 0;
        if (!TryReadProperty(
                deviceInformationSet,
                ref deviceInformation,
                propertyKey,
                out uint propertyType,
                out byte[] propertyData)
            || propertyType != DevpropTypeUInt64
            || propertyData.Length < sizeof(ulong))
            return false;

        value = BinaryPrimitives.ReadUInt64LittleEndian(propertyData);
        return true;
    }

    private static bool TryReadDateProperty(
        IntPtr deviceInformationSet,
        ref SetupAPI.SP_DEVINFO_DATA deviceInformation,
        DEVPROPKEY propertyKey,
        out DateOnly? value)
    {
        value = null;
        if (!TryReadProperty(
                deviceInformationSet,
                ref deviceInformation,
                propertyKey,
                out uint propertyType,
                out byte[] propertyData)
            || propertyType != DevpropTypeFileTime
            || propertyData.Length < sizeof(long))
            return false;

        long fileTime = BinaryPrimitives.ReadInt64LittleEndian(propertyData);
        try
        {
            DateTime date = DateTime.FromFileTimeUtc(fileTime);
            value = DateOnly.FromDateTime(date);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryReadStringProperty(
        IntPtr deviceInformationSet,
        ref SetupAPI.SP_DEVINFO_DATA deviceInformation,
        DEVPROPKEY propertyKey,
        out string value)
    {
        value = string.Empty;
        if (!TryReadProperty(
                deviceInformationSet,
                ref deviceInformation,
                propertyKey,
                out uint propertyType,
                out byte[] propertyData)
            || propertyType != DevpropTypeString
            || propertyData.Length < sizeof(char))
            return false;

        value = Encoding.Unicode.GetString(propertyData).TrimEnd('\0').Trim();
        return value.Length > 0;
    }

    private static bool TryReadProperty(
        IntPtr deviceInformationSet,
        ref SetupAPI.SP_DEVINFO_DATA deviceInformation,
        DEVPROPKEY propertyKey,
        out uint propertyType,
        out byte[] propertyData)
    {
        propertyType = 0;
        propertyData = [];
        DEVPROPKEY mutablePropertyKey = propertyKey;
        bool initialResult = SetupDiGetDevicePropertyW(
            deviceInformationSet,
            ref deviceInformation,
            ref mutablePropertyKey,
            out propertyType,
            propertyBuffer: null,
            propertyBufferSize: 0,
            out uint requiredSize,
            flags: 0);
        if (!initialResult && Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            return false;
        if (requiredSize is 0 or > int.MaxValue) return false;

        propertyData = new byte[requiredSize];
        mutablePropertyKey = propertyKey;
        if (SetupDiGetDevicePropertyW(
                deviceInformationSet,
                ref deviceInformation,
                ref mutablePropertyKey,
                out propertyType,
                propertyData,
                checked((uint)propertyData.Length),
                out uint returnedSize,
                flags: 0)
            && returnedSize <= propertyData.Length)
        {
            if (returnedSize != propertyData.Length)
                Array.Resize(ref propertyData, checked((int)returnedSize));
            return true;
        }

        propertyData = [];
        return false;
    }

    /// <summary>
    /// Reads the values the display driver writes to its own registry key: installed memory, the vBIOS string, and
    /// (AMD only) the Adrenalin release the package installed. Returns whether installed memory was found.
    /// </summary>
    private static bool TryReadDriverKey(
        IntPtr deviceInformationSet,
        ref SetupAPI.SP_DEVINFO_DATA deviceInformation,
        out ulong installedMemoryBytes,
        out string biosString,
        out string amdSoftwareVersion)
    {
        installedMemoryBytes = 0;
        biosString = string.Empty;
        amdSoftwareVersion = string.Empty;
        IntPtr driverKey = SetupDiOpenDevRegKey(
            deviceInformationSet,
            ref deviceInformation,
            DicsFlagGlobal,
            hardwareProfile: 0,
            DiregDriver,
            KeyRead);
        if (driverKey == SetupAPI.INVALID_HANDLE_VALUE) return false;

        try
        {
            _ = TryReadRegistryString(driverKey, valueName: "HardwareInformation.BiosString", out biosString);

            // Adrenalin writes its release as RadeonSoftwareVersion; older Crimson and Catalyst packages wrote only
            // Catalyst_Version. Neither name has been confirmed on an AMD machine here.
            if (!TryReadRegistryString(driverKey, valueName: "RadeonSoftwareVersion", out amdSoftwareVersion))
                _ = TryReadRegistryString(driverKey, valueName: "Catalyst_Version", out amdSoftwareVersion);

            if (TryReadRegistryInteger(
                    driverKey,
                    valueName: "HardwareInformation.qwMemorySize",
                    out installedMemoryBytes))
                return installedMemoryBytes > 0;

            return TryReadRegistryInteger(
                       driverKey,
                       valueName: "HardwareInformation.MemorySize",
                       out installedMemoryBytes)
                   && installedMemoryBytes > 0;
        }
        finally
        {
            _ = RegCloseKey(driverKey);
        }
    }

    /// <summary>Reads a registry string a driver may store as REG_SZ or as raw UTF-16 REG_BINARY.</summary>
    private static bool TryReadRegistryString(IntPtr key, string valueName, out string value)
    {
        value = string.Empty;
        uint valueType = 0;
        uint dataSize = 0;
        int result = RegQueryValueExW(key, valueName, IntPtr.Zero, ref valueType, data: null, ref dataSize);
        if (result != ErrorSuccess || dataSize is 0 or > MaximumRegistryStringBytes) return false;

        byte[] data = new byte[dataSize];
        result = RegQueryValueExW(key, valueName, IntPtr.Zero, ref valueType, data, ref dataSize);
        if (result != ErrorSuccess || valueType is not (RegString or RegBinary)) return false;

        value = Encoding.Unicode.GetString(data, index: 0, (int)dataSize & ~1).TrimEnd('\0').Trim();
        return value.Length > 0;
    }

    /// <summary>Returns the most specific hardware ID, the one naming vendor, device, subsystem and revision.</summary>
    private static string ReadFirstHardwareID(uint deviceInstance)
    {
        if (!TryReadDevNodeProperty(deviceInstance, HardwareIDsProperty, out uint type, out byte[] data)
            || type != DevpropTypeStringList)
            return string.Empty;

        string list = Encoding.Unicode.GetString(data);
        int terminator = list.IndexOf('\0');
        return (terminator >= 0 ? list[..terminator] : list).Trim();
    }

    /// <summary>
    /// Reads the PCIe link of the slot a GPU sits in. AMD's discrete cards put a PCIe switch of their own between the
    /// GPU and the slot, so the GPU's own link is the card-internal one. Walking up through parents from the GPU's
    /// own vendor reaches the switch's upstream port, whose link is the slot's; GPUs without such a switch stop at
    /// themselves because their parent is the platform's root port.
    /// </summary>
    private static GPUPCIeLink ReadPCIeLink(uint deviceInstance, string hardwareID)
    {
        uint linkDevice = deviceInstance;
        if (GPUVendorFormatting.TryParseHardwareID(hardwareID, out GPUHardwareID parsed))
        {
            uint current = deviceInstance;
            for (int depth = 0; depth < MaximumBridgeWalkDepth; depth++)
            {
                if (CM_Get_Parent(out uint parent, current, flags: 0) != ConfigManagerSuccess) break;
                if (!GPUVendorFormatting.TryParseHardwareID(ReadFirstHardwareID(parent), out GPUHardwareID parentID)
                    || parentID.VendorID != parsed.VendorID)
                    break;

                linkDevice = parent;
                current = parent;
            }
        }

        bool hasCurrentSpeed = TryReadDevNodeUInt32(linkDevice, PCICurrentLinkSpeedProperty, out uint currentSpeed);
        bool hasCurrentWidth = TryReadDevNodeUInt32(linkDevice, PCICurrentLinkWidthProperty, out uint currentWidth);
        _ = TryReadDevNodeUInt32(linkDevice, PCIMaximumLinkSpeedProperty, out uint maximumSpeed);
        _ = TryReadDevNodeUInt32(linkDevice, PCIMaximumLinkWidthProperty, out uint maximumWidth);
        return hasCurrentSpeed || hasCurrentWidth
            ? new GPUPCIeLink(HasValue: true, currentSpeed, currentWidth, maximumSpeed, maximumWidth)
            : default;
    }

    private static bool TryReadDevNodeUInt32(uint deviceInstance, DEVPROPKEY propertyKey, out uint value)
    {
        value = 0;
        if (!TryReadDevNodeProperty(deviceInstance, propertyKey, out uint type, out byte[] data)
            || type != DevpropTypeUInt32
            || data.Length < sizeof(uint))
            return false;

        value = BinaryPrimitives.ReadUInt32LittleEndian(data);
        return true;
    }

    private static bool TryReadDevNodeString(uint deviceInstance, DEVPROPKEY propertyKey, out string value)
    {
        value = string.Empty;
        if (!TryReadDevNodeProperty(deviceInstance, propertyKey, out uint type, out byte[] data)
            || type != DevpropTypeString)
            return false;

        value = Encoding.Unicode.GetString(data).TrimEnd('\0').Trim();
        return value.Length > 0;
    }

    private static bool TryReadDevNodeProperty(
        uint deviceInstance,
        DEVPROPKEY propertyKey,
        out uint propertyType,
        out byte[] propertyData)
    {
        propertyData = [];
        DEVPROPKEY mutablePropertyKey = propertyKey;
        uint size = 0;
        int result = CM_Get_DevNode_PropertyW(
            deviceInstance,
            ref mutablePropertyKey,
            out propertyType,
            propertyBuffer: null,
            ref size,
            flags: 0);
        if (result != ConfigManagerBufferSmall || size is 0 or > int.MaxValue) return false;

        byte[] buffer = new byte[size];
        mutablePropertyKey = propertyKey;
        result = CM_Get_DevNode_PropertyW(
            deviceInstance,
            ref mutablePropertyKey,
            out propertyType,
            buffer,
            ref size,
            flags: 0);
        if (result != ConfigManagerSuccess || size > buffer.Length) return false;

        propertyData = size == buffer.Length ? buffer : buffer[..(int)size];
        return true;
    }

    private static bool TryReadRegistryInteger(
        IntPtr key,
        string valueName,
        out ulong value)
    {
        value = 0;
        uint valueType = 0;
        uint dataSize = 0;
        int result = RegQueryValueExW(
            key,
            valueName,
            IntPtr.Zero,
            ref valueType,
            data: null,
            ref dataSize);
        if (result != ErrorSuccess || dataSize == 0 || dataSize > sizeof(ulong))
            return false;

        byte[] data = new byte[dataSize];
        result = RegQueryValueExW(
            key,
            valueName,
            IntPtr.Zero,
            ref valueType,
            data,
            ref dataSize);
        if (result != ErrorSuccess) return false;

        switch (valueType)
        {
            case RegQword when dataSize >= sizeof(ulong):
                value = BinaryPrimitives.ReadUInt64LittleEndian(data);
                return true;
            case RegDword when dataSize >= sizeof(uint):
            case RegBinary when dataSize == sizeof(uint):
                value = BinaryPrimitives.ReadUInt32LittleEndian(data);
                return true;
            case RegBinary when dataSize == sizeof(ulong):
                value = BinaryPrimitives.ReadUInt64LittleEndian(data);
                return true;
            default:
                return false;
        }
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDevicePropertyW(
        IntPtr deviceInformationSet,
        ref SetupAPI.SP_DEVINFO_DATA deviceInformation,
        ref DEVPROPKEY propertyKey,
        out uint propertyType,
        [Out] byte[]? propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiOpenDevRegKey(
        IntPtr deviceInformationSet,
        ref SetupAPI.SP_DEVINFO_DATA deviceInformation,
        uint scope,
        uint hardwareProfile,
        uint keyType,
        uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegQueryValueExW(
        IntPtr key,
        string valueName,
        IntPtr reserved,
        ref uint valueType,
        [Out] byte[]? data,
        ref uint dataSize);

    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(IntPtr key);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parentDeviceInstance, uint deviceInstance, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_PropertyW(
        uint deviceInstance,
        ref DEVPROPKEY propertyKey,
        out uint propertyType,
        [Out] byte[]? propertyBuffer,
        ref uint propertyBufferSize,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DEVPROPKEY(Guid formatID, uint propertyID)
    {
        public readonly Guid FormatID = formatID;
        public readonly uint PropertyID = propertyID;
    }
}

/// <summary>Device-manager properties associated with one exact GPU LUID tuple.</summary>
internal readonly record struct GPUDeviceMetadata(
    bool HasValue,
    string DriverVersion,
    DateOnly? DriverDate,
    string LocationInformation,
    bool HasInstalledMemory,
    ulong InstalledMemoryBytes,
    string DeviceInstanceID,
    string HardwareID,
    string BIOSString,
    string AMDSoftwareVersion,
    GPUPCIeLink Link)
{
    public static GPUDeviceMetadata Empty { get; } = new(
        HasValue: false,
        string.Empty,
        DriverDate: null,
        string.Empty,
        HasInstalledMemory: false,
        InstalledMemoryBytes: 0,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        default);
}
