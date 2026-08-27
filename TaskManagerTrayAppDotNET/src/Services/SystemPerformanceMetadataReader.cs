using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Reads static CPU topology and low-frequency system performance metadata directly from Windows.</summary>
internal sealed class SystemPerformanceMetadataReader
{
    private const string ProcessorRegistryPath = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string ProcessorNameValue = "ProcessorNameString";
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

    private readonly string _processorName = ReadProcessorName();
    private readonly ProcessorTopology _topology = ReadProcessorTopology();
    private readonly ulong _installedPhysicalMemoryBytes = ReadInstalledPhysicalMemoryBytes();

    /// <summary>Captures frequency, counts, commit data, and uptime for the current sample.</summary>
    public SystemPerformanceMetadataSample Sample()
    {
        bool hasFrequencyData = TryReadProcessorFrequency(
            out ulong currentSpeedHertz,
            out ulong maximumSpeedHertz);
        bool hasPerformanceInformation = TryReadPerformanceInformation(
            out SystemPerformanceInformation performanceInformation);
        return new SystemPerformanceMetadataSample(
            _processorName,
            hasFrequencyData,
            currentSpeedHertz,
            maximumSpeedHertz,
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
        out ulong currentSpeedHertz,
        out ulong maximumSpeedHertz)
    {
        int processorCount = ReadActiveProcessorCount();
        if (processorCount <= 0)
        {
            currentSpeedHertz = 0;
            maximumSpeedHertz = 0;
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
                currentSpeedHertz = 0;
                maximumSpeedHertz = 0;
                return false;
            }

            ulong currentMegahertzTotal = 0;
            ulong maximumMegahertzTotal = 0;
            for (int processorIndex = 0; processorIndex < processorCount; processorIndex++)
            {
                PROCESSOR_POWER_INFORMATION processorInformation =
                    Marshal.PtrToStructure<PROCESSOR_POWER_INFORMATION>(
                        IntPtr.Add(buffer, processorIndex * entrySize));
                currentMegahertzTotal = SaturatingAdd(
                    currentMegahertzTotal,
                    processorInformation.CurrentMegahertz);
                maximumMegahertzTotal = SaturatingAdd(
                    maximumMegahertzTotal,
                    processorInformation.MaxMegahertz);
            }

            currentSpeedHertz = currentMegahertzTotal / (ulong)processorCount * MegahertzToHertz;
            maximumSpeedHertz = maximumMegahertzTotal / (ulong)processorCount * MegahertzToHertz;
            return currentSpeedHertz > 0 || maximumSpeedHertz > 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
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
    ulong CurrentSpeedHertz,
    ulong MaximumSpeedHertz,
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
