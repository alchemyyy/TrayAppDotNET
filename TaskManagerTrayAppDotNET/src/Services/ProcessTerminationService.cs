using System.ComponentModel;

namespace TaskManagerTrayAppDotNET.Services;

internal enum ElevatedHelperState
{
    NotRequested,
    Starting,
    Ready,
    Declined,
    Failed,
    Disposed
}

internal readonly record struct ElevatedHelperStatus(
    ElevatedHelperState State,
    string ErrorMessage);

internal delegate ElevatedKillHelperStartResult StartElevatedKillHelperAction(
    IntPtr ownerWindowHandle,
    Action<string>? log);

/// <summary>Coordinates redundant direct and optional pre-armed elevated termination paths.</summary>
internal sealed class ProcessTerminationService : IDisposable
{
    private const int HelperResponseTimeoutMilliseconds = 1_000;
    private const string ElevationLauncherThreadName = "Task Manager elevation launcher";

    private readonly Action<string>? _log;
    private readonly StartElevatedKillHelperAction _startElevatedHelper;
    private readonly object _sync = new();
    private ElevatedKillHelperSession? _helperSession;
    private TaskCompletionSource<ElevatedHelperStatus>? _pendingStartCompletion;
    private Task<ElevatedHelperStatus>? _pendingStartTask;
    private ProcessTerminationTarget? _armedTarget;
    private IntPtr _localTargetHandle;
    private long _targetGeneration;
    private int _localOpenError;
    private ElevatedHelperState _elevatedHelperState = ElevatedHelperState.NotRequested;
    private string _elevatedHelperError = string.Empty;
    private bool _disposed;

    public ProcessTerminationService(Action<string>? log)
        : this(log, ElevatedKillHelperClient.TryStart)
    {
    }

    internal ProcessTerminationService(
        Action<string>? log,
        StartElevatedKillHelperAction startElevatedHelper)
    {
        ArgumentNullException.ThrowIfNull(startElevatedHelper);
        _log = log;
        _startElevatedHelper = startElevatedHelper;
    }

    /// <summary>Returns the current optional elevated-helper capability state.</summary>
    public ElevatedHelperStatus GetElevatedHelperStatus()
    {
        lock (_sync)
            return CreateStatusWithoutLock();
    }

    /// <summary>Starts one owner-parented elevation attempt on a dedicated STA thread.</summary>
    public Task<ElevatedHelperStatus> EnableElevatedHelperAsync(IntPtr ownerWindowHandle)
    {
        TaskCompletionSource<ElevatedHelperStatus>? failedCompletion = null;
        ElevatedHelperStatus immediateStatus;

        lock (_sync)
        {
            if (_disposed || _elevatedHelperState == ElevatedHelperState.Ready)
                return Task.FromResult(CreateStatusWithoutLock());

            if (_elevatedHelperState == ElevatedHelperState.Starting)
            {
                return _pendingStartTask
                    ?? Task.FromResult(new ElevatedHelperStatus(
                        ElevatedHelperState.Failed,
                        "The elevated helper launch task is unavailable."));
            }

            if (ownerWindowHandle == IntPtr.Zero)
            {
                _elevatedHelperState = ElevatedHelperState.Failed;
                _elevatedHelperError =
                    "The Task Manager window is not ready to own the Windows approval prompt.";
                _log?.Invoke(_elevatedHelperError);
                return Task.FromResult(CreateStatusWithoutLock());
            }

            TaskCompletionSource<ElevatedHelperStatus> completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingStartCompletion = completion;
            _pendingStartTask = completion.Task;
            _elevatedHelperState = ElevatedHelperState.Starting;
            _elevatedHelperError = string.Empty;

            try
            {
                Thread launcherThread = new(() => RunElevatedHelperLaunch(ownerWindowHandle, completion))
                {
                    IsBackground = true,
                    Name = ElevationLauncherThreadName
                };
                launcherThread.SetApartmentState(ApartmentState.STA);
                launcherThread.Start();
                return completion.Task;
            }
            catch (Exception exception)
            {
                _elevatedHelperState = ElevatedHelperState.Failed;
                _elevatedHelperError =
                    $"The elevated helper launcher thread could not start: {exception.Message}";
                _pendingStartCompletion = null;
                _pendingStartTask = null;
                immediateStatus = CreateStatusWithoutLock();
                failedCompletion = completion;
            }
        }

        _log?.Invoke(immediateStatus.ErrorMessage);
        _ = failedCompletion.TrySetResult(immediateStatus);
        return failedCompletion.Task;
    }

    /// <summary>Pre-opens handles as soon as the selected process identity is known.</summary>
    public void Arm(ProcessTerminationTarget? target)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArmWithoutLock(target);
        }
    }

    /// <summary>Signals the elevated helper first, then immediately attempts the local duplicate path.</summary>
    public bool TryTerminate(ProcessTerminationTarget target, out string errorMessage)
    {
        if (target.ProcessID <= 0)
        {
            errorMessage = "The selected process cannot be terminated.";
            return false;
        }
        if (target.ProcessID == Environment.ProcessId)
        {
            errorMessage = "Task Manager cannot terminate itself from this window.";
            return false;
        }

        ElevatedKillHelperSession? helperSession;
        ElevatedKillHelperSession? unavailableHelperSession = null;
        ElevatedHelperStatus helperStatus;
        long helperRequestSequence = 0;
        bool helperRequested;
        int localTerminateError;
        bool localTerminated;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_armedTarget != target)
                ArmWithoutLock(target);

            helperSession = _helperSession;
            helperRequested = helperSession != null &&
                helperSession.TryRequestTermination(
                    target,
                    _targetGeneration,
                    out helperRequestSequence);
            if (helperSession != null && !helperRequested && !helperSession.IsReady)
            {
                unavailableHelperSession = helperSession;
                helperSession = null;
                _helperSession = null;
                _elevatedHelperState = ElevatedHelperState.Failed;
                _elevatedHelperError = "The elevated termination helper stopped unexpectedly.";
            }

            localTerminateError = _localOpenError;
            localTerminated = _localTargetHandle != IntPtr.Zero &&
                CriticalProcessActions.TryTerminateHandle(
                    _localTargetHandle,
                    out localTerminateError);
            helperStatus = CreateStatusWithoutLock();
        }

        unavailableHelperSession?.Dispose();
        if (localTerminated)
        {
            errorMessage = string.Empty;
            return true;
        }

        if (helperRequested && helperSession != null)
        {
            bool responseReceived = helperSession.TryWaitForResponse(
                helperRequestSequence,
                HelperResponseTimeoutMilliseconds,
                out int helperResult,
                out int helperError);
            if (!responseReceived)
            {
                // The fixed request slot and event were published before this wait
                errorMessage = string.Empty;
                return true;
            }
            if (helperResult == KillHelperProtocol.ResultSuccess)
            {
                errorMessage = string.Empty;
                return true;
            }

            errorMessage = CreateHelperErrorMessage(helperResult, helperError);
            return false;
        }

        string localErrorMessage = localTerminateError == 0
            ? "No local process termination path is available."
            : new Win32Exception(localTerminateError).Message;
        errorMessage = CreateUnavailableHelperErrorMessage(localErrorMessage, helperStatus);
        return false;
    }

    private void RunElevatedHelperLaunch(
        IntPtr ownerWindowHandle,
        TaskCompletionSource<ElevatedHelperStatus> completion)
    {
        ElevatedKillHelperStartResult startResult;
        try
        {
            startResult = _startElevatedHelper(ownerWindowHandle, _log);
        }
        catch (Exception exception)
        {
            string errorMessage = $"Elevated kill helper launch failed unexpectedly: {exception.Message}";
            _log?.Invoke(errorMessage);
            startResult = new ElevatedKillHelperStartResult(
                ElevatedKillHelperStartOutcome.Failed,
                null,
                errorMessage);
        }

        CompleteElevatedHelperLaunch(startResult, completion);
    }

    private void CompleteElevatedHelperLaunch(
        ElevatedKillHelperStartResult startResult,
        TaskCompletionSource<ElevatedHelperStatus> completion)
    {
        ElevatedKillHelperSession? sessionToDispose = null;
        ElevatedHelperStatus completedStatus;

        lock (_sync)
        {
            if (_disposed)
            {
                sessionToDispose = startResult.Session;
                completedStatus = CreateStatusWithoutLock();
            }
            else if (!ReferenceEquals(_pendingStartCompletion, completion))
            {
                sessionToDispose = startResult.Session;
                completedStatus = CreateStatusWithoutLock();
            }
            else
            {
                _pendingStartCompletion = null;
                _pendingStartTask = null;
                switch (startResult.Outcome)
                {
                    case ElevatedKillHelperStartOutcome.Ready when startResult.Session != null:
                        _helperSession = startResult.Session;
                        _elevatedHelperState = ElevatedHelperState.Ready;
                        _elevatedHelperError = string.Empty;
                        // Publication and arming share this lock with Arm so an older target cannot win
                        _ = _helperSession.TryArm(_armedTarget, _targetGeneration);
                        break;
                    case ElevatedKillHelperStartOutcome.Declined:
                        sessionToDispose = startResult.Session;
                        _elevatedHelperState = ElevatedHelperState.Declined;
                        _elevatedHelperError = string.IsNullOrWhiteSpace(startResult.ErrorMessage)
                            ? "Windows administrator approval was canceled."
                            : startResult.ErrorMessage;
                        break;
                    default:
                        sessionToDispose = startResult.Session;
                        _elevatedHelperState = ElevatedHelperState.Failed;
                        _elevatedHelperError = string.IsNullOrWhiteSpace(startResult.ErrorMessage)
                            ? "The elevated termination helper could not be started."
                            : startResult.ErrorMessage;
                        break;
                }

                completedStatus = CreateStatusWithoutLock();
            }
        }

        sessionToDispose?.Dispose();
        _ = completion.TrySetResult(completedStatus);
    }

    private void ArmWithoutLock(ProcessTerminationTarget? target)
    {
        if (_armedTarget == target) return;

        CloseLocalTargetHandleWithoutLock();
        _armedTarget = target;
        _targetGeneration = unchecked(_targetGeneration + 1);
        if (_targetGeneration == 0)
            _targetGeneration = 1;
        _localOpenError = 0;

        if (target is { } value)
        {
            _ = CriticalProcessActions.TryOpenTerminationHandle(
                value,
                out _localTargetHandle,
                out _localOpenError);
        }

        _ = _helperSession?.TryArm(target, _targetGeneration);
    }

    private ElevatedHelperStatus CreateStatusWithoutLock() =>
        new(_elevatedHelperState, _elevatedHelperError);

    private static string CreateHelperErrorMessage(int result, int errorCode) => result switch
    {
        KillHelperProtocol.ResultInvalidTarget => "The selected process cannot be terminated.",
        KillHelperProtocol.ResultIdentityMismatch => "The selected process exited or its PID was reused.",
        KillHelperProtocol.ResultCriticalProcess => "Windows reports that the selected process is critical.",
        _ when errorCode != 0 => new Win32Exception(errorCode).Message,
        _ => "The elevated termination helper could not terminate the selected process."
    };

    private static string CreateUnavailableHelperErrorMessage(
        string localErrorMessage,
        ElevatedHelperStatus helperStatus) => helperStatus.State switch
        {
            ElevatedHelperState.NotRequested =>
                $"{localErrorMessage} Elevated termination is not enabled.",
            ElevatedHelperState.Starting =>
                $"{localErrorMessage} Elevated termination is waiting for Windows approval.",
            ElevatedHelperState.Declined =>
                $"{localErrorMessage} Windows administrator approval was canceled.",
            ElevatedHelperState.Failed when !string.IsNullOrWhiteSpace(helperStatus.ErrorMessage) =>
                $"{localErrorMessage} {helperStatus.ErrorMessage}",
            ElevatedHelperState.Failed =>
                $"{localErrorMessage} The elevated termination helper is unavailable.",
            ElevatedHelperState.Ready =>
                $"{localErrorMessage} The elevated termination helper did not accept the request.",
            _ => localErrorMessage
        };

    private void CloseLocalTargetHandleWithoutLock()
    {
        if (_localTargetHandle == IntPtr.Zero) return;
        _ = Kernel32.CloseHandle(_localTargetHandle);
        _localTargetHandle = IntPtr.Zero;
    }

    public void Dispose()
    {
        ElevatedKillHelperSession? helperSession;
        TaskCompletionSource<ElevatedHelperStatus>? pendingStartCompletion;
        ElevatedHelperStatus disposedStatus;

        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _elevatedHelperState = ElevatedHelperState.Disposed;
            _elevatedHelperError = string.Empty;
            CloseLocalTargetHandleWithoutLock();
            helperSession = _helperSession;
            _helperSession = null;
            pendingStartCompletion = _pendingStartCompletion;
            _pendingStartCompletion = null;
            _pendingStartTask = null;
            disposedStatus = CreateStatusWithoutLock();
        }

        _ = pendingStartCompletion?.TrySetResult(disposedStatus);
        helperSession?.Dispose();
    }
}
