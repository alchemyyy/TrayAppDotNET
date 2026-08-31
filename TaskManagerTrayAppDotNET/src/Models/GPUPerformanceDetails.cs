namespace TaskManagerTrayAppDotNET.Models;

/// <summary>One stable GPU engine lane selected for the device detail graphs.</summary>
internal readonly record struct GPUPerformanceDetailEngineSnapshot(
    int EngineIndex,
    string Name,
    bool HasUtilizationSample,
    double UtilizationPercent);

/// <summary>Additional dynamic and static values shown by the GPU device detail page.</summary>
internal sealed record GPUPerformanceDetailsSnapshot(
    bool HasDetailData,
    ReadOnlyMemory<GPUPerformanceDetailEngineSnapshot> Engines,
    bool HasTemperatureData,
    double TemperatureCelsius,
    string DriverVersion,
    DateOnly? DriverDate,
    string DirectXVersion,
    string FeatureLevel,
    string PhysicalLocation,
    bool HasHardwareReservedMemoryData,
    ulong HardwareReservedMemoryBytes)
{
    public static GPUPerformanceDetailsSnapshot Empty { get; } = new(
        HasDetailData: false,
        ReadOnlyMemory<GPUPerformanceDetailEngineSnapshot>.Empty,
        HasTemperatureData: false,
        TemperatureCelsius: 0,
        string.Empty,
        DriverDate: null,
        string.Empty,
        string.Empty,
        string.Empty,
        HasHardwareReservedMemoryData: false,
        HardwareReservedMemoryBytes: 0);
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
    ReadOnlyMemory<GPUAdapterEngineIdentity> EngineCatalog)
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
        ReadOnlyMemory<GPUAdapterEngineIdentity>.Empty);
}

/// <summary>Kernel-reported identity for one schedulable GPU engine node.</summary>
internal readonly record struct GPUAdapterEngineIdentity(int EngineIndex, string Name);
