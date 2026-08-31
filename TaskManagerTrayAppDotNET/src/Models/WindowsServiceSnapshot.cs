namespace TaskManagerTrayAppDotNET.Models;

internal enum WindowsServiceStatus : byte
{
    Unknown,
    Stopped,
    StartPending,
    StopPending,
    Running,
    ContinuePending,
    PausePending,
    Paused
}

internal enum WindowsServiceStartType : byte
{
    Unknown,
    Boot,
    System,
    Automatic,
    OnDemand,
    Disabled
}

[Flags]
internal enum WindowsServiceAcceptedControls : uint
{
    None = 0,
    Stop = 0x00000001,
    PauseContinue = 0x00000002,
    Shutdown = 0x00000004,
    ParameterChange = 0x00000008,
    NetworkBindingChange = 0x00000010,
    HardwareProfileChange = 0x00000020,
    PowerEvent = 0x00000040,
    SessionChange = 0x00000080,
    PreShutdown = 0x00000100,
    TimeChange = 0x00000200,
    TriggerEvent = 0x00000400,
    UserModeReboot = 0x00000800
}

internal enum WindowsServiceAction : byte
{
    Start,
    Stop,
    Restart,
    Disable
}

internal enum WindowsServiceOperationStage : byte
{
    None,
    Validate,
    OpenManager,
    OpenService,
    QueryStatus,
    SendControl,
    ChangeConfiguration,
    WaitForState,
    Completed
}

/// <summary>One service row populated from the Windows Service Control Manager.</summary>
internal sealed record WindowsServiceSnapshot(
    string ServiceName,
    string DisplayName,
    uint PID,
    string Description,
    WindowsServiceStatus Status,
    string Group,
    WindowsServiceStartType StartType,
    WindowsServiceAcceptedControls AcceptedControls);

/// <summary>Button availability derived solely from a service's published state.</summary>
internal readonly record struct WindowsServiceActionState(
    bool CanStart,
    bool CanStop,
    bool CanRestart,
    bool CanDisable);

/// <summary>Structured result for a Service Control Manager mutation.</summary>
internal readonly record struct WindowsServiceOperationResult(
    WindowsServiceAction Action,
    WindowsServiceOperationStage Stage,
    string ServiceName,
    bool Succeeded,
    WindowsServiceStatus FinalStatus,
    int Win32ErrorCode,
    string ErrorMessage)
{
    public static WindowsServiceOperationResult Success(
        WindowsServiceAction action,
        string serviceName,
        WindowsServiceStatus finalStatus) =>
        new(
            action,
            WindowsServiceOperationStage.Completed,
            serviceName,
            Succeeded: true,
            finalStatus,
            Win32ErrorCode: 0,
            string.Empty);

    public static WindowsServiceOperationResult Failure(
        WindowsServiceAction action,
        WindowsServiceOperationStage stage,
        string serviceName,
        WindowsServiceStatus finalStatus,
        int win32ErrorCode,
        string errorMessage) =>
        new(
            action,
            stage,
            serviceName,
            Succeeded: false,
            finalStatus,
            win32ErrorCode,
            errorMessage);
}

/// <summary>Structured result for a Service Control Manager enumeration.</summary>
internal sealed record WindowsServiceQueryResult(
    bool Succeeded,
    IReadOnlyList<WindowsServiceSnapshot> Services,
    int Win32ErrorCode,
    string ErrorMessage)
{
    public static WindowsServiceQueryResult Success(IReadOnlyList<WindowsServiceSnapshot> services) =>
        new(Succeeded: true, services, Win32ErrorCode: 0, string.Empty);

    public static WindowsServiceQueryResult Failure(int win32ErrorCode, string errorMessage) =>
        new(Succeeded: false, [], win32ErrorCode, errorMessage);
}

/// <summary>Pure mappings and UI action rules for native service values.</summary>
internal static class WindowsServiceState
{
    private const uint ServiceStopped = 0x00000001;
    private const uint ServiceStartPending = 0x00000002;
    private const uint ServiceStopPending = 0x00000003;
    private const uint ServiceRunning = 0x00000004;
    private const uint ServiceContinuePending = 0x00000005;
    private const uint ServicePausePending = 0x00000006;
    private const uint ServicePaused = 0x00000007;

    private const uint ServiceBootStart = 0x00000000;
    private const uint ServiceSystemStart = 0x00000001;
    private const uint ServiceAutomaticStart = 0x00000002;
    private const uint ServiceDemandStart = 0x00000003;
    private const uint ServiceDisabled = 0x00000004;

    public static WindowsServiceStatus FromNativeStatus(uint status) => status switch
    {
        ServiceStopped => WindowsServiceStatus.Stopped,
        ServiceStartPending => WindowsServiceStatus.StartPending,
        ServiceStopPending => WindowsServiceStatus.StopPending,
        ServiceRunning => WindowsServiceStatus.Running,
        ServiceContinuePending => WindowsServiceStatus.ContinuePending,
        ServicePausePending => WindowsServiceStatus.PausePending,
        ServicePaused => WindowsServiceStatus.Paused,
        _ => WindowsServiceStatus.Unknown
    };

    public static WindowsServiceStartType FromNativeStartType(uint startType) => startType switch
    {
        ServiceBootStart => WindowsServiceStartType.Boot,
        ServiceSystemStart => WindowsServiceStartType.System,
        ServiceAutomaticStart => WindowsServiceStartType.Automatic,
        ServiceDemandStart => WindowsServiceStartType.OnDemand,
        ServiceDisabled => WindowsServiceStartType.Disabled,
        _ => WindowsServiceStartType.Unknown
    };

    public static string GetStatusText(WindowsServiceStatus status) => status switch
    {
        WindowsServiceStatus.Stopped => "Stopped",
        WindowsServiceStatus.StartPending => "Starting",
        WindowsServiceStatus.StopPending => "Stopping",
        WindowsServiceStatus.Running => "Running",
        WindowsServiceStatus.ContinuePending => "Continuing",
        WindowsServiceStatus.PausePending => "Pausing",
        WindowsServiceStatus.Paused => "Paused",
        _ => "Unknown"
    };

    public static string NormalizeServiceName(string? serviceName) => serviceName?.Trim() ?? string.Empty;

    public static string NormalizeDisplayName(string? displayName, string serviceName)
    {
        string normalizedDisplayName = displayName?.Trim() ?? string.Empty;
        return normalizedDisplayName.Length == 0 ? serviceName : normalizedDisplayName;
    }

    public static string NormalizeOptionalText(string? value) => value?.Trim() ?? string.Empty;

    public static uint NormalizePID(WindowsServiceStatus status, uint processID) =>
        status == WindowsServiceStatus.Stopped ? 0 : processID;

    public static WindowsServiceActionState GetActionState(WindowsServiceSnapshot service)
    {
        ArgumentNullException.ThrowIfNull(service);

        bool isPending = service.Status is WindowsServiceStatus.StartPending
            or WindowsServiceStatus.StopPending
            or WindowsServiceStatus.ContinuePending
            or WindowsServiceStatus.PausePending;
        bool isDisabled = service.StartType == WindowsServiceStartType.Disabled;
        bool acceptsStop = service.AcceptedControls.HasFlag(WindowsServiceAcceptedControls.Stop);
        bool isRunning = service.Status is WindowsServiceStatus.Running or WindowsServiceStatus.Paused;
        bool hasStableStatus = isRunning || service.Status == WindowsServiceStatus.Stopped;

        return new WindowsServiceActionState(
            !isPending && !isDisabled && service.Status == WindowsServiceStatus.Stopped,
            !isPending && isRunning && acceptsStop,
            !isPending && !isDisabled && isRunning && acceptsStop,
            !isPending && !isDisabled && hasStableStatus);
    }
}
