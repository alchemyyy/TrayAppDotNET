using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Samples aggregate CPU, per-core CPU, and physical-memory utilization.</summary>
internal sealed class SystemPerformanceSampler : IDisposable
{
    private const int SystemProcessorPerformanceInformation = 8;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int StatusBufferOverflow = unchecked((int)0x80000005);
    private const int StatusBufferTooSmall = unchecked((int)0xC0000023);
    private const uint AllProcessorGroups = 0xFFFF;
    private const int ProcessorCapacitySlack = 4;

    private static readonly int NativeProcessorTimesSize = Marshal.SizeOf<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();

    private ProcessorTimes[] _currentProcessorTimes = [];
    private ProcessorTimes[] _previousProcessorTimes = [];
    private double[] _lastLogicalProcessorPercents = [];
    private IntPtr _processorBuffer;
    private int _processorBufferSize;
    private int _previousProcessorCount;
    private double _lastCPUAveragePercent;
    private double _lastCPUHighestCorePercent;
    private double _lastMemoryPercent;
    private ulong _lastTotalPhysicalMemoryBytes;
    private ulong _lastAvailablePhysicalMemoryBytes;
    private int _lastLogicalProcessorCount;
    private bool _hasPreviousProcessorTimes;
    private bool _lastProcessorSampleAvailable;
    private bool _lastMemorySampleAvailable;
    private bool _disposed;

    /// <summary>Captures the current system utilization percentages.</summary>
    public SystemPerformanceSample Sample()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _lastProcessorSampleAvailable = false;
        _lastMemorySampleAvailable = TryReadMemoryStatus(out SystemMemoryStatus memoryStatus);
        if (_lastMemorySampleAvailable)
        {
            _lastMemoryPercent = memoryStatus.UtilizationPercent;
            _lastTotalPhysicalMemoryBytes = memoryStatus.TotalPhysicalBytes;
            _lastAvailablePhysicalMemoryBytes = memoryStatus.AvailablePhysicalBytes;
        }

        if (!TryReadProcessorTimes(out int processorCount))
        {
            return new SystemPerformanceSample(
                _lastCPUAveragePercent,
                _lastCPUHighestCorePercent,
                _lastMemoryPercent);
        }

        if (!_hasPreviousProcessorTimes || processorCount != _previousProcessorCount)
        {
            SaveCurrentProcessorTimes(processorCount);
            EnsureLogicalProcessorCapacity(processorCount);
            Array.Clear(_lastLogicalProcessorPercents, 0, processorCount);
            _lastLogicalProcessorCount = processorCount;
            _lastCPUAveragePercent = 0;
            _lastCPUHighestCorePercent = 0;
            return new SystemPerformanceSample(0, 0, _lastMemoryPercent);
        }

        EnsureLogicalProcessorCapacity(processorCount);
        Array.Clear(_lastLogicalProcessorPercents, 0, processorCount);
        _lastLogicalProcessorCount = processorCount;
        double aggregateIdleDelta = 0;
        double aggregateTotalDelta = 0;
        double highestCorePercent = 0;
        int validProcessorCount = 0;
        for (int processorIndex = 0; processorIndex < processorCount; processorIndex++)
        {
            if (!TryCalculateTimeDeltas(
                    _previousProcessorTimes[processorIndex],
                    _currentProcessorTimes[processorIndex],
                    out double idleDelta,
                    out double totalDelta))
            {
                continue;
            }

            aggregateIdleDelta += idleDelta;
            aggregateTotalDelta += totalDelta;
            double processorPercent = CalculateCPUUsagePercent(idleDelta, totalDelta);
            _lastLogicalProcessorPercents[processorIndex] = processorPercent;
            highestCorePercent = Math.Max(highestCorePercent, processorPercent);
            validProcessorCount++;
        }

        SaveCurrentProcessorTimes(processorCount);
        if (validProcessorCount > 0)
        {
            _lastCPUAveragePercent = CalculateCPUUsagePercent(
                aggregateIdleDelta,
                aggregateTotalDelta);
            _lastCPUHighestCorePercent = highestCorePercent;
            _lastProcessorSampleAvailable = true;
        }

        return new SystemPerformanceSample(
            _lastCPUAveragePercent,
            _lastCPUHighestCorePercent,
            _lastMemoryPercent);
    }

    /// <summary>Gets whether the last call produced a fresh processor delta.</summary>
    internal bool LastProcessorSampleAvailable => _lastProcessorSampleAvailable;

    /// <summary>Gets whether the last call produced fresh physical-memory counters.</summary>
    internal bool LastMemorySampleAvailable => _lastMemorySampleAvailable;

    /// <summary>Gets the number of logical processors represented by the last processor read.</summary>
    internal int LastLogicalProcessorCount => _lastLogicalProcessorCount;

    /// <summary>Copies the last per-logical-processor percentages into caller-owned storage.</summary>
    internal int CopyLastLogicalProcessorPercents(double[] destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(destination);

        int count = Math.Min(destination.Length, _lastLogicalProcessorCount);
        Array.Copy(_lastLogicalProcessorPercents, destination, count);
        return count;
    }

    /// <summary>Gets the latest direct physical-memory values.</summary>
    internal SystemMemoryStatus GetLastMemoryStatus() => new(
        _lastTotalPhysicalMemoryBytes,
        _lastAvailablePhysicalMemoryBytes,
        _lastMemoryPercent);

    /// <summary>Discards processor deltas so the next capture only establishes a fresh baseline.</summary>
    internal void ResetProcessorBaseline()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _hasPreviousProcessorTimes = false;
        _previousProcessorCount = 0;
        _lastProcessorSampleAvailable = false;
        _lastCPUAveragePercent = 0;
        _lastCPUHighestCorePercent = 0;
        Array.Clear(_lastLogicalProcessorPercents);
    }

    /// <summary>Calculates busy time from deltas where kernel time includes idle time.</summary>
    internal static double CalculateCPUUsagePercent(double idleDelta, double totalDelta)
    {
        if (!double.IsFinite(idleDelta)
            || !double.IsFinite(totalDelta)
            || totalDelta <= 0)
        {
            return 0;
        }

        double boundedIdleDelta = Math.Clamp(idleDelta, 0, totalDelta);
        return Math.Clamp((1.0 - boundedIdleDelta / totalDelta) * 100.0, 0, 100);
    }

    private bool TryReadProcessorTimes(out int processorCount)
    {
        processorCount = 0;
        uint activeProcessorCount = GetActiveProcessorCount(AllProcessorGroups);
        int requestedProcessorCount = activeProcessorCount is > 0 and <= int.MaxValue
            ? (int)activeProcessorCount
            : Math.Max(1, Environment.ProcessorCount);
        int requestedBufferSize = checked(
            (requestedProcessorCount + ProcessorCapacitySlack) * NativeProcessorTimesSize);
        EnsureProcessorBuffer(requestedBufferSize);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            int status = NtQuerySystemInformation(
                SystemProcessorPerformanceInformation,
                _processorBuffer,
                _processorBufferSize,
                out int returnLength);
            if (status >= 0)
            {
                int returnedProcessorCount = returnLength > 0
                    ? returnLength / NativeProcessorTimesSize
                    : requestedProcessorCount;
                processorCount = Math.Min(returnedProcessorCount, _processorBufferSize / NativeProcessorTimesSize);
                if (processorCount <= 0) return false;

                EnsureProcessorTimesCapacity(ref _currentProcessorTimes, processorCount);
                for (int processorIndex = 0; processorIndex < processorCount; processorIndex++)
                {
                    IntPtr processorAddress = IntPtr.Add(
                        _processorBuffer,
                        processorIndex * NativeProcessorTimesSize);
                    SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION native =
                        Marshal.PtrToStructure<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>(processorAddress);
                    _currentProcessorTimes[processorIndex] = new ProcessorTimes(
                        native.IdleTime,
                        native.KernelTime,
                        native.UserTime);
                }

                return true;
            }

            if (!IsBufferSizeStatus(status)
                || returnLength <= _processorBufferSize)
            {
                return false;
            }

            EnsureProcessorBuffer(checked(returnLength + ProcessorCapacitySlack * NativeProcessorTimesSize));
        }

        return false;
    }

    private void SaveCurrentProcessorTimes(int processorCount)
    {
        EnsureProcessorTimesCapacity(ref _previousProcessorTimes, processorCount);
        Array.Copy(_currentProcessorTimes, _previousProcessorTimes, processorCount);
        _previousProcessorCount = processorCount;
        _hasPreviousProcessorTimes = true;
    }

    private void EnsureProcessorBuffer(int requiredSize)
    {
        if (_processorBuffer != IntPtr.Zero && _processorBufferSize >= requiredSize) return;

        if (_processorBuffer != IntPtr.Zero)
            Marshal.FreeHGlobal(_processorBuffer);

        _processorBuffer = Marshal.AllocHGlobal(requiredSize);
        _processorBufferSize = requiredSize;
    }

    private static void EnsureProcessorTimesCapacity(ref ProcessorTimes[] values, int count)
    {
        if (values.Length >= count) return;
        values = new ProcessorTimes[count];
    }

    private void EnsureLogicalProcessorCapacity(int count)
    {
        if (_lastLogicalProcessorPercents.Length >= count) return;
        _lastLogicalProcessorPercents = new double[count];
    }

    private static bool TryCalculateTimeDeltas(
        ProcessorTimes previous,
        ProcessorTimes current,
        out double idleDelta,
        out double totalDelta)
    {
        idleDelta = 0;
        totalDelta = 0;
        if (current.IdleTime < previous.IdleTime
            || current.KernelTime < previous.KernelTime
            || current.UserTime < previous.UserTime)
        {
            return false;
        }

        idleDelta = current.IdleTime - previous.IdleTime;
        double kernelDelta = current.KernelTime - previous.KernelTime;
        double userDelta = current.UserTime - previous.UserTime;
        totalDelta = kernelDelta + userDelta;
        return totalDelta > 0;
    }

    private static bool TryReadMemoryStatus(out SystemMemoryStatus memoryStatus)
    {
        MEMORYSTATUSEX nativeMemoryStatus = new()
        {
            Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
        };
        if (!GlobalMemoryStatusEx(ref nativeMemoryStatus) || nativeMemoryStatus.TotalPhysicalMemory == 0)
        {
            memoryStatus = default;
            return false;
        }

        ulong availableMemory = Math.Min(
            nativeMemoryStatus.AvailablePhysicalMemory,
            nativeMemoryStatus.TotalPhysicalMemory);
        double memoryPercent = (nativeMemoryStatus.TotalPhysicalMemory - availableMemory)
                               / (double)nativeMemoryStatus.TotalPhysicalMemory
                               * 100.0;
        memoryStatus = new SystemMemoryStatus(
            nativeMemoryStatus.TotalPhysicalMemory,
            availableMemory,
            Math.Clamp(memoryPercent, 0, 100));
        return true;
    }

    private static bool IsBufferSizeStatus(int status) =>
        status is StatusInfoLengthMismatch or StatusBufferOverflow or StatusBufferTooSmall;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_processorBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_processorBuffer);
            _processorBuffer = IntPtr.Zero;
            _processorBufferSize = 0;
        }

        _currentProcessorTimes = [];
        _previousProcessorTimes = [];
        _lastLogicalProcessorPercents = [];
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern uint GetActiveProcessorCount(uint groupNumber);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
    {
        public long IdleTime;
        public long KernelTime;
        public long UserTime;
        public long DPCTime;
        public long InterruptTime;
        public uint InterruptCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysicalMemory;
        public ulong AvailablePhysicalMemory;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    private readonly record struct ProcessorTimes(
        long IdleTime,
        long KernelTime,
        long UserTime);
}

/// <summary>Direct physical-memory counters retained with a system sample.</summary>
internal readonly record struct SystemMemoryStatus(
    ulong TotalPhysicalBytes,
    ulong AvailablePhysicalBytes,
    double UtilizationPercent);
