using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Reads static CPU topology and low-frequency system performance metadata directly from Windows.</summary>
internal sealed unsafe class SystemPerformanceMetadataReader : IDisposable
{
    private const string ProcessorRegistryPath = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string ProcessorNameValue = "ProcessorNameString";
    private const string ProcessorPerformancePath =
        @"\Processor Information(*)\% Processor Performance";
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
    private const int MaximumCounterInstanceNameLength = 128;
    private const uint PdhSuccess = 0;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhValidData = 0;
    private const uint PdhNewData = 1;
    private const uint PdhFormatDouble = 0x00000200;
    private const uint PdhFormatNoCap100 = 0x00008000;

    private readonly string _processorName = ReadProcessorName();
    private readonly ProcessorTopology _topology = ReadProcessorTopology();
    private readonly ulong _installedPhysicalMemoryBytes = ReadInstalledPhysicalMemoryBytes();
    private IntPtr _processorPerformanceQuery;
    private IntPtr _processorPerformanceCounter;
    private IntPtr _counterBuffer;
    private uint _counterBufferSize;
    private bool _processorPerformanceQueryPrimed;
    private bool _disposed;

    public SystemPerformanceMetadataReader()
    {
        if (PdhOpenQueryW(null, IntPtr.Zero, out _processorPerformanceQuery) != PdhSuccess) return;

        uint status = PdhAddEnglishCounterW(
            _processorPerformanceQuery,
            ProcessorPerformancePath,
            IntPtr.Zero,
            out _processorPerformanceCounter);
        if (status == PdhSuccess) return;

        _ = PdhCloseQuery(_processorPerformanceQuery);
        _processorPerformanceQuery = IntPtr.Zero;
        _processorPerformanceCounter = IntPtr.Zero;
    }

    /// <summary>Captures frequency, counts, commit data, and uptime for the current sample.</summary>
    public SystemPerformanceMetadataSample Sample()
    {
        bool hasFrequencyData = TryReadProcessorFrequency(
            out ulong highestCurrentSpeedHertz,
            out ulong baseSpeedHertz);
        if (TryReadHighestCurrentTurboSpeed(
                baseSpeedHertz,
                out ulong highestTurboSpeedHertz))
        {
            highestCurrentSpeedHertz = highestTurboSpeedHertz;
            hasFrequencyData = true;
        }
        bool hasPerformanceInformation = TryReadPerformanceInformation(
            out SystemPerformanceInformation performanceInformation);
        return new SystemPerformanceMetadataSample(
            _processorName,
            hasFrequencyData,
            highestCurrentSpeedHertz,
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
        {
            return ProcessorTopology.WithLogicalProcessorCount(ReadActiveProcessorCount());
        }

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
                {
                    break;
                }

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

    private static bool TryReadProcessorFrequency(
        out ulong highestCurrentSpeedHertz,
        out ulong baseSpeedHertz)
    {
        int processorCount = ReadActiveProcessorCount();
        if (processorCount <= 0)
        {
            highestCurrentSpeedHertz = 0;
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
                0,
                buffer,
                checked((uint)(processorCount * entrySize)));
            if (status != 0)
            {
                highestCurrentSpeedHertz = 0;
                baseSpeedHertz = 0;
                return false;
            }

            ulong highestCurrentMegahertz = 0;
            ulong baseMegahertzTotal = 0;
            for (int processorIndex = 0; processorIndex < processorCount; processorIndex++)
            {
                PROCESSOR_POWER_INFORMATION processorInformation =
                    Marshal.PtrToStructure<PROCESSOR_POWER_INFORMATION>(
                        IntPtr.Add(buffer, processorIndex * entrySize));
                highestCurrentMegahertz = Math.Max(
                    highestCurrentMegahertz,
                    processorInformation.CurrentMegahertz);
                baseMegahertzTotal = SaturatingAdd(
                    baseMegahertzTotal,
                    processorInformation.MaxMegahertz);
            }

            highestCurrentSpeedHertz = highestCurrentMegahertz * MegahertzToHertz;
            baseSpeedHertz = baseMegahertzTotal / (ulong)processorCount * MegahertzToHertz;
            return highestCurrentSpeedHertz > 0 || baseSpeedHertz > 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Discards the current PDH delta baseline after a sampling reset.</summary>
    internal void ResetFrequencyBaseline() => _processorPerformanceQueryPrimed = false;

    /// <summary>Applies a turbo-aware processor-performance percentage to the nominal base speed.</summary>
    internal static ulong CalculateCurrentSpeedHertz(
        ulong baseSpeedHertz,
        double processorPerformancePercent)
    {
        if (baseSpeedHertz == 0
            || !double.IsFinite(processorPerformancePercent)
            || processorPerformancePercent <= 0)
        {
            return 0;
        }

        double speedHertz = baseSpeedHertz * processorPerformancePercent / 100.0;
        return speedHertz >= ulong.MaxValue
            ? ulong.MaxValue
            : (ulong)Math.Round(speedHertz, MidpointRounding.AwayFromZero);
    }

    private bool TryReadHighestCurrentTurboSpeed(
        ulong baseSpeedHertz,
        out ulong highestCurrentSpeedHertz)
    {
        highestCurrentSpeedHertz = 0;
        if (_disposed
            || baseSpeedHertz == 0
            || _processorPerformanceQuery == IntPtr.Zero
            || _processorPerformanceCounter == IntPtr.Zero
            || PdhCollectQueryData(_processorPerformanceQuery) != PdhSuccess)
        {
            return false;
        }

        bool canReadFormattedValues = _processorPerformanceQueryPrimed;
        _processorPerformanceQueryPrimed = true;
        if (!canReadFormattedValues
            || !TryReadCounterArray(
                _processorPerformanceCounter,
                PdhFormatDouble | PdhFormatNoCap100,
                out PDH_FORMATTED_COUNTER_VALUE_ITEM* items,
                out uint itemCount))
        {
            return false;
        }

        for (uint itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            PDH_FORMATTED_COUNTER_VALUE_ITEM item = items[itemIndex];
            if (item.Name == IntPtr.Zero
                || item.Value.Status is not (PdhValidData or PdhNewData)
                || !IsLogicalProcessorInstanceName(ReadNullTerminatedSpan((char*)item.Name)))
            {
                continue;
            }

            ulong currentSpeedHertz = CalculateCurrentSpeedHertz(
                baseSpeedHertz,
                item.Value.DoubleValue);
            highestCurrentSpeedHertz = Math.Max(highestCurrentSpeedHertz, currentSpeedHertz);
        }

        return highestCurrentSpeedHertz > 0;
    }

    private bool TryReadCounterArray(
        IntPtr counter,
        uint format,
        out PDH_FORMATTED_COUNTER_VALUE_ITEM* items,
        out uint itemCount)
    {
        uint requiredSize = _counterBufferSize;
        uint status = PdhGetFormattedCounterArrayW(
            counter,
            format,
            ref requiredSize,
            out itemCount,
            _counterBuffer);
        if (status == PdhMoreData)
        {
            EnsureCounterBuffer(requiredSize);
            requiredSize = _counterBufferSize;
            status = PdhGetFormattedCounterArrayW(
                counter,
                format,
                ref requiredSize,
                out itemCount,
                _counterBuffer);
        }

        if (status != PdhSuccess)
        {
            items = null;
            itemCount = 0;
            return false;
        }

        items = (PDH_FORMATTED_COUNTER_VALUE_ITEM*)_counterBuffer;
        return true;
    }

    private void EnsureCounterBuffer(uint requiredSize)
    {
        if (requiredSize <= _counterBufferSize) return;

        uint capacity = Math.Max(4_096U, _counterBufferSize);
        while (capacity < requiredSize)
            capacity = checked(capacity * 2);
        _counterBuffer = _counterBuffer == IntPtr.Zero
            ? Marshal.AllocHGlobal(checked((int)capacity))
            : Marshal.ReAllocHGlobal(_counterBuffer, checked((IntPtr)capacity));
        _counterBufferSize = capacity;
    }

    private static ReadOnlySpan<char> ReadNullTerminatedSpan(char* value)
    {
        int length = 0;
        while (length < MaximumCounterInstanceNameLength && value[length] != '\0')
            length++;
        return new ReadOnlySpan<char>(value, length);
    }

    private static bool IsLogicalProcessorInstanceName(ReadOnlySpan<char> instanceName)
    {
        bool hasDigit = false;
        bool hasSeparator = false;
        bool hasDigitAfterSeparator = false;
        foreach (char character in instanceName)
        {
            if (char.IsAsciiDigit(character))
            {
                hasDigit = true;
                if (hasSeparator) hasDigitAfterSeparator = true;
                continue;
            }

            if (character != ',' || !hasDigit || hasSeparator) return false;
            hasSeparator = true;
        }

        return hasDigit && (!hasSeparator || hasDigitAfterSeparator);
    }

    private static bool TryReadPerformanceInformation(out SystemPerformanceInformation information)
    {
        PERFORMANCE_INFORMATION native = new()
        {
            Size = (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>()
        };
        if (!K32GetPerformanceInfo(ref native, native.Size) || native.PageSize == 0)
        {
            information = default;
            return false;
        }

        information = new SystemPerformanceInformation(
            native.ProcessCount,
            native.ThreadCount,
            native.HandleCount,
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
            : Math.Max(1, Environment.ProcessorCount);
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

        if (_counterBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_counterBuffer);
            _counterBuffer = IntPtr.Zero;
            _counterBufferSize = 0;
        }

        if (_processorPerformanceQuery == IntPtr.Zero) return;
        _ = PdhCloseQuery(_processorPerformanceQuery);
        _processorPerformanceQuery = IntPtr.Zero;
        _processorPerformanceCounter = IntPtr.Zero;
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

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(
        IntPtr counter,
        uint format,
        ref uint bufferSize,
        out uint itemCount,
        IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FORMATTED_COUNTER_VALUE_ITEM
    {
        public IntPtr Name;
        public PDH_FORMATTED_COUNTER_VALUE Value;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PDH_FORMATTED_COUNTER_VALUE
    {
        [FieldOffset(0)]
        public uint Status;

        [FieldOffset(8)]
        public double DoubleValue;
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
            new(0, 0, logicalProcessorCount, 0, 0, 0);
    }
}

internal readonly record struct SystemPerformanceMetadataSample(
    string ProcessorName,
    bool HasFrequencyData,
    ulong HighestCurrentSpeedHertz,
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
    ulong InstalledPhysicalMemoryBytes,
    TimeSpan Uptime);

internal readonly record struct SystemPerformanceInformation(
    uint ProcessCount,
    uint ThreadCount,
    uint HandleCount,
    ulong CommittedBytes,
    ulong CommitLimitBytes,
    ulong CachedBytes,
    ulong PagedPoolBytes,
    ulong NonPagedPoolBytes);
