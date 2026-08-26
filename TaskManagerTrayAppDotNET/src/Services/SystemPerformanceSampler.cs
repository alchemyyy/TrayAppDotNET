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
    private IntPtr _processorBuffer;
    private int _processorBufferSize;
    private int _previousProcessorCount;
    private double _lastCPUAveragePercent;
    private double _lastCPUHighestCorePercent;
    private double _lastMemoryPercent;
    private bool _hasPreviousProcessorTimes;
    private bool _disposed;

    /// <summary>Captures the current system utilization percentages.</summary>
    public SystemPerformanceSample Sample()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (TryReadMemoryPercent(out double memoryPercent))
            _lastMemoryPercent = memoryPercent;

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
            _lastCPUAveragePercent = 0;
            _lastCPUHighestCorePercent = 0;
            return new SystemPerformanceSample(0, 0, _lastMemoryPercent);
        }

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
            highestCorePercent = Math.Max(
                highestCorePercent,
                CalculateCPUUsagePercent(idleDelta, totalDelta));
            validProcessorCount++;
        }

        SaveCurrentProcessorTimes(processorCount);
        if (validProcessorCount > 0)
        {
            _lastCPUAveragePercent = CalculateCPUUsagePercent(
                aggregateIdleDelta,
                aggregateTotalDelta);
            _lastCPUHighestCorePercent = highestCorePercent;
        }

        return new SystemPerformanceSample(
            _lastCPUAveragePercent,
            _lastCPUHighestCorePercent,
            _lastMemoryPercent);
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

    private static bool TryReadMemoryPercent(out double memoryPercent)
    {
        MEMORYSTATUSEX memoryStatus = new()
        {
            Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
        };
        if (!GlobalMemoryStatusEx(ref memoryStatus) || memoryStatus.TotalPhysicalMemory == 0)
        {
            memoryPercent = 0;
            return false;
        }

        ulong availableMemory = Math.Min(
            memoryStatus.AvailablePhysicalMemory,
            memoryStatus.TotalPhysicalMemory);
        memoryPercent = (memoryStatus.TotalPhysicalMemory - availableMemory)
                        / (double)memoryStatus.TotalPhysicalMemory
                        * 100.0;
        memoryPercent = Math.Clamp(memoryPercent, 0, 100);
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
