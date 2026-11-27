namespace TaskManagerTrayAppDotNET.Models;

/// <summary>One stable GPU engine lane selected for the device detail graphs.</summary>
internal readonly record struct GPUPerformanceDetailEngineSnapshot(
    int EngineIndex,
    string Name,
    bool HasUtilizationSample,
    double UtilizationPercent);

/// <summary>Additional dynamic and static values shown by the GPU device detail page.</summary>
internal sealed record GPUPerformanceDetailsSnapshot(
    ReadOnlyMemory<GPUPerformanceDetailEngineSnapshot> Engines,
    bool HasTemperatureData,
    double TemperatureCelsius,
    bool HasPowerData,
    double PowerPercent,
    bool HasClockData,
    ulong ClockHertz,
    ulong MaximumClockHertz,
    bool HasMemoryClockData,
    ulong MemoryClockHertz,
    string DriverVersion,
    DateOnly? DriverDate,
    string DirectXVersion,
    string FeatureLevel,
    string PhysicalLocation,
    bool HasHardwareReservedMemoryData,
    ulong HardwareReservedMemoryBytes,
    GPUAdapterIdentity Identity)
{
    public static GPUPerformanceDetailsSnapshot Empty { get; } = new(
        ReadOnlyMemory<GPUPerformanceDetailEngineSnapshot>.Empty,
        HasTemperatureData: false,
        TemperatureCelsius: 0,
        HasPowerData: false,
        PowerPercent: 0,
        HasClockData: false,
        ClockHertz: 0,
        MaximumClockHertz: 0,
        HasMemoryClockData: false,
        MemoryClockHertz: 0,
        string.Empty,
        DriverDate: null,
        string.Empty,
        string.Empty,
        string.Empty,
        HasHardwareReservedMemoryData: false,
        HardwareReservedMemoryBytes: 0,
        GPUAdapterIdentity.Empty);
}

/// <summary>Static GPU metadata retained by adapter LUID between performance samples.</summary>
internal sealed record GPUAdapterHardwareMetadata(
    bool HasMetadata,
    string DriverVersion,
    DateOnly? DriverDate,
    string DirectXVersion,
    string FeatureLevel,
    string PhysicalLocation,
    bool HasHardwareReservedMemoryData,
    ulong HardwareReservedMemoryBytes,
    ReadOnlyMemory<GPUAdapterEngineIdentity> EngineCatalog,
    GPUAdapterIdentity Identity,
    int ClockNodeOrdinal)
{
    public static GPUAdapterHardwareMetadata Empty { get; } = new(
        HasMetadata: false,
        string.Empty,
        DriverDate: null,
        string.Empty,
        string.Empty,
        string.Empty,
        HasHardwareReservedMemoryData: false,
        HardwareReservedMemoryBytes: 0,
        ReadOnlyMemory<GPUAdapterEngineIdentity>.Empty,
        GPUAdapterIdentity.Empty,
        ClockNodeOrdinal: -1);
}

/// <summary>Kernel-reported identity for one schedulable GPU engine node.</summary>
internal readonly record struct GPUAdapterEngineIdentity(int EngineIndex, string Name);

/// <summary>Device-manager identity of one GPU: PnP IDs, the vendor's driver and vBIOS versions, and its PCIe link.</summary>
internal sealed record GPUAdapterIdentity(
    string DeviceInstanceID,
    GPUHardwareID HardwareID,
    string PublicDriverVersion,
    string VBIOSVersion,
    GPUPCIeLink Link,
    GPUPCIAddress Address)
{
    public static GPUAdapterIdentity Empty { get; } = new(
        string.Empty,
        default,
        string.Empty,
        string.Empty,
        default,
        default);
}

/// <summary>The vendor, device, subsystem and revision fields of a PCI hardware ID.</summary>
internal readonly record struct GPUHardwareID(
    bool HasValue,
    ushort VendorID,
    ushort DeviceID,
    ushort SubsystemVendorID,
    ushort SubsystemID,
    byte Revision);

/// <summary>
/// Negotiated and maximum PCIe link generation and lane count. <see cref="BehindOnCardSwitch"/> marks a GPU's link to
/// a PCIe switch on its own card, which says nothing about the slot.
/// </summary>
internal readonly record struct GPUPCIeLink(
    bool HasValue,
    uint CurrentGeneration,
    uint CurrentWidth,
    uint MaximumGeneration,
    uint MaximumWidth,
    bool BehindOnCardSwitch = false);

/// <summary>PCI bus, device and function numbers the graphics kernel reports for an adapter.</summary>
internal readonly record struct GPUPCIAddress(bool HasValue, uint Bus, uint Device, uint Function);
