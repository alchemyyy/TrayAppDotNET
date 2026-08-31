namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Physical storage media behavior reported by the Windows storage stack.</summary>
internal enum DiskMediaKind
{
    Unknown,
    SolidState,
    Rotational
}

/// <summary>Static volume and media metadata for one physical disk number.</summary>
internal readonly record struct DiskDeviceMetadataSnapshot(
    bool HasDeviceData,
    uint PhysicalDiskNumber,
    bool HasVolumeData,
    string VolumeNames,
    ulong FormattedCapacityBytes,
    ulong AvailableBytes,
    bool HasSystemDiskData,
    bool IsSystemDisk,
    bool HasPageFileData,
    bool HasPageFile,
    DiskMediaKind MediaKind)
{
    public static DiskDeviceMetadataSnapshot Unavailable(uint physicalDiskNumber) => new(
        HasDeviceData: false,
        physicalDiskNumber,
        HasVolumeData: false,
        string.Empty,
        FormattedCapacityBytes: 0,
        AvailableBytes: 0,
        HasSystemDiskData: false,
        IsSystemDisk: false,
        HasPageFileData: false,
        HasPageFile: false,
        DiskMediaKind.Unknown);
}

/// <summary>Complete normalized values consumed by the Disk performance detail view.</summary>
internal sealed record DiskPerformanceDetailsSnapshot(
    string DeviceID,
    int PhysicalDiskNumber,
    string Model,
    string VolumeNames,
    string DeviceType,
    bool HasPerformanceSample,
    double ActiveTimePercent,
    double TransferBytesPerSecond,
    double ReadBytesPerSecond,
    double WriteBytesPerSecond,
    double AverageResponseTimeMilliseconds,
    ulong CapacityBytes,
    ulong FormattedCapacityBytes,
    bool HasSystemDiskData,
    bool IsSystemDisk,
    bool HasPageFileData,
    bool HasPageFile);

/// <summary>One physical extent belonging to a Windows volume.</summary>
internal readonly record struct DiskVolumeExtent(
    uint PhysicalDiskNumber,
    ulong ExtentLengthBytes);

/// <summary>One physical disk's exact share of a logical byte count.</summary>
internal readonly record struct DiskByteAllocation(
    uint PhysicalDiskNumber,
    ulong Bytes);
