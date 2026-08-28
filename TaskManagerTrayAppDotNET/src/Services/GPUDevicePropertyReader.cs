using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using TrayAppDotNETCommon.Interop;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Reads driver and installed-memory properties for an exact GPU adapter LUID.</summary>
internal static class GPUDevicePropertyReader
{
    private const uint DevpropTypeUInt32 = 0x00000007;
    private const uint DevpropTypeUInt64 = 0x00000009;
    private const uint DevpropTypeFileTime = 0x00000010;
    private const uint DevpropTypeString = 0x00000012;
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
        2);
    private static readonly DEVPROPKEY GPUPhysicalAdapterIndexProperty = new(
        new Guid("60b193cb-5276-4d0f-96fc-f173abad3ec6"),
        3);
    private static readonly DEVPROPKEY DriverDateProperty = new(
        new Guid("a8b865dd-2e3d-4094-ad97-e593a70c75d6"),
        2);
    private static readonly DEVPROPKEY DriverVersionProperty = new(
        new Guid("a8b865dd-2e3d-4094-ad97-e593a70c75d6"),
        3);
    private static readonly DEVPROPKEY LocationInformationProperty = new(
        new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        15);

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
            for (int deviceIndex = 0; ; deviceIndex++)
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
                {
                    continue;
                }

                int physicalAdapterIndex = 0;
                if (TryReadUInt32Property(
                        deviceInformationSet,
                        ref deviceInformation,
                        GPUPhysicalAdapterIndexProperty,
                        out uint physicalAdapterIndexValue))
                {
                    physicalAdapterIndex = checked((int)physicalAdapterIndexValue);
                }
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
                bool hasInstalledMemory = TryReadInstalledMemory(
                    deviceInformationSet,
                    ref deviceInformation,
                    out ulong installedMemoryBytes);

                error = null;
                return new GPUDeviceMetadata(
                    true,
                    driverVersion,
                    driverDate,
                    locationInformation,
                    hasInstalledMemory,
                    installedMemoryBytes);
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
        {
            return false;
        }

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
        {
            return false;
        }

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
        {
            return false;
        }

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
        {
            return false;
        }

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
            null,
            0,
            out uint requiredSize,
            0);
        if (!initialResult && Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            return false;
        if (requiredSize == 0 || requiredSize > int.MaxValue) return false;

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
                0)
            && returnedSize <= propertyData.Length)
        {
            if (returnedSize != propertyData.Length)
                Array.Resize(ref propertyData, checked((int)returnedSize));
            return true;
        }

        propertyData = [];
        return false;
    }

    private static bool TryReadInstalledMemory(
        IntPtr deviceInformationSet,
        ref SetupAPI.SP_DEVINFO_DATA deviceInformation,
        out ulong installedMemoryBytes)
    {
        installedMemoryBytes = 0;
        IntPtr driverKey = SetupDiOpenDevRegKey(
            deviceInformationSet,
            ref deviceInformation,
            DicsFlagGlobal,
            0,
            DiregDriver,
            KeyRead);
        if (driverKey == SetupAPI.INVALID_HANDLE_VALUE) return false;

        try
        {
            if (TryReadRegistryInteger(
                    driverKey,
                    "HardwareInformation.qwMemorySize",
                    out installedMemoryBytes))
            {
                return installedMemoryBytes > 0;
            }

            return TryReadRegistryInteger(
                       driverKey,
                       "HardwareInformation.MemorySize",
                       out installedMemoryBytes)
                   && installedMemoryBytes > 0;
        }
        finally
        {
            _ = RegCloseKey(driverKey);
        }
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
            null,
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
    ulong InstalledMemoryBytes)
{
    public static GPUDeviceMetadata Empty { get; } = new(
        false,
        string.Empty,
        null,
        string.Empty,
        false,
        0);
}
