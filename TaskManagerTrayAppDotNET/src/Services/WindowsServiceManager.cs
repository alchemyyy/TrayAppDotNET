using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Enumerates and controls local Win32 services through the Service Control Manager.</summary>
internal sealed partial class WindowsServiceManager
{
    private const int ErrorInvalidName = 123;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorMoreData = 234;
    private const int ErrorNotSupported = 50;
    private const int ErrorServiceRequestTimeout = 1053;

    private const uint ServiceControlStop = 0x00000001;
    private const uint ServiceNoChange = 0xFFFFFFFF;
    private const uint ServiceDisabled = 0x00000004;
    private const uint ServiceWin32 = 0x00000030;
    private const uint ServiceStateAll = 0x00000003;
    private const int ServiceConfigDescription = 1;
    private const int ServiceStatusProcessInfo = 0;
    private const int ServiceEnumProcessInfo = 0;

    private const uint ServiceQueryConfig = 0x00000001;
    private const uint ServiceChangeConfig = 0x00000002;
    private const uint ServiceQueryStatus = 0x00000004;
    private const uint ServiceStart = 0x00000010;
    private const uint ServiceStop = 0x00000020;
    private const uint ServiceActionRights = ServiceQueryStatus;

    private const uint ServiceControlManagerConnect = 0x00000001;
    private const uint ServiceControlManagerEnumerateService = 0x00000004;

    private const int InitialEnumerationBufferSize = 64 * 1024;
    private const int MaximumNativeBufferSize = 64 * 1024 * 1024;
    private const int MaximumEnumerationPages = 64;
    private const int DefaultOperationTimeoutMilliseconds = 30_000;
    private const int MinimumStatusPollMilliseconds = 50;
    private const int MaximumStatusPollMilliseconds = 500;

    private readonly WindowsServiceConfigurationCache _configurationCache = new();

    /// <summary>Enumerates services, optionally bypassing cached static configuration.</summary>
    public WindowsServiceQueryResult QueryServices(bool refreshConfiguration = false)
    {
        if (!OperatingSystem.IsWindows())
            return WindowsServiceQueryResult.Failure(ErrorNotSupported, WindowsOnlyMessage());

        using SafeServiceHandle managerHandle = NativeMethods.OpenSCManagerW(
            null,
            null,
            ServiceControlManagerConnect | ServiceControlManagerEnumerateService);
        if (managerHandle.IsInvalid)
            return QueryFailureFromLastError();

        Dictionary<string, WindowsServiceSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> serviceNames = new(StringComparer.OrdinalIgnoreCase);
        int bufferSize = ResolveInitialEnumerationBufferSize(managerHandle, out int initialError);
        if (bufferSize <= 0)
        {
            if (initialError == 0)
            {
                _configurationCache.RetainOnly(serviceNames);
                return WindowsServiceQueryResult.Success(Array.Empty<WindowsServiceSnapshot>());
            }
            return QueryFailure(initialError);
        }

        using NativeBuffer buffer = new(bufferSize);
        uint resumeHandle = 0;
        for (int pageIndex = 0; pageIndex < MaximumEnumerationPages; pageIndex++)
        {
            bool completed = NativeMethods.EnumServicesStatusExW(
                managerHandle,
                ServiceEnumProcessInfo,
                ServiceWin32,
                ServiceStateAll,
                buffer.DangerousGetHandle(),
                checked((uint)buffer.ByteCount),
                out uint bytesNeeded,
                out uint servicesReturned,
                ref resumeHandle,
                null);
            int errorCode = completed ? 0 : Marshal.GetLastPInvokeError();

            AppendServicePage(
                managerHandle,
                buffer.DangerousGetHandle(),
                servicesReturned,
                refreshConfiguration,
                snapshots,
                serviceNames);
            if (completed)
            {
                _configurationCache.RetainOnly(serviceNames);
                List<WindowsServiceSnapshot> ordered = new(snapshots.Values);
                ordered.Sort(static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(
                    left.ServiceName,
                    right.ServiceName));
                return WindowsServiceQueryResult.Success(ordered);
            }

            if (errorCode != ErrorMoreData) return QueryFailure(errorCode);
            if (servicesReturned != 0) continue;

            // A single record no longer fits. Retry from the same resume point with a larger buffer
            if (bytesNeeded <= buffer.ByteCount || bytesNeeded > MaximumNativeBufferSize)
                return QueryFailure(errorCode);
            buffer.Resize(checked((int)bytesNeeded));
        }

        return QueryFailure(
            ErrorMoreData,
            "Service enumeration exceeded the maximum number of native result pages.");
    }

    /// <summary>Starts a stopped service and waits for the running state.</summary>
    public WindowsServiceOperationResult Start(string serviceName) =>
        RunAction(serviceName, WindowsServiceAction.Start, StartCore);

    /// <summary>Stops a running service and waits for the stopped state.</summary>
    public WindowsServiceOperationResult Stop(string serviceName) =>
        RunAction(serviceName, WindowsServiceAction.Stop, StopCore);

    /// <summary>Stops and starts a service without changing its startup configuration.</summary>
    public WindowsServiceOperationResult Restart(string serviceName) =>
        RunAction(serviceName, WindowsServiceAction.Restart, RestartCore);

    /// <summary>Changes the service start type to Disabled without stopping a running service.</summary>
    public WindowsServiceOperationResult Disable(string serviceName)
    {
        WindowsServiceOperationResult result = RunAction(
            serviceName,
            WindowsServiceAction.Disable,
            DisableCore);
        if (result.Succeeded) _configurationCache.Invalidate(result.ServiceName);
        return result;
    }

    private static WindowsServiceOperationResult RunAction(
        string serviceName,
        WindowsServiceAction action,
        Func<SafeServiceHandle, string, WindowsServiceAction, WindowsServiceOperationResult> execute)
    {
        string normalizedServiceName = WindowsServiceState.NormalizeServiceName(serviceName);
        if (normalizedServiceName.Length == 0)
        {
            return Failure(
                action,
                WindowsServiceOperationStage.Validate,
                normalizedServiceName,
                WindowsServiceStatus.Unknown,
                ErrorInvalidName,
                "A service name is required.");
        }
        if (!OperatingSystem.IsWindows())
        {
            return Failure(
                action,
                WindowsServiceOperationStage.OpenManager,
                normalizedServiceName,
                WindowsServiceStatus.Unknown,
                ErrorNotSupported,
                WindowsOnlyMessage());
        }

        using SafeServiceHandle managerHandle = NativeMethods.OpenSCManagerW(
            null,
            null,
            ServiceControlManagerConnect);
        if (managerHandle.IsInvalid)
        {
            return FailureFromLastError(
                action,
                WindowsServiceOperationStage.OpenManager,
                normalizedServiceName,
                WindowsServiceStatus.Unknown);
        }

        return execute(managerHandle, normalizedServiceName, action);
    }

    private static WindowsServiceOperationResult StartCore(
        SafeServiceHandle managerHandle,
        string serviceName,
        WindowsServiceAction action)
    {
        using SafeServiceHandle serviceHandle = NativeMethods.OpenServiceW(
            managerHandle,
            serviceName,
            ServiceActionRights | ServiceStart);
        if (serviceHandle.IsInvalid)
            return OpenServiceFailure(action, serviceName);

        if (!TryQueryStatus(serviceHandle, out NativeServiceStatusProcess nativeStatus, out int queryError))
        {
            return Failure(
                action,
                WindowsServiceOperationStage.QueryStatus,
                serviceName,
                WindowsServiceStatus.Unknown,
                queryError);
        }

        WindowsServiceStatus status = WindowsServiceState.FromNativeStatus(nativeStatus.CurrentState);
        if (status == WindowsServiceStatus.Running)
            return WindowsServiceOperationResult.Success(action, serviceName, status);
        if (status != WindowsServiceStatus.StartPending &&
            !NativeMethods.StartServiceW(serviceHandle, 0, IntPtr.Zero))
        {
            return FailureFromLastError(
                action,
                WindowsServiceOperationStage.SendControl,
                serviceName,
                status);
        }

        return WaitForState(serviceHandle, serviceName, action, WindowsServiceStatus.Running);
    }

    private static WindowsServiceOperationResult StopCore(
        SafeServiceHandle managerHandle,
        string serviceName,
        WindowsServiceAction action)
    {
        using SafeServiceHandle serviceHandle = NativeMethods.OpenServiceW(
            managerHandle,
            serviceName,
            ServiceActionRights | ServiceStop);
        if (serviceHandle.IsInvalid)
            return OpenServiceFailure(action, serviceName);

        return StopOpenedService(serviceHandle, serviceName, action);
    }

    private static WindowsServiceOperationResult RestartCore(
        SafeServiceHandle managerHandle,
        string serviceName,
        WindowsServiceAction action)
    {
        using SafeServiceHandle serviceHandle = NativeMethods.OpenServiceW(
            managerHandle,
            serviceName,
            ServiceActionRights | ServiceStart | ServiceStop);
        if (serviceHandle.IsInvalid)
            return OpenServiceFailure(action, serviceName);

        WindowsServiceOperationResult stopResult = StopOpenedService(serviceHandle, serviceName, action);
        if (!stopResult.Succeeded) return stopResult;

        if (!NativeMethods.StartServiceW(serviceHandle, 0, IntPtr.Zero))
        {
            return FailureFromLastError(
                action,
                WindowsServiceOperationStage.SendControl,
                serviceName,
                WindowsServiceStatus.Stopped);
        }

        return WaitForState(serviceHandle, serviceName, action, WindowsServiceStatus.Running);
    }

    private static WindowsServiceOperationResult DisableCore(
        SafeServiceHandle managerHandle,
        string serviceName,
        WindowsServiceAction action)
    {
        using SafeServiceHandle serviceHandle = NativeMethods.OpenServiceW(
            managerHandle,
            serviceName,
            ServiceChangeConfig | ServiceQueryStatus);
        if (serviceHandle.IsInvalid)
            return OpenServiceFailure(action, serviceName);

        WindowsServiceStatus status = TryQueryStatus(
            serviceHandle,
            out NativeServiceStatusProcess nativeStatus,
            out _)
            ? WindowsServiceState.FromNativeStatus(nativeStatus.CurrentState)
            : WindowsServiceStatus.Unknown;
        if (!NativeMethods.ChangeServiceConfigW(
                serviceHandle,
                ServiceNoChange,
                ServiceDisabled,
                ServiceNoChange,
                null,
                null,
                IntPtr.Zero,
                null,
                null,
                null,
                null))
        {
            return FailureFromLastError(
                action,
                WindowsServiceOperationStage.ChangeConfiguration,
                serviceName,
                status);
        }

        // SERVICE_DISABLED affects future starts. Deliberately do not send SERVICE_CONTROL_STOP
        return WindowsServiceOperationResult.Success(action, serviceName, status);
    }

    private static WindowsServiceOperationResult StopOpenedService(
        SafeServiceHandle serviceHandle,
        string serviceName,
        WindowsServiceAction action)
    {
        if (!TryQueryStatus(serviceHandle, out NativeServiceStatusProcess nativeStatus, out int queryError))
        {
            return Failure(
                action,
                WindowsServiceOperationStage.QueryStatus,
                serviceName,
                WindowsServiceStatus.Unknown,
                queryError);
        }

        WindowsServiceStatus status = WindowsServiceState.FromNativeStatus(nativeStatus.CurrentState);
        if (status == WindowsServiceStatus.Stopped)
            return WindowsServiceOperationResult.Success(action, serviceName, status);
        if (status != WindowsServiceStatus.StopPending &&
            !NativeMethods.ControlService(serviceHandle, ServiceControlStop, out _))
        {
            return FailureFromLastError(
                action,
                WindowsServiceOperationStage.SendControl,
                serviceName,
                status);
        }

        return WaitForState(serviceHandle, serviceName, action, WindowsServiceStatus.Stopped);
    }

    private static WindowsServiceOperationResult WaitForState(
        SafeServiceHandle serviceHandle,
        string serviceName,
        WindowsServiceAction action,
        WindowsServiceStatus targetStatus)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds < DefaultOperationTimeoutMilliseconds)
        {
            if (!TryQueryStatus(serviceHandle, out NativeServiceStatusProcess nativeStatus, out int queryError))
            {
                return Failure(
                    action,
                    WindowsServiceOperationStage.QueryStatus,
                    serviceName,
                    WindowsServiceStatus.Unknown,
                    queryError);
            }

            WindowsServiceStatus status = WindowsServiceState.FromNativeStatus(nativeStatus.CurrentState);
            if (status == targetStatus)
                return WindowsServiceOperationResult.Success(action, serviceName, status);

            int waitHintMilliseconds = nativeStatus.WaitHint > int.MaxValue
                ? MaximumStatusPollMilliseconds
                : (int)nativeStatus.WaitHint / 10;
            int delayMilliseconds = Math.Clamp(
                waitHintMilliseconds,
                MinimumStatusPollMilliseconds,
                MaximumStatusPollMilliseconds);
            Thread.Sleep(delayMilliseconds);
        }

        return Failure(
            action,
            WindowsServiceOperationStage.WaitForState,
            serviceName,
            WindowsServiceStatus.Unknown,
            ErrorServiceRequestTimeout,
            $"The service did not reach the {WindowsServiceState.GetStatusText(targetStatus)} state within "
            + $"{DefaultOperationTimeoutMilliseconds / 1_000} seconds.");
    }

    private static bool TryQueryStatus(
        SafeServiceHandle serviceHandle,
        out NativeServiceStatusProcess status,
        out int errorCode)
    {
        int statusSize = Marshal.SizeOf<NativeServiceStatusProcess>();
        using NativeBuffer buffer = new(statusSize);
        bool succeeded = NativeMethods.QueryServiceStatusEx(
            serviceHandle,
            ServiceStatusProcessInfo,
            buffer.DangerousGetHandle(),
            checked((uint)statusSize),
            out _);
        if (!succeeded)
        {
            status = default;
            errorCode = Marshal.GetLastPInvokeError();
            return false;
        }

        status = Marshal.PtrToStructure<NativeServiceStatusProcess>(buffer.DangerousGetHandle());
        errorCode = 0;
        return true;
    }

    private static int ResolveInitialEnumerationBufferSize(
        SafeServiceHandle managerHandle,
        out int errorCode)
    {
        uint resumeHandle = 0;
        bool succeeded = NativeMethods.EnumServicesStatusExW(
            managerHandle,
            ServiceEnumProcessInfo,
            ServiceWin32,
            ServiceStateAll,
            IntPtr.Zero,
            0,
            out uint bytesNeeded,
            out _,
            ref resumeHandle,
            null);
        if (succeeded)
        {
            errorCode = 0;
            return bytesNeeded == 0 ? 0 : checked((int)bytesNeeded);
        }

        errorCode = Marshal.GetLastPInvokeError();
        if (errorCode != ErrorMoreData) return 0;
        if (bytesNeeded > MaximumNativeBufferSize)
        {
            errorCode = ErrorInsufficientBuffer;
            return 0;
        }

        return Math.Max(InitialEnumerationBufferSize, checked((int)bytesNeeded));
    }

    private void AppendServicePage(
        SafeServiceHandle managerHandle,
        IntPtr pageAddress,
        uint serviceCount,
        bool refreshConfiguration,
        Dictionary<string, WindowsServiceSnapshot> destination,
        HashSet<string> serviceNames)
    {
        int entrySize = Marshal.SizeOf<NativeEnumServiceStatusProcess>();
        for (uint serviceIndex = 0; serviceIndex < serviceCount; serviceIndex++)
        {
            int entryOffset = checked((int)serviceIndex * entrySize);
            NativeEnumServiceStatusProcess nativeService =
                Marshal.PtrToStructure<NativeEnumServiceStatusProcess>(IntPtr.Add(pageAddress, entryOffset));
            string serviceName = WindowsServiceState.NormalizeServiceName(
                Marshal.PtrToStringUni(nativeService.ServiceName));
            if (serviceName.Length == 0) continue;

            WindowsServiceStatus status = WindowsServiceState.FromNativeStatus(
                nativeService.Status.CurrentState);
            WindowsServiceConfiguration configuration = GetConfiguration(
                managerHandle,
                serviceName,
                refreshConfiguration);
            WindowsServiceSnapshot snapshot = new(
                serviceName,
                WindowsServiceState.NormalizeDisplayName(
                    Marshal.PtrToStringUni(nativeService.DisplayName),
                    serviceName),
                WindowsServiceState.NormalizePID(status, nativeService.Status.ProcessID),
                configuration.Description,
                status,
                configuration.Group,
                configuration.StartType,
                (WindowsServiceAcceptedControls)nativeService.Status.ControlsAccepted);
            destination[serviceName] = snapshot;
            serviceNames.Add(serviceName);
        }
    }

    private WindowsServiceConfiguration GetConfiguration(
        SafeServiceHandle managerHandle,
        string serviceName,
        bool refreshConfiguration)
    {
        if (!refreshConfiguration
            && _configurationCache.TryGet(serviceName, out WindowsServiceConfiguration cached))
        {
            return cached;
        }

        WindowsServiceConfiguration configuration = ReadConfiguration(managerHandle, serviceName);
        _configurationCache.Store(serviceName, configuration);
        return configuration;
    }

    private static WindowsServiceConfiguration ReadConfiguration(
        SafeServiceHandle managerHandle,
        string serviceName)
    {
        string description = string.Empty;
        string group = string.Empty;
        WindowsServiceStartType startType = WindowsServiceStartType.Unknown;

        using SafeServiceHandle serviceHandle = NativeMethods.OpenServiceW(
            managerHandle,
            serviceName,
            ServiceQueryConfig);
        if (serviceHandle.IsInvalid)
            return new WindowsServiceConfiguration(description, group, startType);

        if (TryReadBaseConfiguration(
                serviceHandle,
                out string binaryPathName,
                out WindowsServiceStartType configuredStartType))
        {
            group = TryReadServiceHostGroup(binaryPathName);
            startType = configuredStartType;
        }

        description = TryReadDescription(serviceHandle);
        return new WindowsServiceConfiguration(description, group, startType);
    }

    private static bool TryReadBaseConfiguration(
        SafeServiceHandle serviceHandle,
        out string binaryPathName,
        out WindowsServiceStartType startType)
    {
        binaryPathName = string.Empty;
        startType = WindowsServiceStartType.Unknown;
        _ = NativeMethods.QueryServiceConfigW(serviceHandle, IntPtr.Zero, 0, out uint bytesNeeded);
        int errorCode = Marshal.GetLastPInvokeError();
        if (errorCode != ErrorInsufficientBuffer || bytesNeeded == 0 || bytesNeeded > MaximumNativeBufferSize)
            return false;

        using NativeBuffer buffer = new(checked((int)bytesNeeded));
        if (!NativeMethods.QueryServiceConfigW(
                serviceHandle,
                buffer.DangerousGetHandle(),
                bytesNeeded,
                out _))
        {
            return false;
        }

        NativeQueryServiceConfig configuration =
            Marshal.PtrToStructure<NativeQueryServiceConfig>(buffer.DangerousGetHandle());
        binaryPathName = WindowsServiceState.NormalizeOptionalText(
            Marshal.PtrToStringUni(configuration.BinaryPathName));
        startType = WindowsServiceState.FromNativeStartType(configuration.StartType);
        return true;
    }

    private static string TryReadServiceHostGroup(string binaryPathName)
    {
        if (binaryPathName.Length == 0) return string.Empty;

        IntPtr argumentList = NativeMethods.CommandLineToArgvW(binaryPathName, out int argumentCount);
        if (argumentList == IntPtr.Zero) return string.Empty;

        try
        {
            if (argumentCount < 2) return string.Empty;

            for (int argumentIndex = 1; argumentIndex < argumentCount - 1; argumentIndex++)
            {
                IntPtr argumentAddress = Marshal.ReadIntPtr(argumentList, argumentIndex * IntPtr.Size);
                string argument = Marshal.PtrToStringUni(argumentAddress) ?? string.Empty;
                if (!string.Equals(argument, "-k", StringComparison.OrdinalIgnoreCase)) continue;

                IntPtr groupAddress = Marshal.ReadIntPtr(argumentList, (argumentIndex + 1) * IntPtr.Size);
                return WindowsServiceState.NormalizeOptionalText(Marshal.PtrToStringUni(groupAddress));
            }
        }
        finally
        {
            _ = NativeMethods.LocalFree(argumentList);
        }

        return string.Empty;
    }

    private static string TryReadDescription(SafeServiceHandle serviceHandle)
    {
        _ = NativeMethods.QueryServiceConfig2W(
            serviceHandle,
            ServiceConfigDescription,
            IntPtr.Zero,
            0,
            out uint bytesNeeded);
        int errorCode = Marshal.GetLastPInvokeError();
        if (errorCode != ErrorInsufficientBuffer || bytesNeeded == 0 || bytesNeeded > MaximumNativeBufferSize)
            return string.Empty;

        using NativeBuffer buffer = new(checked((int)bytesNeeded));
        if (!NativeMethods.QueryServiceConfig2W(
                serviceHandle,
                ServiceConfigDescription,
                buffer.DangerousGetHandle(),
                bytesNeeded,
                out _))
        {
            return string.Empty;
        }

        NativeServiceDescription nativeDescription =
            Marshal.PtrToStructure<NativeServiceDescription>(buffer.DangerousGetHandle());
        return WindowsServiceState.NormalizeOptionalText(
            Marshal.PtrToStringUni(nativeDescription.Description));
    }

    private static WindowsServiceOperationResult OpenServiceFailure(
        WindowsServiceAction action,
        string serviceName) =>
        FailureFromLastError(
            action,
            WindowsServiceOperationStage.OpenService,
            serviceName,
            WindowsServiceStatus.Unknown);

    private static WindowsServiceQueryResult QueryFailureFromLastError() =>
        QueryFailure(Marshal.GetLastPInvokeError());

    private static WindowsServiceQueryResult QueryFailure(int errorCode) =>
        QueryFailure(errorCode, FormatWin32Error(errorCode));

    private static WindowsServiceQueryResult QueryFailure(int errorCode, string message) =>
        WindowsServiceQueryResult.Failure(errorCode, message);

    private static WindowsServiceOperationResult FailureFromLastError(
        WindowsServiceAction action,
        WindowsServiceOperationStage stage,
        string serviceName,
        WindowsServiceStatus finalStatus)
    {
        int errorCode = Marshal.GetLastPInvokeError();
        return Failure(action, stage, serviceName, finalStatus, errorCode);
    }

    private static WindowsServiceOperationResult Failure(
        WindowsServiceAction action,
        WindowsServiceOperationStage stage,
        string serviceName,
        WindowsServiceStatus finalStatus,
        int errorCode) =>
        Failure(action, stage, serviceName, finalStatus, errorCode, FormatWin32Error(errorCode));

    private static WindowsServiceOperationResult Failure(
        WindowsServiceAction action,
        WindowsServiceOperationStage stage,
        string serviceName,
        WindowsServiceStatus finalStatus,
        int errorCode,
        string errorMessage) =>
        WindowsServiceOperationResult.Failure(
            action,
            stage,
            serviceName,
            finalStatus,
            errorCode,
            errorMessage);

    private static string FormatWin32Error(int errorCode) => new Win32Exception(errorCode).Message;

    private static string WindowsOnlyMessage() => "Windows services are only available on Windows.";

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessID;
        public uint ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeEnumServiceStatusProcess
    {
        public IntPtr ServiceName;
        public IntPtr DisplayName;
        public NativeServiceStatusProcess Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeQueryServiceConfig
    {
        public uint ServiceType;
        public uint StartType;
        public uint ErrorControl;
        public IntPtr BinaryPathName;
        public IntPtr LoadOrderGroup;
        public uint TagID;
        public IntPtr Dependencies;
        public IntPtr ServiceStartName;
        public IntPtr DisplayName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeServiceDescription
    {
        public IntPtr Description;
    }

    private sealed class NativeBuffer : SafeHandleZeroOrMinusOneIsInvalid
    {
        public NativeBuffer(int byteCount)
            : base(true)
        {
            if (byteCount <= 0) throw new ArgumentOutOfRangeException(nameof(byteCount));
            SetHandle(Marshal.AllocHGlobal(byteCount));
            ByteCount = byteCount;
        }

        public int ByteCount { get; private set; }

        public void Resize(int byteCount)
        {
            if (byteCount <= ByteCount) return;
            if (byteCount > MaximumNativeBufferSize)
                throw new ArgumentOutOfRangeException(nameof(byteCount));

            SetHandle(Marshal.ReAllocHGlobal(handle, (IntPtr)byteCount));
            ByteCount = byteCount;
        }

        protected override bool ReleaseHandle()
        {
            Marshal.FreeHGlobal(handle);
            return true;
        }
    }

    private sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeServiceHandle()
            : base(true)
        {
        }

        protected override bool ReleaseHandle() => NativeMethods.CloseServiceHandle(handle);
    }

    private static partial class NativeMethods
    {
        [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        public static partial SafeServiceHandle OpenSCManagerW(
            string? machineName,
            string? databaseName,
            uint desiredAccess);

        [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        public static partial SafeServiceHandle OpenServiceW(
            SafeServiceHandle serviceControlManager,
            string serviceName,
            uint desiredAccess);

        [LibraryImport("advapi32.dll", EntryPoint = "CloseServiceHandle")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CloseServiceHandle(IntPtr serviceHandle);

        [LibraryImport("advapi32.dll", EntryPoint = "EnumServicesStatusExW", SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool EnumServicesStatusExW(
            SafeServiceHandle serviceControlManager,
            int infoLevel,
            uint serviceType,
            uint serviceState,
            IntPtr services,
            uint bufferSize,
            out uint bytesNeeded,
            out uint servicesReturned,
            ref uint resumeHandle,
            string? groupName);

        [LibraryImport("advapi32.dll", EntryPoint = "QueryServiceConfigW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool QueryServiceConfigW(
            SafeServiceHandle service,
            IntPtr serviceConfig,
            uint bufferSize,
            out uint bytesNeeded);

        [LibraryImport("advapi32.dll", EntryPoint = "QueryServiceConfig2W", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool QueryServiceConfig2W(
            SafeServiceHandle service,
            int infoLevel,
            IntPtr buffer,
            uint bufferSize,
            out uint bytesNeeded);

        [LibraryImport("advapi32.dll", EntryPoint = "QueryServiceStatusEx", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool QueryServiceStatusEx(
            SafeServiceHandle service,
            int infoLevel,
            IntPtr buffer,
            uint bufferSize,
            out uint bytesNeeded);

        [LibraryImport("advapi32.dll", EntryPoint = "StartServiceW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool StartServiceW(
            SafeServiceHandle service,
            uint argumentCount,
            IntPtr arguments);

        [LibraryImport("advapi32.dll", EntryPoint = "ControlService", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ControlService(
            SafeServiceHandle service,
            uint control,
            out NativeServiceStatus serviceStatus);

        [LibraryImport("advapi32.dll", EntryPoint = "ChangeServiceConfigW", SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ChangeServiceConfigW(
            SafeServiceHandle service,
            uint serviceType,
            uint startType,
            uint errorControl,
            string? binaryPathName,
            string? loadOrderGroup,
            IntPtr tagID,
            string? dependencies,
            string? serviceStartName,
            string? password,
            string? displayName);

        [LibraryImport("shell32.dll", EntryPoint = "CommandLineToArgvW", SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        public static partial IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

        [LibraryImport("kernel32.dll", EntryPoint = "LocalFree")]
        public static partial IntPtr LocalFree(IntPtr memory);
    }
}

/// <summary>Static service metadata that does not need to be queried with every status refresh.</summary>
internal readonly record struct WindowsServiceConfiguration(
    string Description,
    string Group,
    WindowsServiceStartType StartType);

/// <summary>Thread-safe static service metadata cache shared by refreshes and mutations.</summary>
internal sealed class WindowsServiceConfigurationCache
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, WindowsServiceConfiguration> _configurations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _staleServiceNames = [];

    public bool TryGet(string serviceName, out WindowsServiceConfiguration configuration)
    {
        lock (_gate)
            return _configurations.TryGetValue(serviceName, out configuration);
    }

    public void Store(string serviceName, WindowsServiceConfiguration configuration)
    {
        lock (_gate)
            _configurations[serviceName] = configuration;
    }

    public void Invalidate(string serviceName)
    {
        lock (_gate)
            _configurations.Remove(serviceName);
    }

    public void RetainOnly(IReadOnlySet<string> serviceNames)
    {
        ArgumentNullException.ThrowIfNull(serviceNames);
        lock (_gate)
        {
            _staleServiceNames.Clear();
            foreach (string serviceName in _configurations.Keys)
            {
                if (!serviceNames.Contains(serviceName)) _staleServiceNames.Add(serviceName);
            }

            for (int serviceIndex = 0; serviceIndex < _staleServiceNames.Count; serviceIndex++)
                _configurations.Remove(_staleServiceNames[serviceIndex]);
            _staleServiceNames.Clear();
        }
    }
}
