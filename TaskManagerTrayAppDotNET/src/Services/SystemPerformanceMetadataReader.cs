using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Reads static CPU topology and low-frequency system performance metadata directly from Windows.</summary>
internal sealed class SystemPerformanceMetadataReader : IDisposable
{
    private const string ProcessorRegistryPath = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string ProcessorNameValue = "ProcessorNameString";

    // Taskmgr.exe reads this pair, counter IDs 33 and 34 of the Processor Information counter set, for Speed
    private const string ActualFrequencyTotalPath =
        @"\Processor Information(_Total)\Actual Frequency";

    // Its base counter is elapsed time in the same units as the Actual Frequency busy-time base
    private const string ProcessorUtilityTotalPath =
        @"\Processor Information(_Total)\% Processor Utility";

    private const uint AllProcessorGroups = 0xFFFF;
    private const int ProcessorPowerInformation = 11;
    private const uint RelationAll = 0xFFFF;
    private const int RelationProcessorCore = 0;
    private const int RelationCache = 2;
    private const int RelationProcessorPackage = 3;
    private const uint ProcessorFeatureVirtualizationFirmwareEnabled = 21;
    private const ulong Kilobyte = 1_024;
    private const ulong MegahertzToHertz = 1_000_000;
    private const int LogicalProcessorInformationHeaderSize = 8;
    private const int CacheLevelOffset = 8;
    private const int CacheSizeOffset = 12;
    private const int ErrorInsufficientBuffer = 122;
    private const uint PdhSuccess = 0;
    private const uint PdhValidData = 0;
    private const uint PdhNewData = 1;

    private readonly string _processorName = ReadProcessorName();
    private readonly ProcessorTopology _topology = ReadProcessorTopology();
    private readonly ulong _installedPhysicalMemoryBytes = ReadInstalledPhysicalMemoryBytes();
    private readonly ProcessorFrequencyAccumulator _frequencyAccumulator = new();
    private readonly SystemProcessSnapshot _processSnapshot = new();
    private IntPtr _frequencyQuery;
    private IntPtr _actualFrequencyTotalCounter;
    private IntPtr _processorUtilityTotalCounter;
    private bool _disposed;

    public SystemPerformanceMetadataReader()
    {
        if (PdhOpenQueryW(dataSource: null, IntPtr.Zero, out _frequencyQuery) != PdhSuccess) return;

        uint status = PdhAddEnglishCounterW(
            _frequencyQuery,
            ActualFrequencyTotalPath,
            IntPtr.Zero,
            out _actualFrequencyTotalCounter);
        if (status == PdhSuccess)
        {
            status = PdhAddEnglishCounterW(
                _frequencyQuery,
                ProcessorUtilityTotalPath,
                IntPtr.Zero,
                out _processorUtilityTotalCounter);
        }

        if (status == PdhSuccess) return;

        _ = PdhCloseQuery(_frequencyQuery);
        _frequencyQuery = IntPtr.Zero;
        _actualFrequencyTotalCounter = IntPtr.Zero;
        _processorUtilityTotalCounter = IntPtr.Zero;
    }

    /// <summary>Captures frequency, counts, commit data, and uptime for the current sample.</summary>
    /// <param name="includeProcessTotals">Walks every process for the process, thread, and handle counts.</param>
    public SystemPerformanceMetadataSample Sample(bool includeProcessTotals)
    {
        bool hasFrequencyData = TryReadProcessorFrequency(
            out ulong currentSpeedHertz,
            out ulong baseSpeedHertz);
        if (TryReadActualFrequency(out ulong actualFrequencyHertz))
        {
            currentSpeedHertz = actualFrequencyHertz;
            hasFrequencyData = true;
        }

        bool hasPerformanceInformation = TryReadPerformanceInformation(
            out SystemPerformanceInformation performanceInformation);
        bool hasProcessTotals = TryReadProcessTotals(
            includeProcessTotals,
            out SystemProcessTotals processTotals);
        return new SystemPerformanceMetadataSample(
            _processorName,
            hasFrequencyData,
            currentSpeedHertz,
            baseSpeedHertz,
            _topology.SocketCount,
            _topology.CoreCount,
            _topology.LogicalProcessorCount,
            IsProcessorFeaturePresent(ProcessorFeatureVirtualizationFirmwareEnabled),
            _topology.L1CacheBytes,
            _topology.L2CacheBytes,
            _topology.L3CacheBytes,
            hasPerformanceInformation,
            performanceInformation,
            hasProcessTotals,
            processTotals,
            _installedPhysicalMemoryBytes,
            TimeSpan.FromMilliseconds(GetTickCount64()));
    }

    private static string ReadProcessorName()
    {
        try
        {
            using RegistryKey? processorKey = Registry.LocalMachine.OpenSubKey(ProcessorRegistryPath);
            string? processorName = processorKey?.GetValue(ProcessorNameValue) as string;
            return string.IsNullOrWhiteSpace(processorName) ? "CPU" : processorName.Trim();
        }
        catch (Exception exception) when (exception is System.Security.SecurityException
                                              or UnauthorizedAccessException
                                              or IOException)
        {
            TADNLog.Log($"SystemPerformanceMetadataReader processor name: {exception.Message}");
            return "CPU";
        }
    }

    private static ProcessorTopology ReadProcessorTopology()
    {
        uint requiredLength = 0;
        if (GetLogicalProcessorInformationEx(RelationAll, IntPtr.Zero, ref requiredLength)
            || Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer
            || requiredLength < LogicalProcessorInformationHeaderSize)
            return ProcessorTopology.WithLogicalProcessorCount(ReadActiveProcessorCount());

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)requiredLength));
        try
        {
            uint returnedLength = requiredLength;
            if (!GetLogicalProcessorInformationEx(RelationAll, buffer, ref returnedLength))
                return ProcessorTopology.WithLogicalProcessorCount(ReadActiveProcessorCount());

            int coreCount = 0;
            int socketCount = 0;
            ulong l1CacheBytes = 0;
            ulong l2CacheBytes = 0;
            ulong l3CacheBytes = 0;
            int offset = 0;
            while (offset <= returnedLength - LogicalProcessorInformationHeaderSize)
            {
                IntPtr entry = IntPtr.Add(buffer, offset);
                int relationship = Marshal.ReadInt32(entry);
                int entrySize = Marshal.ReadInt32(entry, sizeof(int));
                if (entrySize < LogicalProcessorInformationHeaderSize
                    || entrySize > returnedLength - offset)
                    break;

                switch (relationship)
                {
                    case RelationProcessorCore:
                        coreCount++;
                        break;
                    case RelationProcessorPackage:
                        socketCount++;
                        break;
                    case RelationCache when entrySize >= CacheSizeOffset + sizeof(uint):
                    {
                        byte level = Marshal.ReadByte(entry, CacheLevelOffset);
                        uint cacheSize = unchecked((uint)Marshal.ReadInt32(entry, CacheSizeOffset));
                        switch (level)
                        {
                            case 1:
                                l1CacheBytes = SaturatingAdd(l1CacheBytes, cacheSize);
                                break;
                            case 2:
                                l2CacheBytes = SaturatingAdd(l2CacheBytes, cacheSize);
                                break;
                            case 3:
                                l3CacheBytes = SaturatingAdd(l3CacheBytes, cacheSize);
                                break;
                        }

                        break;
                    }
                }

                offset += entrySize;
            }

            return new ProcessorTopology(
                socketCount,
                coreCount,
                ReadActiveProcessorCount(),
                l1CacheBytes,
                l2CacheBytes,
                l3CacheBytes);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Reads the mean CurrentMhz and the mean MaxMhz that CallNtPowerInformation reports per processor.</summary>
    /// <remarks>
    /// The mean CurrentMhz is what Taskmgr.exe shows as Speed on its data-layer path
    /// (WdcCpuMonitor::UpdateProcessorFrequency). It is the nominal clock times the current throttle, so it only
    /// stands in until the first Actual Frequency window qualifies.
    /// </remarks>
    private static bool TryReadProcessorFrequency(
        out ulong currentSpeedHertz,
        out ulong baseSpeedHertz)
    {
        int processorCount = ReadActiveProcessorCount();
        if (processorCount <= 0)
        {
            currentSpeedHertz = 0;
            baseSpeedHertz = 0;
            return false;
        }

        int entrySize = Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>();
        IntPtr buffer = Marshal.AllocHGlobal(checked(processorCount * entrySize));
        try
        {
            uint status = CallNtPowerInformation(
                ProcessorPowerInformation,
                IntPtr.Zero,
                inputBufferSize: 0,
                buffer,
                checked((uint)(processorCount * entrySize)));
            if (status != 0)
            {
                currentSpeedHertz = 0;
                baseSpeedHertz = 0;
                return false;
            }

            ulong currentMegahertzTotal = 0;
            ulong baseMegahertzTotal = 0;
            for (int processorIndex = 0; processorIndex < processorCount; processorIndex++)
            {
                PROCESSOR_POWER_INFORMATION processorInformation =
                    Marshal.PtrToStructure<PROCESSOR_POWER_INFORMATION>(
                        IntPtr.Add(buffer, processorIndex * entrySize));
                currentMegahertzTotal = SaturatingAdd(
                    currentMegahertzTotal,
                    processorInformation.CurrentMegahertz);
                baseMegahertzTotal = SaturatingAdd(
                    baseMegahertzTotal,
                    processorInformation.MaxMegahertz);
            }

            currentSpeedHertz = currentMegahertzTotal / (ulong)processorCount * MegahertzToHertz;
            baseSpeedHertz = baseMegahertzTotal / (ulong)processorCount * MegahertzToHertz;
            return currentSpeedHertz > 0 || baseSpeedHertz > 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Discards the current PDH delta baseline after a sampling reset.</summary>
    internal void ResetFrequencyBaseline() => _frequencyAccumulator.Reset();

    /// <summary>Reads the busy-time-weighted mean clock of all logical processors, held until a newer one qualifies.</summary>
    private bool TryReadActualFrequency(out ulong currentSpeedHertz)
    {
        currentSpeedHertz = 0;
        if (_disposed
            || _frequencyQuery == IntPtr.Zero
            || PdhCollectQueryData(_frequencyQuery) != PdhSuccess)
            return false;

        long collectedTimestamp = Stopwatch.GetTimestamp();
        if (!TryReadRawCounter(_processorUtilityTotalCounter, out PDH_RAW_COUNTER elapsedValue)
            || !TryReadRawCounter(_actualFrequencyTotalCounter, out PDH_RAW_COUNTER frequencyValue))
            return false;

        // NOTE: both bases are 32-bit counters, so only the low bits carry time
        _ = _frequencyAccumulator.Update(
            new ProcessorFrequencyReading(
                frequencyValue.FirstValue,
                unchecked((uint)frequencyValue.SecondValue)),
            unchecked((uint)elapsedValue.SecondValue),
            collectedTimestamp);
        currentSpeedHertz = ConvertMegahertzToHertz(_frequencyAccumulator.Megahertz);
        return currentSpeedHertz > 0;
    }

    private static bool TryReadRawCounter(IntPtr counter, out PDH_RAW_COUNTER value) =>
        PdhGetRawCounterValue(counter, out _, out value) == PdhSuccess
        && value.Status is PdhValidData or PdhNewData;

    private static ulong ConvertMegahertzToHertz(double megahertz)
    {
        if (!double.IsFinite(megahertz) || megahertz <= 0) return 0;

        double hertz = megahertz * MegahertzToHertz;
        return hertz >= ulong.MaxValue
            ? ulong.MaxValue
            : (ulong)Math.Round(hertz, MidpointRounding.AwayFromZero);
    }

    /// <summary>Sums every process entry for the counts, or releases the walk buffer while no page shows them.</summary>
    private bool TryReadProcessTotals(bool includeProcessTotals, out SystemProcessTotals totals)
    {
        totals = default;
        if (_disposed) return false;
        if (includeProcessTotals) return _processSnapshot.TryCaptureTotals(out totals);

        _processSnapshot.ReleaseQueryBuffer();
        return false;
    }

    private static bool TryReadPerformanceInformation(out SystemPerformanceInformation information)
    {
        PERFORMANCE_INFORMATION native = new() { Size = (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>() };
        if (!K32GetPerformanceInfo(ref native, native.Size) || native.PageSize == 0)
        {
            information = default;
            return false;
        }

        information = new SystemPerformanceInformation(
            PageCountToBytes(native.CommitTotal, native.PageSize),
            PageCountToBytes(native.CommitLimit, native.PageSize),
            PageCountToBytes(native.SystemCache, native.PageSize),
            PageCountToBytes(native.KernelPaged, native.PageSize),
            PageCountToBytes(native.KernelNonPaged, native.PageSize));
        return true;
    }

    private static ulong ReadInstalledPhysicalMemoryBytes()
    {
        if (!GetPhysicallyInstalledSystemMemory(out ulong installedKilobytes)) return 0;
        return installedKilobytes > ulong.MaxValue / Kilobyte
            ? ulong.MaxValue
            : installedKilobytes * Kilobyte;
    }

    private static int ReadActiveProcessorCount()
    {
        uint processorCount = GetActiveProcessorCount(AllProcessorGroups);
        return processorCount is > 0 and <= int.MaxValue
            ? (int)processorCount
            : Math.Max(val1: 1, Environment.ProcessorCount);
    }

    private static ulong PageCountToBytes(nuint pageCount, nuint pageSize)
    {
        ulong count = pageCount;
        ulong size = pageSize;
        return size > 0 && count > ulong.MaxValue / size ? ulong.MaxValue : count * size;
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        left > ulong.MaxValue - right ? ulong.MaxValue : left + right;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _processSnapshot.Dispose();
        if (_frequencyQuery == IntPtr.Zero) return;
        _ = PdhCloseQuery(_frequencyQuery);
        _frequencyQuery = IntPtr.Zero;
        _actualFrequencyTotalCounter = IntPtr.Zero;
        _processorUtilityTotalCounter = IntPtr.Zero;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(
        uint relationshipType,
        IntPtr buffer,
        ref uint returnedLength);

    [DllImport("kernel32.dll")]
    private static extern uint GetActiveProcessorCount(uint groupNumber);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessorFeaturePresent(uint processorFeature);

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32GetPerformanceInfo(
        ref PERFORMANCE_INFORMATION performanceInformation,
        uint size);

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(
        int informationLevel,
        IntPtr inputBuffer,
        uint inputBufferSize,
        IntPtr outputBuffer,
        uint outputBufferSize);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(
        string? dataSource,
        IntPtr userData,
        out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(
        IntPtr query,
        string fullCounterPath,
        IntPtr userData,
        out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetRawCounterValue(
        IntPtr counter,
        out uint counterType,
        out PDH_RAW_COUNTER value);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_RAW_COUNTER
    {
        public uint Status;
        public uint TimeStampLow;
        public uint TimeStampHigh;
        public long FirstValue;
        public long SecondValue;
        public uint MultiCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSOR_POWER_INFORMATION
    {
        public uint Number;
        public uint MaxMegahertz;
        public uint CurrentMegahertz;
        public uint MegahertzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PERFORMANCE_INFORMATION
    {
        public uint Size;
        public nuint CommitTotal;
        public nuint CommitLimit;
        public nuint CommitPeak;
        public nuint PhysicalTotal;
        public nuint PhysicalAvailable;
        public nuint SystemCache;
        public nuint KernelTotal;
        public nuint KernelPaged;
        public nuint KernelNonPaged;
        public nuint PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    private readonly record struct ProcessorTopology(
        int SocketCount,
        int CoreCount,
        int LogicalProcessorCount,
        ulong L1CacheBytes,
        ulong L2CacheBytes,
        ulong L3CacheBytes)
    {
        public static ProcessorTopology WithLogicalProcessorCount(int logicalProcessorCount) =>
            new(SocketCount: 0, CoreCount: 0, logicalProcessorCount, L1CacheBytes: 0, L2CacheBytes: 0, L3CacheBytes: 0);
    }
}

internal readonly record struct SystemPerformanceMetadataSample(
    string ProcessorName,
    bool HasFrequencyData,
    ulong CurrentSpeedHertz,
    ulong BaseSpeedHertz,
    int SocketCount,
    int CoreCount,
    int LogicalProcessorCount,
    bool IsVirtualizationFirmwareEnabled,
    ulong L1CacheBytes,
    ulong L2CacheBytes,
    ulong L3CacheBytes,
    bool HasPerformanceInformation,
    SystemPerformanceInformation PerformanceInformation,
    bool HasProcessTotals,
    SystemProcessTotals ProcessTotals,
    ulong InstalledPhysicalMemoryBytes,
    TimeSpan Uptime);

internal readonly record struct SystemPerformanceInformation(
    ulong CommittedBytes,
    ulong CommitLimitBytes,
    ulong CachedBytes,
    ulong PagedPoolBytes,
    ulong NonPagedPoolBytes);
