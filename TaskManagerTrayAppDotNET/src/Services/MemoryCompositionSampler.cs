using System.Runtime.InteropServices;
using TaskManagerTrayAppDotNET.Interop;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Samples Task Manager-style physical-memory lists and compression-store totals.</summary>
internal sealed unsafe class MemoryCompositionSampler : IDisposable
{
    private const string CacheBytesPath = @"\Memory\Cache Bytes";
    private const string FreeBytesPath = @"\Memory\Free & Zero Page List Bytes";
    private const string ModifiedBytesPath = @"\Memory\Modified Page List Bytes";
    private const string StandbyCoreBytesPath = @"\Memory\Standby Cache Core Bytes";
    private const string StandbyNormalBytesPath = @"\Memory\Standby Cache Normal Priority Bytes";
    private const string StandbyReserveBytesPath = @"\Memory\Standby Cache Reserve Bytes";
    private const uint PdhSuccess = 0;
    private const uint PdhValidData = 0;
    private const uint PdhNewData = 1;
    private const uint PdhFormatLarge = 0x00000400;
    private const int SystemStoreInformation = 109;
    private const uint StoreInformationVersion = 1;
    private const uint MemoryCompressionInformationRequest = 22;
    private const uint CompressionInformationVersionV1 = 3;
    private const uint CompressionInformationSizeV1 = 40;

    private readonly PdhQuery _query;
    private IntPtr _cacheBytesCounter;
    private IntPtr _freeBytesCounter;
    private IntPtr _modifiedBytesCounter;
    private IntPtr _standbyCoreBytesCounter;
    private IntPtr _standbyNormalBytesCounter;
    private IntPtr _standbyReserveBytesCounter;
    private bool _disposed;

    public MemoryCompositionSampler() => _query = new PdhQuery(nameof(MemoryCompositionSampler), AddCounters);

    /// <summary>Captures the current memory-list sizes and compression-store ratio.</summary>
    public MemoryCompositionSample Sample()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool hasCompositionData = false;
        ulong cacheBytes = 0;
        ulong freeBytes = 0;
        ulong modifiedBytes = 0;
        ulong standbyBytes = 0;
        if (_query.TryCollect()
                                  && TryReadByteCounter(_cacheBytesCounter, out cacheBytes)
                                  && TryReadByteCounter(_freeBytesCounter, out freeBytes)
                                  && TryReadByteCounter(_modifiedBytesCounter, out modifiedBytes)
                                  && TryReadByteCounter(_standbyCoreBytesCounter, out ulong standbyCoreBytes)
                                  && TryReadByteCounter(_standbyNormalBytesCounter, out ulong standbyNormalBytes)
                                  && TryReadByteCounter(_standbyReserveBytesCounter, out ulong standbyReserveBytes))
        {
            standbyBytes = SaturatingAdd(
                SaturatingAdd(standbyCoreBytes, standbyNormalBytes),
                standbyReserveBytes);
            hasCompositionData = true;
        }

        bool hasCompressionData = TryReadCompressionInformation(
            out ulong compressedBytes,
            out ulong estimatedDataBytes,
            out ulong savedBytes);
        return new MemoryCompositionSample(
            hasCompositionData,
            cacheBytes,
            freeBytes,
            modifiedBytes,
            standbyBytes,
            hasCompressionData,
            compressedBytes,
            estimatedDataBytes,
            savedBytes);
    }

    /// <summary>Normalizes independently sampled lists into an exact physical-memory composition.</summary>
    internal static NormalizedMemoryComposition Normalize(
        ulong totalPhysicalBytes,
        ulong fallbackAvailableBytes,
        MemoryCompositionSample sample)
    {
        ulong clampedFallbackAvailable = Math.Min(fallbackAvailableBytes, totalPhysicalBytes);
        if (!sample.HasCompositionData)
        {
            ulong fallbackInUse = totalPhysicalBytes - clampedFallbackAvailable;
            ulong fallbackCompressed = sample.HasCompressionData
                ? Math.Min(sample.CompressedBytes, fallbackInUse)
                : 0;
            ulong fallbackEstimated = sample.HasCompressionData
                ? Math.Max(fallbackCompressed, sample.EstimatedDataBytes)
                : 0;
            return new NormalizedMemoryComposition(
                HasCompositionData: false,
                fallbackInUse,
                clampedFallbackAvailable,
                ModifiedBytes: 0,
                clampedFallbackAvailable,
                FreeBytes: 0,
                CachedBytes: 0,
                sample.HasCompressionData,
                fallbackCompressed,
                fallbackEstimated,
                fallbackEstimated - fallbackCompressed);
        }

        ulong remainingBytes = totalPhysicalBytes;
        ulong freeBytes = Math.Min(sample.FreeBytes, remainingBytes);
        remainingBytes -= freeBytes;
        ulong standbyBytes = Math.Min(sample.StandbyBytes, remainingBytes);
        remainingBytes -= standbyBytes;
        ulong modifiedBytes = Math.Min(sample.ModifiedBytes, remainingBytes);
        remainingBytes -= modifiedBytes;
        ulong inUseBytes = remainingBytes;
        ulong availableBytes = SaturatingAdd(freeBytes, standbyBytes);
        ulong cachedBytes = SaturatingAdd(
            SaturatingAdd(sample.CacheBytes, modifiedBytes),
            standbyBytes);
        ulong compressedBytes = sample.HasCompressionData
            ? Math.Min(sample.CompressedBytes, inUseBytes)
            : 0;
        ulong estimatedDataBytes = sample.HasCompressionData
            ? Math.Max(compressedBytes, sample.EstimatedDataBytes)
            : 0;
        return new NormalizedMemoryComposition(
            HasCompositionData: true,
            inUseBytes,
            availableBytes,
            modifiedBytes,
            standbyBytes,
            freeBytes,
            cachedBytes,
            sample.HasCompressionData,
            compressedBytes,
            estimatedDataBytes,
            estimatedDataBytes - compressedBytes);
    }

    private uint AddCounters(PdhQuery query)
    {
        ClearCounterHandles();
        uint status = query.AddCounter(CacheBytesPath, out _cacheBytesCounter);
        if (status == PdhSuccess) status = query.AddCounter(FreeBytesPath, out _freeBytesCounter);
        if (status == PdhSuccess) status = query.AddCounter(ModifiedBytesPath, out _modifiedBytesCounter);
        if (status == PdhSuccess) status = query.AddCounter(StandbyCoreBytesPath, out _standbyCoreBytesCounter);
        if (status == PdhSuccess) status = query.AddCounter(StandbyNormalBytesPath, out _standbyNormalBytesCounter);
        if (status == PdhSuccess) status = query.AddCounter(StandbyReserveBytesPath, out _standbyReserveBytesCounter);
        if (status != PdhSuccess) ClearCounterHandles();
        return status;
    }

    private static bool TryReadByteCounter(IntPtr counter, out ulong value)
    {
        value = 0;
        uint status = PdhGetFormattedCounterValue(
            counter,
            PdhFormatLarge,
            out uint _,
            out PDH_FORMATTED_COUNTER_VALUE formattedValue);
        if (status != PdhSuccess
            || formattedValue.Status is not (PdhValidData or PdhNewData)
            || formattedValue.LargeValue < 0)
            return false;

        value = (ulong)formattedValue.LargeValue;
        return true;
    }

    private static bool TryReadCompressionInformation(
        out ulong compressedBytes,
        out ulong estimatedDataBytes,
        out ulong savedBytes)
    {
        SM_STORE_COMPRESSION_INFORMATION_REQUEST compressionInformation = new()
        {
            Version = CompressionInformationVersionV1
        };
        SYSTEM_STORE_INFORMATION storeInformation = new()
        {
            Version = StoreInformationVersion,
            StoreInformationClass = MemoryCompressionInformationRequest,
            Data = (IntPtr)(&compressionInformation),
            Length = CompressionInformationSizeV1
        };
        int status = NtDll.NtQuerySystemInformation(
            SystemStoreInformation,
            &storeInformation,
            (uint)sizeof(SYSTEM_STORE_INFORMATION),
            returnLength: null);
        if (status < 0 || compressionInformation.TotalCompressedSize == 0)
        {
            compressedBytes = 0;
            estimatedDataBytes = 0;
            savedBytes = 0;
            return false;
        }

        compressedBytes = compressionInformation.WorkingSetSize;
        double estimatedBytes = compressedBytes
                                * (double)compressionInformation.TotalDataCompressed
                                / compressionInformation.TotalCompressedSize;
        estimatedDataBytes = estimatedBytes >= ulong.MaxValue
            ? ulong.MaxValue
            : (ulong)Math.Round(estimatedBytes, MidpointRounding.AwayFromZero);
        estimatedDataBytes = Math.Max(compressedBytes, estimatedDataBytes);
        savedBytes = estimatedDataBytes - compressedBytes;
        return true;
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        left > ulong.MaxValue - right ? ulong.MaxValue : left + right;

    private void ClearCounterHandles()
    {
        _cacheBytesCounter = IntPtr.Zero;
        _freeBytesCounter = IntPtr.Zero;
        _modifiedBytesCounter = IntPtr.Zero;
        _standbyCoreBytesCounter = IntPtr.Zero;
        _standbyNormalBytesCounter = IntPtr.Zero;
        _standbyReserveBytesCounter = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _query.Dispose();
        ClearCounterHandles();
    }

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(
        IntPtr counter,
        uint format,
        out uint valueType,
        out PDH_FORMATTED_COUNTER_VALUE value);

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PDH_FORMATTED_COUNTER_VALUE
    {
        [FieldOffset(0)]
        public uint Status;

        [FieldOffset(8)]
        public long LargeValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_STORE_INFORMATION
    {
        public uint Version;
        public uint StoreInformationClass;
        public IntPtr Data;
        public uint Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SM_STORE_COMPRESSION_INFORMATION_REQUEST
    {
        public uint Version;
        public uint CompressionProcessID;
        public uint WorkingSetSize;
        public nuint TotalDataCompressed;
        public nuint TotalCompressedSize;
        public nuint TotalUniqueDataCompressed;
        public IntPtr PartitionHandle;
    }
}

internal readonly record struct MemoryCompositionSample(
    bool HasCompositionData,
    ulong CacheBytes,
    ulong FreeBytes,
    ulong ModifiedBytes,
    ulong StandbyBytes,
    bool HasCompressionData,
    ulong CompressedBytes,
    ulong EstimatedDataBytes,
    ulong SavedBytes);

internal readonly record struct NormalizedMemoryComposition(
    bool HasCompositionData,
    ulong InUseBytes,
    ulong AvailableBytes,
    ulong ModifiedBytes,
    ulong StandbyBytes,
    ulong FreeBytes,
    ulong CachedBytes,
    bool HasCompressionData,
    ulong CompressedBytes,
    ulong EstimatedDataBytes,
    ulong SavedBytes);
