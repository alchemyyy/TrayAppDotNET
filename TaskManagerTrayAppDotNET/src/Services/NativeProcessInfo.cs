using System.Runtime.InteropServices;
using System.Text;
using TaskManagerTrayAppDotNET.Interop;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Low-overhead native process queries used by the Details sampler.</summary>
internal static unsafe class NativeProcessInfo
{
    public const string Unavailable = "Unavailable";

    private const int ErrorInsufficientBuffer = 122;
    private const int ProcessBasicInformation = 0;
    private const int ProcessBreakOnTermination = 29;
    private const int ProcessProtectionInformation = 61;
    private const int ProcessIOPriority = 33;
    private const uint TokenQuery = 0x0008;
    private const int TokenUser = 1;
    private const int TokenElevation = 20;
    private const int TokenVirtualizationAllowed = 23;
    private const int TokenVirtualizationEnabled = 24;
    private const int TokenIsAppContainer = 29;
    private const uint UserObjectCount = 1;
    private const uint GDIObjectCount = 0;
    private const int ProcessPowerThrottling = 4;
    private const uint ProcessPowerThrottlingCurrentVersion = 1;
    private const uint ProcessPowerThrottlingExecutionSpeed = 0x1;
    private const uint IdlePriorityClass = 0x40;
    private const int ProcessControlFlowGuardPolicy = 7;
    private const int ProcessUserShadowStackPolicy = 15;
    private const uint CFGEnabled = 0x1;
    // PROCESS_MITIGATION_CONTROL_FLOW_GUARD_POLICY: bit 3 is EnableXfg; bit 4 (0x10) is EnableXfgAuditMode
    internal const uint XFGEnabled = 0x8;
    private const uint UserShadowStackEnabled = 0x1;
    private const int ProcessorPowerInformation = 11;
    private const double MegahertzToHertz = 1_000_000;
    private const ushort ImageFileMachineUnknown = 0x0000;
    private const ushort ImageFileMachineI386 = 0x014c;
    private const ushort ImageFileMachineAMD64 = 0x8664;
    private const ushort ImageFileMachineARM64 = 0xAA64;
    private const int PackageFullNameBufferLength = 128;

    /// <summary>Reads installed physical memory once for percentage-based column formatting.</summary>
    public static long ReadTotalPhysicalMemoryBytes()
    {
        MEMORYSTATUSEX memoryStatus = new() { Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref memoryStatus) && memoryStatus.TotalPhysicalMemory <= long.MaxValue
            ? (long)memoryStatus.TotalPhysicalMemory
            : 0;
    }

    /// <summary>Reads an exact kernel creation time without substituting a snapshot timestamp.</summary>
    public static bool TryReadCreationTimeTicks(IntPtr processHandle, out long creationTimeTicks)
    {
        creationTimeTicks = 0;
        if (!Kernel32.GetProcessTimes(
                processHandle,
                out Kernel32.FILETIME creationTime,
                out _,
                out _,
                out _))
            return false;

        creationTimeTicks = unchecked(
            (long)(((ulong)creationTime.HighDateTime << 32) | creationTime.LowDateTime));
        return creationTimeTicks > 0;
    }

    /// <summary>Reads the kernel creator PID when the bulk system snapshot is unavailable.</summary>
    public static int ReadParentProcessID(IntPtr processHandle)
    {
        PROCESS_BASIC_INFORMATION information;
        int status = NtDll.NtQueryInformationProcess(
            processHandle,
            ProcessBasicInformation,
            &information,
            sizeof(PROCESS_BASIC_INFORMATION),
            returnLength: null);
        if (status < 0) return -1;

        long parentProcessID = information.InheritedFromUniqueProcessID.ToInt64();
        return parentProcessID is >= 0 and <= int.MaxValue ? (int)parentProcessID : -1;
    }

    public static bool TryReadMemoryCounters(IntPtr processHandle, out ProcessMemoryCounters counters)
    {
        PROCESS_MEMORY_COUNTERS_EX2 native = new() { Size = (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS_EX2>() };
        if (!GetProcessMemoryInfo(processHandle, ref native, native.Size))
        {
            counters = default;
            return false;
        }

        long workingSet = ToLong(native.WorkingSetSize);
        long privateWorkingSet = ToLong(native.PrivateWorkingSetSize);
        counters = new ProcessMemoryCounters(
            workingSet,
            ToLong(native.PeakWorkingSetSize),
            privateWorkingSet,
            Math.Max(val1: 0, workingSet - privateWorkingSet),
            ToLong(native.PrivateUsage),
            ToLong(native.QuotaPagedPoolUsage),
            ToLong(native.QuotaNonPagedPoolUsage),
            native.PageFaultCount);
        return true;
    }

    public static ulong ReadCycleCount(IntPtr processHandle) =>
        QueryProcessCycleTime(processHandle, out ulong cycles) ? cycles : 0;

    public static ProcessIOCounters ReadIOCounters(IntPtr processHandle)
    {
        if (!GetProcessIoCounters(processHandle, out IO_COUNTERS counters)) return default;

        return new ProcessIOCounters(
            counters.ReadOperationCount,
            counters.WriteOperationCount,
            counters.OtherOperationCount,
            counters.ReadTransferCount,
            counters.WriteTransferCount,
            counters.OtherTransferCount);
    }

    public static int ReadUserObjectCount(IntPtr processHandle) =>
        ToSignedCount(GetGuiResources(processHandle, UserObjectCount));

    public static int ReadGDIObjectCount(IntPtr processHandle) =>
        ToSignedCount(GetGuiResources(processHandle, GDIObjectCount));

    public static string ReadUserName(IntPtr processHandle)
    {
        if (!OpenProcessToken(processHandle, TokenQuery, out IntPtr tokenHandle)) return Unavailable;

        try
        {
            _ = GetTokenInformation(tokenHandle, TokenUser, IntPtr.Zero, tokenInformationLength: 0,
                out int requiredLength);
            if (requiredLength <= 0) return Unavailable;

            IntPtr tokenBuffer = Marshal.AllocHGlobal(requiredLength);
            try
            {
                if (!GetTokenInformation(tokenHandle, TokenUser, tokenBuffer, requiredLength, out requiredLength))
                    return Unavailable;

                TOKEN_USER tokenUser = Marshal.PtrToStructure<TOKEN_USER>(tokenBuffer);
                return LookupAccountName(tokenUser.User.Sid);
            }
            finally
            {
                Marshal.FreeHGlobal(tokenBuffer);
            }
        }
        finally
        {
            Kernel32.CloseHandle(tokenHandle);
        }
    }

    /// <summary>Reads a non-localized SID string for semantic security scoping.</summary>
    public static string? ReadUserSID(IntPtr processHandle)
    {
        if (!OpenProcessToken(processHandle, TokenQuery, out IntPtr tokenHandle)) return null;

        try
        {
            _ = GetTokenInformation(
                tokenHandle,
                TokenUser,
                IntPtr.Zero,
                tokenInformationLength: 0,
                out int requiredLength);
            if (requiredLength <= 0) return null;

            IntPtr tokenBuffer = Marshal.AllocHGlobal(requiredLength);
            try
            {
                if (!GetTokenInformation(
                        tokenHandle,
                        TokenUser,
                        tokenBuffer,
                        requiredLength,
                        out requiredLength))
                    return null;

                TOKEN_USER tokenUser = Marshal.PtrToStructure<TOKEN_USER>(tokenBuffer);
                if (!ConvertSidToStringSidW(tokenUser.User.Sid, out IntPtr sidString)) return null;
                try
                {
                    return Marshal.PtrToStringUni(sidString);
                }
                finally
                {
                    _ = LocalFree(sidString);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tokenBuffer);
            }
        }
        finally
        {
            Kernel32.CloseHandle(tokenHandle);
        }
    }

    public static ProcessDisplayCode ReadElevation(IntPtr processHandle) =>
        TryReadTokenInteger(processHandle, TokenElevation, out int elevated)
            ? elevated != 0 ? ProcessDisplayCode.Yes : ProcessDisplayCode.No
            : ProcessDisplayCode.Unavailable;

    public static ProcessDisplayCode ReadUACVirtualization(IntPtr processHandle)
    {
        if (!TryReadTokenInteger(processHandle, TokenVirtualizationAllowed, out int allowed))
            return ProcessDisplayCode.Unavailable;
        if (allowed == 0) return ProcessDisplayCode.NotAllowed;

        return TryReadTokenInteger(processHandle, TokenVirtualizationEnabled, out int enabled)
            ? enabled != 0 ? ProcessDisplayCode.Enabled : ProcessDisplayCode.Disabled
            : ProcessDisplayCode.Unavailable;
    }

    public static ProcessDisplayCode ReadIsolation(IntPtr processHandle, bool isPackaged)
    {
        if (!TryReadTokenInteger(processHandle, TokenIsAppContainer, out int isAppContainer))
            return isPackaged ? ProcessDisplayCode.UWP : ProcessDisplayCode.Unavailable;
        if (isAppContainer != 0) return ProcessDisplayCode.AppContainer;
        return isPackaged ? ProcessDisplayCode.UWP : ProcessDisplayCode.NoIsolation;
    }

    /// <summary>Returns null for a failed query and empty for a known unpackaged process.</summary>
    public static string? ReadPackageFullNameForGrouping(IntPtr processHandle)
    {
        const int AppModelErrorNoPackage = 15700;
        // PACKAGE_FULL_NAME_MAX_LENGTH is 127 characters plus the terminator, so a stack buffer covers every package
        char* stackBuffer = stackalloc char[PackageFullNameBufferLength];
        uint length = PackageFullNameBufferLength;
        int result = GetPackageFullName(processHandle, ref length, stackBuffer);
        if (result == ErrorInsufficientBuffer && length is > PackageFullNameBufferLength and <= ushort.MaxValue)
        {
            char[] heapBuffer = new char[length];
            fixed (char* heapPointer = heapBuffer)
            {
                result = GetPackageFullName(processHandle, ref length, heapPointer);
                if (result == 0) return new string(heapPointer);
            }
        }

        return result switch
        {
            0 => new string(stackBuffer),
            AppModelErrorNoPackage => string.Empty,
            _ => null
        };
    }

    /// <summary>
    /// Reads ProcessBreakOnTermination, the flag RtlSetProcessIsCritical sets. Task Manager's IsCriticalProcess
    /// reads the same flag; false when the query fails.
    /// </summary>
    public static bool ReadIsCritical(IntPtr processHandle)
    {
        int breakOnTermination = 0;
        int status = NtDll.NtQueryInformationProcess(
            processHandle,
            ProcessBreakOnTermination,
            &breakOnTermination,
            sizeof(int),
            returnLength: null);
        return status >= 0 && breakOnTermination != 0;
    }

    /// <summary>Reads whether the process runs as a protected or protected light process; false when the query fails.</summary>
    public static bool ReadIsProtected(IntPtr processHandle)
    {
        byte protection = 0;
        int status = NtDll.NtQueryInformationProcess(
            processHandle,
            ProcessProtectionInformation,
            &protection,
            sizeof(byte),
            returnLength: null);
        return status >= 0 && protection != 0;
    }

    public static ProcessDisplayCode ReadArchitecture(IntPtr processHandle)
    {
        if (!IsWow64Process2(processHandle, out ushort processMachine, out ushort nativeMachine))
            return ProcessDisplayCode.Unavailable;

        ushort machine = processMachine == ImageFileMachineUnknown ? nativeMachine : processMachine;
        return machine switch
        {
            ImageFileMachineI386 => ProcessDisplayCode.X86,
            ImageFileMachineAMD64 => ProcessDisplayCode.X64,
            ImageFileMachineARM64 => ProcessDisplayCode.ARM64,
            _ => ProcessDisplayCode.Unavailable
        };
    }

    public static ProcessDisplayCode GetPlatform(ProcessDisplayCode architecture) => architecture switch
    {
        ProcessDisplayCode.X86 => ProcessDisplayCode.Platform32Bit,
        ProcessDisplayCode.X64 or ProcessDisplayCode.ARM64 => ProcessDisplayCode.Platform64Bit,
        _ => ProcessDisplayCode.Unavailable
    };

    /// <summary>
    /// Reads the DEP state. GetProcessDEPPolicy only works for 32-bit processes (64-bit ones fail with
    /// ERROR_NOT_SUPPORTED), and DEP is always on and permanent for 64-bit processes, so only x86 targets are queried.
    /// </summary>
    public static ProcessDisplayCode ReadDataExecutionPrevention(IntPtr processHandle, ProcessDisplayCode architecture)
    {
        if (architecture != ProcessDisplayCode.X86)
            return DecodeDataExecutionPrevention(architecture, policyRead: false, flags: 0);

        bool policyRead = GetProcessDEPPolicy(processHandle, out uint flags, out _);
        return DecodeDataExecutionPrevention(architecture, policyRead, flags);
    }

    /// <summary>Maps the architecture and, for x86, the DEP policy flags to the column value.</summary>
    internal static ProcessDisplayCode DecodeDataExecutionPrevention(
        ProcessDisplayCode architecture,
        bool policyRead,
        uint flags) => architecture switch
    {
        ProcessDisplayCode.X64 or ProcessDisplayCode.ARM64 => ProcessDisplayCode.Enabled,
        ProcessDisplayCode.X86 when policyRead => (flags & 0x1) != 0 ? ProcessDisplayCode.Enabled : ProcessDisplayCode.Disabled,
        _ => ProcessDisplayCode.Unavailable
    };

    /// <summary>Maps CFG policy flags to the Extended Control Flow Guard column value.</summary>
    internal static ProcessDisplayCode DecodeExtendedControlFlowGuard(uint flags)
    {
        if ((flags & CFGEnabled) == 0) return ProcessDisplayCode.Disabled;
        return (flags & XFGEnabled) != 0 ? ProcessDisplayCode.Enabled : ProcessDisplayCode.Disabled;
    }

    public static ProcessDisplayCode ReadIOPriority(IntPtr processHandle)
    {
        int priority = 0;
        int status = NtDll.NtQueryInformationProcess(
            processHandle,
            ProcessIOPriority,
            &priority,
            sizeof(int),
            returnLength: null);
        if (status < 0) return ProcessDisplayCode.Unavailable;

        return priority switch
        {
            0 => ProcessDisplayCode.VeryLow,
            1 => ProcessDisplayCode.Low,
            2 => ProcessDisplayCode.Normal,
            3 => ProcessDisplayCode.High,
            4 => ProcessDisplayCode.Critical,
            _ => ProcessDisplayCode.Unavailable
        };
    }

    /// <summary>Reads the Power throttling value Task Manager shows, which is its Efficiency mode state.</summary>
    /// <remarks>
    /// Task Manager's Efficiency mode command sets both states, and both its Power throttling and Status columns
    /// require both: EcoQoS execution-speed throttling and the idle priority class.
    /// </remarks>
    public static ProcessDisplayCode ReadPowerThrottling(IntPtr processHandle)
    {
        PROCESS_POWER_THROTTLING_STATE state = new() { Version = ProcessPowerThrottlingCurrentVersion };
        if (!GetProcessInformation(
                processHandle,
                ProcessPowerThrottling,
                ref state,
                (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>()))
            return ProcessDisplayCode.Unavailable;

        if ((state.ControlMask & ProcessPowerThrottlingExecutionSpeed) == 0
            || (state.StateMask & ProcessPowerThrottlingExecutionSpeed) == 0)
            return ProcessDisplayCode.Disabled;

        uint priorityClass = GetPriorityClass(processHandle);
        if (priorityClass == 0) return ProcessDisplayCode.Unavailable;
        return priorityClass == IdlePriorityClass
            ? ProcessDisplayCode.Enabled
            : ProcessDisplayCode.Disabled;
    }

    /// <summary>Reads whether a process is in Efficiency mode; an unreadable state reports false.</summary>
    public static bool ReadIsEfficiencyMode(IntPtr processHandle) =>
        ReadPowerThrottling(processHandle) == ProcessDisplayCode.Enabled;

    public static ProcessDisplayCode ReadDPIAwareness(IntPtr processHandle)
    {
        int result = GetProcessDpiAwareness(processHandle, out int awareness);
        if (result < 0) return ProcessDisplayCode.Unavailable;

        return awareness switch
        {
            0 => ProcessDisplayCode.DPIUnaware,
            1 => ProcessDisplayCode.DPISystem,
            2 => ProcessDisplayCode.DPIPerMonitor,
            _ => ProcessDisplayCode.Unavailable
        };
    }

    public static ProcessDisplayCode ReadHardwareStackProtection(IntPtr processHandle)
    {
        PROCESS_MITIGATION_POLICY_INFORMATION policy = default;
        if (!GetProcessMitigationPolicy(
                processHandle,
                ProcessUserShadowStackPolicy,
                ref policy,
                Marshal.SizeOf<PROCESS_MITIGATION_POLICY_INFORMATION>()))
            return ProcessDisplayCode.Unavailable;

        return (policy.Flags & UserShadowStackEnabled) != 0
            ? ProcessDisplayCode.Enabled
            : ProcessDisplayCode.Disabled;
    }

    public static ProcessDisplayCode ReadExtendedControlFlowGuard(IntPtr processHandle)
    {
        PROCESS_MITIGATION_POLICY_INFORMATION policy = default;
        if (!GetProcessMitigationPolicy(
                processHandle,
                ProcessControlFlowGuardPolicy,
                ref policy,
                Marshal.SizeOf<PROCESS_MITIGATION_POLICY_INFORMATION>()))
            return ProcessDisplayCode.Unavailable;

        return DecodeExtendedControlFlowGuard(policy.Flags);
    }

    /// <summary>Returns the aggregate nominal cycle capacity of all active logical processors.</summary>
    public static double ReadNominalProcessorCycleCapacity()
    {
        uint processorCount = (uint)Kernel32Processor.MachineProcessorCount;
        if (processorCount is 0 or > 4_096) return 0;

        int entrySize = Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>();
        int bufferSize = checked((int)processorCount * entrySize);
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            uint status = CallNtPowerInformation(
                ProcessorPowerInformation,
                IntPtr.Zero,
                inputBufferSize: 0,
                buffer,
                (uint)bufferSize);
            if (status != 0) return 0;

            double nominalCyclesPerSecond = 0;
            for (int processorIndex = 0; processorIndex < processorCount; processorIndex++)
            {
                IntPtr entryAddress = IntPtr.Add(buffer, processorIndex * entrySize);
                PROCESSOR_POWER_INFORMATION information =
                    Marshal.PtrToStructure<PROCESSOR_POWER_INFORMATION>(entryAddress);
                nominalCyclesPerSecond += information.MaxMegahertz * MegahertzToHertz;
            }

            return nominalCyclesPerSecond;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string LookupAccountName(IntPtr sid)
    {
        uint nameLength = 0;
        uint domainLength = 0;
        _ = LookupAccountSidW(systemName: null, sid, name: null, ref nameLength, referencedDomainName: null,
            ref domainLength, out _);
        if (Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer || nameLength == 0) return Unavailable;

        StringBuilder name = new((int)nameLength);
        StringBuilder domain = new((int)Math.Max(val1: 1, domainLength));
        if (!LookupAccountSidW(systemName: null, sid, name, ref nameLength, domain, ref domainLength, out _))
            return Unavailable;

        return domain.Length == 0 ? name.ToString() : string.Concat(domain, arg1: "\\", name);
    }

    private static bool TryReadTokenInteger(IntPtr processHandle, int informationClass, out int value)
    {
        value = 0;
        if (!OpenProcessToken(processHandle, TokenQuery, out IntPtr tokenHandle)) return false;

        try
        {
            IntPtr buffer = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                if (!GetTokenInformation(tokenHandle, informationClass, buffer, sizeof(int), out _))
                    return false;
                value = Marshal.ReadInt32(buffer);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            Kernel32.CloseHandle(tokenHandle);
        }
    }

    private static int ToSignedCount(uint value) => value > int.MaxValue ? int.MaxValue : (int)value;

    // Without the cast ReSharper reads the comparison as nuint and reports CS0652
    // ReSharper disable once RedundantCast
    private static long ToLong(nuint value) => (ulong)value > long.MaxValue ? long.MaxValue : (long)value;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryProcessCycleTime(IntPtr process, out ulong cycleTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr process, out IO_COUNTERS counters);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetPriorityClass(IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessDEPPolicy(
        IntPtr process,
        out uint flags,
        [MarshalAs(UnmanagedType.Bool)] out bool permanent);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessInformation(
        IntPtr process,
        int informationClass,
        ref PROCESS_POWER_THROTTLING_STATE information,
        uint informationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMitigationPolicy(
        IntPtr process,
        int mitigationPolicy,
        ref PROCESS_MITIGATION_POLICY_INFORMATION buffer,
        int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process2(
        IntPtr process,
        out ushort processMachine,
        out ushort nativeMachine);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(
        int informationLevel,
        IntPtr inputBuffer,
        uint inputBufferSize,
        IntPtr outputBuffer,
        uint outputBufferSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFullName(IntPtr process, ref uint packageFullNameLength, char* packageFullName);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(
        IntPtr process,
        ref PROCESS_MEMORY_COUNTERS_EX2 counters,
        uint size);

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr token,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupAccountSidW(
        string? systemName,
        IntPtr sid,
        StringBuilder? name,
        ref uint nameLength,
        StringBuilder? referencedDomainName,
        ref uint domainNameLength,
        out int use);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertSidToStringSidW(IntPtr sid, out IntPtr stringSid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("shcore.dll")]
    private static extern int GetProcessDpiAwareness(IntPtr process, out int awareness);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_MEMORY_COUNTERS_EX2
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
        public nuint PrivateWorkingSetSize;
        public nuint SharedCommitUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_USER
    {
        public SID_AND_ATTRIBUTES User;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2A;
        public IntPtr Reserved2B;
        public IntPtr UniqueProcessID;
        public IntPtr InheritedFromUniqueProcessID;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_MITIGATION_POLICY_INFORMATION
    {
        public uint Flags;
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

    internal readonly record struct ProcessMemoryCounters(
        long WorkingSetBytes,
        long PeakWorkingSetBytes,
        long PrivateWorkingSetBytes,
        long SharedWorkingSetBytes,
        long CommitSizeBytes,
        long PagedPoolBytes,
        long NonPagedPoolBytes,
        long PageFaultCount);

    internal readonly record struct ProcessIOCounters(
        ulong ReadOperations,
        ulong WriteOperations,
        ulong OtherOperations,
        ulong ReadBytes,
        ulong WriteBytes,
        ulong OtherBytes);
}
