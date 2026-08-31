namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Normalizes live disk counters and static metadata for detail-view consumption.</summary>
internal static class DiskPerformanceDetailsFactory
{
    private const string GenericDiskType = "Disk";
    private const string SolidStateType = "SSD";
    private const string RotationalType = "HDD";

    public static DiskPerformanceDetailsSnapshot Create(
        DiskPerformanceSnapshot performance,
        DiskDeviceMetadataSnapshot metadata)
    {
        ArgumentNullException.ThrowIfNull(performance);

        bool hasMatchingMetadata = metadata.HasDeviceData
                                   && performance.SortKey >= 0
                                   && metadata.PhysicalDiskNumber == (uint)performance.SortKey;
        bool hasPerformanceSample = performance.HasPerformanceSample;
        double readBytesPerSecond = hasPerformanceSample
            ? NormalizeNonnegative(performance.ReadBytesPerSecond)
            : 0;
        double writeBytesPerSecond = hasPerformanceSample
            ? NormalizeNonnegative(performance.WriteBytesPerSecond)
            : 0;
        double transferBytesPerSecond = SaturatingAdd(
            readBytesPerSecond,
            writeBytesPerSecond);

        string volumeNames = hasMatchingMetadata
                             && !string.IsNullOrWhiteSpace(metadata.VolumeNames)
            ? metadata.VolumeNames
            : performance.VolumeNames;
        ulong formattedCapacityBytes = hasMatchingMetadata && metadata.HasVolumeData
            ? metadata.FormattedCapacityBytes
            : performance.FormattedCapacityBytes;
        DiskMediaKind mediaKind = hasMatchingMetadata
            ? metadata.MediaKind
            : DiskMediaKind.Unknown;

        return new DiskPerformanceDetailsSnapshot(
            performance.DeviceID,
            performance.SortKey,
            performance.Name,
            volumeNames,
            FormatDeviceType(mediaKind, performance.DeviceType),
            hasPerformanceSample,
            hasPerformanceSample ? NormalizePercent(performance.ActiveTimePercent) : 0,
            transferBytesPerSecond,
            readBytesPerSecond,
            writeBytesPerSecond,
            hasPerformanceSample
                ? NormalizeNonnegative(performance.AverageResponseTimeMilliseconds)
                : 0,
            performance.CapacityBytes,
            formattedCapacityBytes,
            hasMatchingMetadata && metadata.HasSystemDiskData,
            hasMatchingMetadata && metadata.IsSystemDisk,
            hasMatchingMetadata && metadata.HasPageFileData,
            hasMatchingMetadata && metadata.HasPageFile);
    }

    /// <summary>Combines the media behavior and storage bus into Task Manager's type form.</summary>
    internal static string FormatDeviceType(DiskMediaKind mediaKind, string busType)
    {
        string normalizedBusType = busType.Trim();
        if (normalizedBusType.StartsWith(SolidStateType, StringComparison.OrdinalIgnoreCase)
            || normalizedBusType.StartsWith(RotationalType, StringComparison.OrdinalIgnoreCase))
            return normalizedBusType;

        string mediaType = mediaKind switch
        {
            DiskMediaKind.SolidState => SolidStateType,
            DiskMediaKind.Rotational => RotationalType,
            _ => string.Empty
        };
        if (mediaType.Length == 0)
            return normalizedBusType.Length > 0 ? normalizedBusType : GenericDiskType;
        if (normalizedBusType.Length == 0
            || normalizedBusType.Equals(GenericDiskType, StringComparison.OrdinalIgnoreCase)
            || normalizedBusType.Equals(value: "Unknown", StringComparison.OrdinalIgnoreCase))
            return mediaType;

        return string.Concat(mediaType, str1: " (", normalizedBusType, str3: ")");
    }

    private static double NormalizePercent(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, min: 0, max: 100) : 0;

    private static double NormalizeNonnegative(double value) =>
        double.IsFinite(value) ? Math.Max(val1: 0, value) : 0;

    private static double SaturatingAdd(double left, double right)
    {
        double sum = left + right;
        return double.IsFinite(sum) ? sum : double.MaxValue;
    }
}
