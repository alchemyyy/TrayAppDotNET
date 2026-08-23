using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Reads all core process counters with one SystemProcessInformation query.</summary>
internal sealed class SystemProcessSnapshot : IDisposable
{
    private const int SystemProcessInformation = 5;
    private const int SystemFullProcessInformation = 148;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int StatusBufferOverflow = unchecked((int)0x80000005);
    private const int StatusBufferTooSmall = unchecked((int)0xC0000023);
    private const int InitialBufferSize = 1024 * 1024;
    private const int BufferGrowthSlack = 64 * 1024;
    private const int MaximumBufferSize = 64 * 1024 * 1024;
    private const uint WaitingThreadState = 5;
    private const uint SuspendedWaitReason = 5;

    private static readonly int ProcessHeaderSize = Marshal.SizeOf<SYSTEM_PROCESS_INFORMATION>();
    private static readonly int ThreadEntrySize = Marshal.SizeOf<SYSTEM_THREAD_INFORMATION>();
    private static readonly int ExtendedThreadEntrySize = Marshal.SizeOf<SYSTEM_EXTENDED_THREAD_INFORMATION>();
    private static readonly int ProcessExtensionSize = Marshal.SizeOf<SYSTEM_PROCESS_INFORMATION_EXTENSION>();

    private IntPtr _buffer;
    private int _bufferSize;
    private int _activeThreadEntrySize = ThreadEntrySize;
    private bool _hasJobObjectIDs;
    private bool _lastRequestedJobObjectIDs;
    private bool _disposed;

    public bool HasJobObjectIDs => _hasJobObjectIDs;

    public bool TryCapture(Dictionary<int, SystemProcessData> destination, bool includeJobObjectIDs = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(destination);

        if (_lastRequestedJobObjectIDs != includeJobObjectIDs)
        {
            ReleaseBuffer();
            _lastRequestedJobObjectIDs = includeJobObjectIDs;
        }

        if (includeJobObjectIDs
            && TryCaptureCore(destination, SystemFullProcessInformation, true))
        {
            return true;
        }

        return TryCaptureCore(destination, SystemProcessInformation, false);
    }

    private bool TryCaptureCore(
        Dictionary<int, SystemProcessData> destination,
        int informationClass,
        bool hasFullProcessInformation)
    {
        destination.Clear();

        EnsureBuffer(InitialBufferSize);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            int status = NtQuerySystemInformation(
                informationClass,
                _buffer,
                _bufferSize,
                out int requiredLength);
            if (status >= 0)
            {
                _hasJobObjectIDs = hasFullProcessInformation;
                _activeThreadEntrySize = hasFullProcessInformation
                    ? ExtendedThreadEntrySize
                    : ThreadEntrySize;
                return ParseSnapshot(
                    destination,
                    requiredLength > 0 ? requiredLength : _bufferSize,
                    hasFullProcessInformation);
            }
            if (!IsBufferSizeStatus(status) || _bufferSize >= MaximumBufferSize)
                return false;

            int requestedSize = requiredLength > _bufferSize
                ? checked(requiredLength + BufferGrowthSlack)
                : checked(_bufferSize * 2);
            EnsureBuffer(Math.Min(MaximumBufferSize, requestedSize));
        }

        return false;
    }

    private bool ParseSnapshot(
        Dictionary<int, SystemProcessData> destination,
        int validLength,
        bool hasFullProcessInformation)
    {
        int entryOffset = 0;
        while (entryOffset >= 0 && entryOffset + ProcessHeaderSize <= validLength)
        {
            IntPtr entryAddress = IntPtr.Add(_buffer, entryOffset);
            SYSTEM_PROCESS_INFORMATION process =
                Marshal.PtrToStructure<SYSTEM_PROCESS_INFORMATION>(entryAddress);
            long processIDValue = process.UniqueProcessID.ToInt64();
            if (processIDValue is >= 0 and <= int.MaxValue)
            {
                int processID = (int)processIDValue;
                int entryLength = process.NextEntryOffset == 0
                    ? validLength - entryOffset
                    : (int)Math.Min(process.NextEntryOffset, int.MaxValue);
                int threadEntrySize = hasFullProcessInformation
                    ? ExtendedThreadEntrySize
                    : ThreadEntrySize;
                int availableThreadCount = Math.Max(0, (entryLength - ProcessHeaderSize) / threadEntrySize);
                int threadCount = (int)Math.Min(process.NumberOfThreads, (uint)availableThreadCount);
                long jobObjectID = hasFullProcessInformation
                    ? ReadJobObjectID(entryAddress, entryLength, threadCount)
                    : -1;
                destination[processID] = new SystemProcessData(
                    process.CreateTime,
                    SaturatingAdd(process.KernelTime, process.UserTime),
                    process.CycleTime,
                    ToNonNegativeLong(process.WorkingSetSize),
                    ToNonNegativeLong(process.PeakWorkingSetSize),
                    Math.Max(0, process.WorkingSetPrivateSize),
                    ToNonNegativeLong(process.PrivatePageCount),
                    ToNonNegativeLong(process.QuotaPagedPoolUsage),
                    ToNonNegativeLong(process.QuotaNonPagedPoolUsage),
                    process.PageFaultCount,
                    process.BasePriority,
                    ToInt32(process.HandleCount),
                    ToInt32(process.SessionID),
                    threadCount,
                    entryOffset,
                    ToNonNegativeUInt64(process.ReadOperationCount),
                    ToNonNegativeUInt64(process.WriteOperationCount),
                    ToNonNegativeUInt64(process.OtherOperationCount),
                    ToNonNegativeUInt64(process.ReadTransferCount),
                    ToNonNegativeUInt64(process.WriteTransferCount),
                    ToNonNegativeUInt64(process.OtherTransferCount),
                    jobObjectID);
            }

            if (process.NextEntryOffset == 0) return true;
            if (process.NextEntryOffset > int.MaxValue
                || process.NextEntryOffset < ProcessHeaderSize
                || entryOffset > validLength - (int)process.NextEntryOffset)
            {
                destination.Clear();
                return false;
            }

            entryOffset += (int)process.NextEntryOffset;
        }

        destination.Clear();
        return false;
    }

    private static long ReadJobObjectID(IntPtr entryAddress, int entryLength, int threadCount)
    {
        long extensionOffset = ProcessHeaderSize + (long)threadCount * ExtendedThreadEntrySize;
        if (extensionOffset < 0 || extensionOffset > entryLength - ProcessExtensionSize) return -1;

        IntPtr extensionAddress = IntPtr.Add(entryAddress, (int)extensionOffset);
        SYSTEM_PROCESS_INFORMATION_EXTENSION extension =
            Marshal.PtrToStructure<SYSTEM_PROCESS_INFORMATION_EXTENSION>(extensionAddress);
        return extension.JobObjectID;
    }

    public ProcessExecutionState ReadExecutionState(SystemProcessData process)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (process.NativeEntryOffset < 0 || process.NativeEntryOffset > _bufferSize - ProcessHeaderSize)
            return ProcessExecutionState.Running;

        IntPtr processAddress = IntPtr.Add(_buffer, process.NativeEntryOffset);
        return AreAllThreadsSuspended(processAddress, process.ThreadCount)
            ? ProcessExecutionState.Suspended
            : ProcessExecutionState.Running;
    }

    private bool AreAllThreadsSuspended(IntPtr processAddress, int threadCount)
    {
        if (threadCount <= 0) return false;

        IntPtr threadAddress = IntPtr.Add(processAddress, ProcessHeaderSize);
        for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
        {
            SYSTEM_THREAD_INFORMATION thread =
                Marshal.PtrToStructure<SYSTEM_THREAD_INFORMATION>(threadAddress);
            if (thread.ThreadState != WaitingThreadState || thread.WaitReason != SuspendedWaitReason)
                return false;
            threadAddress = IntPtr.Add(threadAddress, _activeThreadEntrySize);
        }

        return true;
    }

    /// <summary>Materializes a process name only when a new process identity needs static data.</summary>
    public string ReadImageName(SystemProcessData process)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (process.NativeEntryOffset < 0 || process.NativeEntryOffset > _bufferSize - ProcessHeaderSize)
            return string.Empty;

        IntPtr processAddress = IntPtr.Add(_buffer, process.NativeEntryOffset);
        SYSTEM_PROCESS_INFORMATION nativeProcess =
            Marshal.PtrToStructure<SYSTEM_PROCESS_INFORMATION>(processAddress);
        if (nativeProcess.ImageName.Buffer == IntPtr.Zero || nativeProcess.ImageName.Length == 0)
            return string.Empty;

        return Marshal.PtrToStringUni(
                   nativeProcess.ImageName.Buffer,
                   nativeProcess.ImageName.Length / sizeof(char))
               ?? string.Empty;
    }

    private void EnsureBuffer(int requiredSize)
    {
        if (_buffer != IntPtr.Zero && _bufferSize >= requiredSize) return;

        if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
        _buffer = Marshal.AllocHGlobal(requiredSize);
        _bufferSize = requiredSize;
    }

    private void ReleaseBuffer()
    {
        if (_buffer == IntPtr.Zero) return;

        Marshal.FreeHGlobal(_buffer);
        _buffer = IntPtr.Zero;
        _bufferSize = 0;
    }

    private static long ToNonNegativeLong(nuint value) =>
        value > long.MaxValue ? long.MaxValue : (long)value;

    private static ulong ToNonNegativeUInt64(long value) => value <= 0 ? 0 : (ulong)value;

    private static int ToInt32(uint value) => value > int.MaxValue ? int.MaxValue : (int)value;

    private static bool IsBufferSizeStatus(int status) =>
        status is StatusInfoLengthMismatch or StatusBufferOverflow or StatusBufferTooSmall;

    private static long SaturatingAdd(long left, long right)
    {
        if (left <= 0) return Math.Max(0, right);
        if (right <= 0) return left;
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        ReleaseBuffer();
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_PROCESS_INFORMATION
    {
        public uint NextEntryOffset;
        public uint NumberOfThreads;
        public long WorkingSetPrivateSize;
        public uint HardFaultCount;
        public uint NumberOfThreadsHighWatermark;
        public ulong CycleTime;
        public long CreateTime;
        public long UserTime;
        public long KernelTime;
        public UNICODE_STRING ImageName;
        public int BasePriority;
        public IntPtr UniqueProcessID;
        public IntPtr InheritedFromUniqueProcessID;
        public uint HandleCount;
        public uint SessionID;
        public nuint UniqueProcessKey;
        public nuint PeakVirtualSize;
        public nuint VirtualSize;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivatePageCount;
        public long ReadOperationCount;
        public long WriteOperationCount;
        public long OtherOperationCount;
        public long ReadTransferCount;
        public long WriteTransferCount;
        public long OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CLIENT_ID
    {
        public IntPtr UniqueProcess;
        public IntPtr UniqueThread;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_THREAD_INFORMATION
    {
        public long KernelTime;
        public long UserTime;
        public long CreateTime;
        public uint WaitTime;
        public IntPtr StartAddress;
        public CLIENT_ID ClientID;
        public int Priority;
        public int BasePriority;
        public uint ContextSwitches;
        public uint ThreadState;
        public uint WaitReason;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_EXTENDED_THREAD_INFORMATION
    {
        public SYSTEM_THREAD_INFORMATION ThreadInformation;
        public nuint StackBase;
        public nuint StackLimit;
        public IntPtr Win32StartAddress;
        public IntPtr TEBBase;
        public nuint Reserved2;
        public nuint Reserved3;
        public nuint Reserved4;
    }

    // SystemFullProcessInformation uses this private x64 layout after the extended thread array
    [StructLayout(LayoutKind.Explicit, Size = 368)]
    private struct SYSTEM_PROCESS_INFORMATION_EXTENSION
    {
        [FieldOffset(352)]
        public uint JobObjectID;
    }
}

internal readonly record struct SystemProcessData(
    long CreationTimeTicks,
    long TotalProcessorTicks,
    ulong CycleCount,
    long WorkingSetBytes,
    long PeakWorkingSetBytes,
    long PrivateWorkingSetBytes,
    long CommitSizeBytes,
    long PagedPoolBytes,
    long NonPagedPoolBytes,
    long PageFaultCount,
    int BasePriority,
    int HandleCount,
    int SessionID,
    int ThreadCount,
    int NativeEntryOffset,
    ulong IOReadOperations,
    ulong IOWriteOperations,
    ulong IOOtherOperations,
    ulong IOReadBytes,
    ulong IOWriteBytes,
    ulong IOOtherBytes,
    long JobObjectID);
