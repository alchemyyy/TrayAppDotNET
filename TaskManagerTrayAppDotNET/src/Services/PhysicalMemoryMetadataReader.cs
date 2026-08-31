using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Reads physical DIMM metadata through the native WMI COM interfaces.</summary>
internal sealed class PhysicalMemoryMetadataReader
{
    private const string WmiNamespace = @"ROOT\CIMV2";

    private const string PublicModuleQuery =
        "SELECT BankLabel, Capacity, ConfiguredClockSpeed, Speed, FormFactor, PartNumber "
        + "FROM Win32_PhysicalMemory";

    private const string PrivateModuleQuery =
        "SELECT BankLabel, Capacity, ConfiguredClockSpeed, Speed, FormFactor, PartNumber, SerialNumber "
        + "FROM Win32_PhysicalMemory";

    private const string SlotQuery =
        "SELECT MemoryDevices FROM Win32_PhysicalMemoryArray WHERE Use = 3";

    private const int WbemInfinite = -1;
    private const int WbemFlagReturnImmediately = 0x10;
    private const int WbemFlagForwardOnly = 0x20;
    private const int WbemQueryFlags = WbemFlagReturnImmediately | WbemFlagForwardOnly;

    private PhysicalMemoryHardwareMetadata _publicMetadata = PhysicalMemoryHardwareMetadata.Empty;
    private PhysicalMemoryHardwareMetadata _privateMetadata = PhysicalMemoryHardwareMetadata.Empty;
    private string? _publicError;
    private string? _privateError;
    private bool _publicAttempted;
    private bool _privateAttempted;
    private bool _publicSucceeded;
    private bool _privateSucceeded;

    /// <summary>Returns cached module metadata, querying WMI at most once per privacy mode.</summary>
    public PhysicalMemoryHardwareMetadata Get(bool includeSerialNumbers, out string? error)
    {
        if (!includeSerialNumbers)
        {
            EnsurePublicMetadata();
            error = _publicError;
            return _publicMetadata;
        }

        EnsurePrivateMetadata();
        if (_privateSucceeded)
        {
            error = null;
            return _privateMetadata;
        }

        EnsurePublicMetadata();
        error = _privateError ?? _publicError;
        return _publicMetadata;
    }

    private void EnsurePublicMetadata()
    {
        if (_publicAttempted) return;
        _publicAttempted = true;
        _publicSucceeded = TryRead(
            includeSerialNumbers: false,
            out _publicMetadata,
            out _publicError);
    }

    private void EnsurePrivateMetadata()
    {
        if (_privateAttempted) return;
        _privateAttempted = true;
        _privateSucceeded = TryRead(
            includeSerialNumbers: true,
            out _privateMetadata,
            out _privateError);
        if (!_privateSucceeded) return;

        _publicMetadata = RemoveSerialNumbers(_privateMetadata);
        _publicAttempted = true;
        _publicSucceeded = true;
        _publicError = null;
    }

    private static bool TryRead(
        bool includeSerialNumbers,
        out PhysicalMemoryHardwareMetadata metadata,
        out string? error)
    {
        metadata = PhysicalMemoryHardwareMetadata.Empty;
        error = null;

        try
        {
            using WmiComApartmentScope _ = WmiComApartmentScope.Enter();
            IPhysicalMemoryWbemServices? services = null;
            try
            {
                if (!TryConnect(out services, out error)) return false;

                List<PhysicalMemoryModuleData> modules = [];
                Query(
                    services,
                    includeSerialNumbers ? PrivateModuleQuery : PublicModuleQuery,
                    memoryObject => modules.Add(ReadModule(memoryObject, includeSerialNumbers)));
                modules.RemoveAll(static module => module.CapacityBytes == 0
                                                   && string.IsNullOrWhiteSpace(module.BankLabel)
                                                   && string.IsNullOrWhiteSpace(module.PartNumber));
                modules.Sort(CompareModules);
                int totalSlotCount = ReadTotalSlotCount(services);
                metadata = BuildMetadata(modules, totalSlotCount);
                return true;
            }
            finally
            {
                Safe.Release(services);
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static PhysicalMemoryModuleData ReadModule(
        IPhysicalMemoryWbemClassObject memoryObject,
        bool includeSerialNumbers)
    {
        _ = TryGetString(memoryObject, propertyName: "BankLabel", out string bankLabel);
        _ = TryGetUInt64(memoryObject, propertyName: "Capacity", out ulong capacityBytes);
        _ = TryGetUInt64(
            memoryObject,
            propertyName: "ConfiguredClockSpeed",
            out ulong configuredSpeedMegatransfersPerSecond);
        _ = TryGetUInt64(memoryObject, propertyName: "Speed", out ulong fallbackSpeedMegatransfersPerSecond);
        _ = TryGetUInt64(memoryObject, propertyName: "FormFactor", out ulong formFactor);
        _ = TryGetString(memoryObject, propertyName: "PartNumber", out string partNumber);
        string serialNumber = string.Empty;
        if (includeSerialNumbers)
            _ = TryGetString(memoryObject, propertyName: "SerialNumber", out serialNumber);

        ulong speedMegatransfersPerSecond = configuredSpeedMegatransfersPerSecond > 0
            ? configuredSpeedMegatransfersPerSecond
            : fallbackSpeedMegatransfersPerSecond;
        return new PhysicalMemoryModuleData(
            NormalizeText(bankLabel),
            capacityBytes,
            speedMegatransfersPerSecond,
            formFactor <= ushort.MaxValue ? (ushort)formFactor : (ushort)0,
            NormalizeText(partNumber),
            NormalizeText(serialNumber));
    }

    private static int ReadTotalSlotCount(IPhysicalMemoryWbemServices services)
    {
        ulong totalSlots = 0;
        Query(
            services,
            SlotQuery,
            memoryArray =>
            {
                if (!TryGetUInt64(memoryArray, propertyName: "MemoryDevices", out ulong memoryDevices)) return;
                totalSlots = SaturatingAdd(totalSlots, memoryDevices);
            });
        return totalSlots > int.MaxValue ? int.MaxValue : (int)totalSlots;
    }

    private static PhysicalMemoryHardwareMetadata BuildMetadata(
        IReadOnlyList<PhysicalMemoryModuleData> modules,
        int totalSlotCount)
    {
        PhysicalMemoryModuleSnapshot[] snapshots = new PhysicalMemoryModuleSnapshot[modules.Count];
        ulong commonSpeed = 0;
        string? commonFormFactor = null;
        bool hasMixedFormFactors = false;
        for (int moduleIndex = 0; moduleIndex < modules.Count; moduleIndex++)
        {
            PhysicalMemoryModuleData module = modules[moduleIndex];
            snapshots[moduleIndex] = new PhysicalMemoryModuleSnapshot(
                module.BankLabel,
                module.CapacityBytes,
                module.PartNumber,
                module.SerialNumber);
            if (module.SpeedMegatransfersPerSecond > 0)
            {
                commonSpeed = commonSpeed == 0
                    ? module.SpeedMegatransfersPerSecond
                    : Math.Min(commonSpeed, module.SpeedMegatransfersPerSecond);
            }

            string formFactor = FormatFormFactor(module.FormFactor);
            if (string.Equals(formFactor, b: "Unknown", StringComparison.Ordinal)) continue;
            if (commonFormFactor == null)
            {
                commonFormFactor = formFactor;
                continue;
            }

            if (!string.Equals(commonFormFactor, formFactor, StringComparison.Ordinal))
                hasMixedFormFactors = true;
        }

        int usedSlotCount = modules.Count;
        return new PhysicalMemoryHardwareMetadata(
            commonSpeed,
            usedSlotCount,
            Math.Max(usedSlotCount, totalSlotCount),
            hasMixedFormFactors ? "Mixed" : commonFormFactor ?? "Unknown",
            snapshots);
    }

    private static PhysicalMemoryHardwareMetadata RemoveSerialNumbers(
        PhysicalMemoryHardwareMetadata metadata)
    {
        ReadOnlySpan<PhysicalMemoryModuleSnapshot> modules = metadata.Modules.Span;
        PhysicalMemoryModuleSnapshot[] publicModules = new PhysicalMemoryModuleSnapshot[modules.Length];
        for (int moduleIndex = 0; moduleIndex < modules.Length; moduleIndex++)
        {
            PhysicalMemoryModuleSnapshot module = modules[moduleIndex];
            publicModules[moduleIndex] = module with { SerialNumber = string.Empty };
        }

        return metadata with { Modules = publicModules };
    }

    private static int CompareModules(PhysicalMemoryModuleData left, PhysicalMemoryModuleData right)
    {
        int bankComparison = StringComparer.OrdinalIgnoreCase.Compare(left.BankLabel, right.BankLabel);
        if (bankComparison != 0) return bankComparison;

        int capacityComparison = left.CapacityBytes.CompareTo(right.CapacityBytes);
        if (capacityComparison != 0) return capacityComparison;
        return StringComparer.OrdinalIgnoreCase.Compare(left.PartNumber, right.PartNumber);
    }

    private static string FormatFormFactor(ushort formFactor) => formFactor switch
    {
        2 => "SIP",
        3 => "DIP",
        4 => "ZIP",
        5 => "SOJ",
        7 => "SIMM",
        8 => "DIMM",
        9 => "TSOP",
        10 => "PGA",
        11 => "RIMM",
        12 => "SODIMM",
        13 => "SRIMM",
        14 => "SMD",
        15 => "SSMP",
        16 => "QFP",
        17 => "TQFP",
        18 => "SOIC",
        19 => "LCC",
        20 => "PLCC",
        21 => "BGA",
        22 => "FPBGA",
        23 => "LGA",
        _ => "Unknown"
    };

    private static string NormalizeText(string value) => value.Trim().TrimEnd('\0').Trim();

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        left > ulong.MaxValue - right ? ulong.MaxValue : left + right;

    private static bool TryConnect(
        [NotNullWhen(true)] out IPhysicalMemoryWbemServices? services,
        out string? error)
    {
        services = null;
        error = null;

        IPhysicalMemoryWbemLocator? locator = null;
        try
        {
            locator = COMActivation.CreateInstance<IPhysicalMemoryWbemLocator>(
                PhysicalMemoryWmiNative.ClsidWbemLocator,
                typeof(IPhysicalMemoryWbemLocator).GUID);
            int result = locator.ConnectServer(
                WmiNamespace,
                user: null,
                password: null,
                locale: null,
                securityFlags: 0,
                authority: null,
                IntPtr.Zero,
                out IntPtr servicesPointer);
            if (result < 0 || servicesPointer == IntPtr.Zero)
            {
                error = $"IWbemLocator.ConnectServer('{WmiNamespace}') failed ({FormatResult(result)}).";
                return false;
            }

            result = PhysicalMemoryWmiNative.CoSetProxyBlanket(
                servicesPointer,
                PhysicalMemoryWmiNative.RpcCAuthenticationWinNT,
                PhysicalMemoryWmiNative.RpcCAuthorizationNone,
                IntPtr.Zero,
                PhysicalMemoryWmiNative.RpcCAuthenticationLevelCall,
                PhysicalMemoryWmiNative.RpcCImpersonationLevelImpersonate,
                IntPtr.Zero,
                PhysicalMemoryWmiNative.EoacNone);
            if (result < 0)
            {
                _ = Marshal.Release(servicesPointer);
                error = $"CoSetProxyBlanket(IWbemServices) failed ({FormatResult(result)}).";
                return false;
            }

            services = COMActivation.GetObjectForComInstance<IPhysicalMemoryWbemServices>(
                servicesPointer,
                releaseInputReference: true);
            return true;
        }
        finally
        {
            Safe.Release(locator);
        }
    }

    private static void Query(
        IPhysicalMemoryWbemServices services,
        string query,
        Action<IPhysicalMemoryWbemClassObject> handleObject)
    {
        int result = services.ExecQuery(
            queryLanguage: "WQL",
            query,
            WbemQueryFlags,
            IntPtr.Zero,
            out IntPtr enumeratorPointer);
        if (result < 0 || enumeratorPointer == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"IWbemServices.ExecQuery failed ({FormatResult(result)}): {query}");
        }

        IPhysicalMemoryEnumWbemClassObject? enumerator = null;
        try
        {
            enumerator = COMActivation.GetObjectForComInstance<IPhysicalMemoryEnumWbemClassObject>(
                enumeratorPointer,
                releaseInputReference: true);
            while (true)
            {
                result = enumerator.Next(
                    WbemInfinite,
                    count: 1,
                    out IntPtr objectPointer,
                    out uint returnedCount);
                if (result < 0)
                {
                    throw new InvalidOperationException(
                        $"IEnumWbemClassObject.Next failed ({FormatResult(result)}).");
                }

                if (returnedCount == 0 || objectPointer == IntPtr.Zero) break;

                IPhysicalMemoryWbemClassObject? memoryObject = null;
                try
                {
                    memoryObject = COMActivation.GetObjectForComInstance<IPhysicalMemoryWbemClassObject>(
                        objectPointer,
                        releaseInputReference: true);
                    handleObject(memoryObject);
                }
                finally
                {
                    Safe.Release(memoryObject);
                }
            }
        }
        finally
        {
            Safe.Release(enumerator);
        }
    }

    private static unsafe bool TryGetString(
        IPhysicalMemoryWbemClassObject memoryObject,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        PhysicalMemoryWmiVariant variant = default;
        int result = memoryObject.Get(
            propertyName,
            flags: 0,
            (IntPtr)(&variant),
            IntPtr.Zero,
            IntPtr.Zero);
        try
        {
            if (result < 0
                || (variant.VariantType & PhysicalMemoryWmiVariant.VariantTypeMask)
                != PhysicalMemoryWmiVariant.VariantBStr
                || variant.PointerValue == IntPtr.Zero)
                return false;

            value = Marshal.PtrToStringBSTR(variant.PointerValue);
            return true;
        }
        finally
        {
            _ = PhysicalMemoryWmiNative.VariantClear((IntPtr)(&variant));
        }
    }

    private static unsafe bool TryGetUInt64(
        IPhysicalMemoryWbemClassObject memoryObject,
        string propertyName,
        out ulong value)
    {
        value = 0;
        PhysicalMemoryWmiVariant variant = default;
        int result = memoryObject.Get(
            propertyName,
            flags: 0,
            (IntPtr)(&variant),
            IntPtr.Zero,
            IntPtr.Zero);
        try
        {
            if (result < 0) return false;

            ushort variantType = (ushort)(
                variant.VariantType & PhysicalMemoryWmiVariant.VariantTypeMask);
            switch (variantType)
            {
                case PhysicalMemoryWmiVariant.VariantUI1:
                    value = variant.ByteValue;
                    return true;
                case PhysicalMemoryWmiVariant.VariantUI2:
                    value = variant.UInt16Value;
                    return true;
                case PhysicalMemoryWmiVariant.VariantUI4:
                case PhysicalMemoryWmiVariant.VariantUInt:
                    value = variant.UInt32Value;
                    return true;
                case PhysicalMemoryWmiVariant.VariantUI8:
                    value = variant.UInt64Value;
                    return true;
                case PhysicalMemoryWmiVariant.VariantI1 when variant.SByteValue >= 0:
                    value = (ulong)variant.SByteValue;
                    return true;
                case PhysicalMemoryWmiVariant.VariantI2 when variant.Int16Value >= 0:
                    value = (ulong)variant.Int16Value;
                    return true;
                case PhysicalMemoryWmiVariant.VariantI4:
                case PhysicalMemoryWmiVariant.VariantInt when variant.Int32Value >= 0:
                    value = (ulong)variant.Int32Value;
                    return true;
                case PhysicalMemoryWmiVariant.VariantI8 when variant.Int64Value >= 0:
                    value = (ulong)variant.Int64Value;
                    return true;
                case PhysicalMemoryWmiVariant.VariantBStr when variant.PointerValue != IntPtr.Zero:
                    string? text = Marshal.PtrToStringBSTR(variant.PointerValue);
                    return ulong.TryParse(
                        text,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out value);
                default:
                    return false;
            }
        }
        finally
        {
            _ = PhysicalMemoryWmiNative.VariantClear((IntPtr)(&variant));
        }
    }

    private static string FormatResult(int result) =>
        string.Create(CultureInfo.InvariantCulture, $"0x{unchecked((uint)result):X8}");

    private sealed record PhysicalMemoryModuleData(
        string BankLabel,
        ulong CapacityBytes,
        ulong SpeedMegatransfersPerSecond,
        ushort FormFactor,
        string PartNumber,
        string SerialNumber);
}

internal readonly record struct PhysicalMemoryHardwareMetadata(
    ulong SpeedMegatransfersPerSecond,
    int UsedSlotCount,
    int TotalSlotCount,
    string FormFactor,
    ReadOnlyMemory<PhysicalMemoryModuleSnapshot> Modules)
{
    public static PhysicalMemoryHardwareMetadata Empty { get; } = new(
        SpeedMegatransfersPerSecond: 0,
        UsedSlotCount: 0,
        TotalSlotCount: 0,
        FormFactor: "Unknown",
        ReadOnlyMemory<PhysicalMemoryModuleSnapshot>.Empty);
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PhysicalMemoryWmiVariant
{
    public const ushort VariantTypeMask = 0x0FFF;
    public const ushort VariantI2 = 2;
    public const ushort VariantI4 = 3;
    public const ushort VariantBStr = 8;
    public const ushort VariantI1 = 16;
    public const ushort VariantUI1 = 17;
    public const ushort VariantUI2 = 18;
    public const ushort VariantUI4 = 19;
    public const ushort VariantI8 = 20;
    public const ushort VariantUI8 = 21;
    public const ushort VariantInt = 22;
    public const ushort VariantUInt = 23;

    [FieldOffset(0)]
    public ushort VariantType;

    [FieldOffset(8)]
    public sbyte SByteValue;

    [FieldOffset(8)]
    public byte ByteValue;

    [FieldOffset(8)]
    public short Int16Value;

    [FieldOffset(8)]
    public ushort UInt16Value;

    [FieldOffset(8)]
    public int Int32Value;

    [FieldOffset(8)]
    public uint UInt32Value;

    [FieldOffset(8)]
    public long Int64Value;

    [FieldOffset(8)]
    public ulong UInt64Value;

    [FieldOffset(8)]
    public IntPtr PointerValue;
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("dc12a687-737f-11cf-884d-00aa004b2e24")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IPhysicalMemoryWbemLocator
{
    [PreserveSig]
    int ConnectServer(
        [MarshalAs(UnmanagedType.BStr)] string networkResource,
        [MarshalAs(UnmanagedType.BStr)] string? user,
        [MarshalAs(UnmanagedType.BStr)] string? password,
        [MarshalAs(UnmanagedType.BStr)] string? locale,
        int securityFlags,
        [MarshalAs(UnmanagedType.BStr)] string? authority,
        IntPtr context,
        out IntPtr services);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("9556dc99-828c-11cf-a37e-00aa003240c7")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IPhysicalMemoryWbemServices
{
    void UnusedOpenNamespace();
    void UnusedCancelAsyncCall();
    void UnusedQueryObjectSink();
    void UnusedGetObject();
    void UnusedGetObjectAsync();
    void UnusedPutClass();
    void UnusedPutClassAsync();
    void UnusedDeleteClass();
    void UnusedDeleteClassAsync();
    void UnusedCreateClassEnum();
    void UnusedCreateClassEnumAsync();
    void UnusedPutInstance();
    void UnusedPutInstanceAsync();
    void UnusedDeleteInstance();
    void UnusedDeleteInstanceAsync();
    void UnusedCreateInstanceEnum();
    void UnusedCreateInstanceEnumAsync();

    [PreserveSig]
    int ExecQuery(
        [MarshalAs(UnmanagedType.BStr)] string queryLanguage,
        [MarshalAs(UnmanagedType.BStr)] string query,
        int flags,
        IntPtr context,
        out IntPtr enumerator);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("dc12a681-737f-11cf-884d-00aa004b2e24")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IPhysicalMemoryWbemClassObject
{
    void UnusedGetQualifierSet();

    [PreserveSig]
    int Get(
        [MarshalAs(UnmanagedType.BStr)] string propertyName,
        int flags,
        IntPtr value,
        IntPtr type,
        IntPtr flavor);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("027947e1-d731-11ce-a357-000000000001")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IPhysicalMemoryEnumWbemClassObject
{
    void UnusedReset();

    [PreserveSig]
    int Next(int timeout, uint count, out IntPtr objects, out uint returnedCount);
}

internal sealed class WmiComApartmentScope : IDisposable
{
    private static int _securityInitialized;
    private readonly bool _uninitialize;

    private WmiComApartmentScope(bool uninitialize) => _uninitialize = uninitialize;

    public static WmiComApartmentScope Enter()
    {
        int result = PhysicalMemoryWmiNative.CoInitializeEx(
            IntPtr.Zero,
            PhysicalMemoryWmiNative.CoinitMultithreaded);
        bool uninitialize = result is PhysicalMemoryWmiNative.SOk or PhysicalMemoryWmiNative.SFalse;
        if (result < 0 && result != PhysicalMemoryWmiNative.RpcEChangedMode)
            Marshal.ThrowExceptionForHR(result);

        if (Interlocked.Exchange(ref _securityInitialized, value: 1) == 0)
        {
            int securityResult = PhysicalMemoryWmiNative.CoInitializeSecurity(
                IntPtr.Zero,
                authenticationServiceCount: -1,
                IntPtr.Zero,
                IntPtr.Zero,
                PhysicalMemoryWmiNative.RpcCAuthenticationLevelDefault,
                PhysicalMemoryWmiNative.RpcCImpersonationLevelImpersonate,
                IntPtr.Zero,
                PhysicalMemoryWmiNative.EoacNone,
                IntPtr.Zero);
            if (securityResult < 0 && securityResult != PhysicalMemoryWmiNative.RpcETooLate)
                Marshal.ThrowExceptionForHR(securityResult);
        }

        return new WmiComApartmentScope(uninitialize);
    }

    public void Dispose()
    {
        if (_uninitialize) PhysicalMemoryWmiNative.CoUninitialize();
    }
}

internal static partial class PhysicalMemoryWmiNative
{
    public static readonly Guid ClsidWbemLocator = new("4590f811-1d3a-11d0-891f-00aa004b2e24");

    public const int SOk = 0;
    public const int SFalse = 1;
    public const int RpcEChangedMode = unchecked((int)0x80010106);
    public const int RpcETooLate = unchecked((int)0x80010119);
    public const uint CoinitMultithreaded = 0x0;
    public const uint RpcCAuthenticationWinNT = 10;
    public const uint RpcCAuthorizationNone = 0;
    public const uint RpcCAuthenticationLevelDefault = 0;
    public const uint RpcCAuthenticationLevelCall = 3;
    public const uint RpcCImpersonationLevelImpersonate = 3;
    public const uint EoacNone = 0;

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(IntPtr reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeSecurity(
        IntPtr securityDescriptor,
        int authenticationServiceCount,
        IntPtr authenticationServices,
        IntPtr reserved1,
        uint authenticationLevel,
        uint impersonationLevel,
        IntPtr authenticationList,
        uint capabilities,
        IntPtr reserved3);

    [LibraryImport("ole32.dll")]
    public static partial int CoSetProxyBlanket(
        IntPtr proxy,
        uint authenticationService,
        uint authorizationService,
        IntPtr serverPrincipalName,
        uint authenticationLevel,
        uint impersonationLevel,
        IntPtr authenticationInfo,
        uint capabilities);

    [LibraryImport("oleaut32.dll")]
    public static partial int VariantClear(IntPtr variant);
}
