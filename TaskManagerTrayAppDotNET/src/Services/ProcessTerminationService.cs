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

internal delegate ElevatedKillHelperStartResult StartKillHelperAction(
    IntPtr ownerWindowHandle,
    bool elevate,
    Action<string>? log);

/// <summary>Routes termination through a native helper with a pre-opened managed fallback.</summary>
internal sealed class ProcessTerminationService : IDisposable
{
    private const int HelperResponseTimeoutMilliseconds = 1_000;
    private const string ElevationLauncherThreadName = "Task Manager elevation launcher";
    private const string StandardLauncherThreadName = "Task Manager native helper launcher";

    private readonly Action<string>? _log;
    private readonly StartKillHelperAction _startKillHelper;
    private readonly Lock _sync = new();
    private ElevatedKillHelperSession? _helperSession;
    private TaskCompletionSource<ElevatedHelperStatus>? _pendingElevationCompletion;
    private Task<ElevatedHelperStatus>? _pendingElevationTask;
    private TaskCompletionSource<bool>? _pendingStandardCompletion;
    private Task<bool>? _pendingStandardTask;
    private ProcessTerminationTarget? _armedTarget;
    private IntPtr _localTargetHandle;
    private long _targetGeneration;
    private int _localOpenError;
    private ElevatedHelperState _elevatedHelperState = ElevatedHelperState.NotRequested;
    private string _elevatedHelperError = string.Empty;
    private string _nativeHelperError = string.Empty;
    private bool _helperIsElevated;
    private bool _disposed;

    public ProcessTerminationService(Action<string>? log)
        : this(log, ElevatedKillHelperClient.TryStart)
    {
    }

    internal ProcessTerminationService(
        Action<string>? log,
        StartKillHelperAction startKillHelper)
    {
        ArgumentNullException.ThrowIfNull(startKillHelper);
        _log = log;
        _startKillHelper = startKillHelper;
    }

    /// <summary>Returns the current optional elevated-helper capability state.</summary>
    public ElevatedHelperStatus GetElevatedHelperStatus()
    {
        ElevatedKillHelperSession? unavailableSession = null;
        ElevatedHelperStatus status;
        bool shouldStartStandardHelper = false;

        lock (_sync)
        {
            if (!_disposed && _helperSession != null && !_helperSession.IsReady)
            {
                unavailableSession = DetachUnavailableHelperWithoutLock(
                    "The native termination helper stopped unexpectedly.");
                shouldStartStandardHelper = true;
            }

            status = CreateStatusWithoutLock();
        }

        unavailableSession?.Dispose();
        if (shouldStartStandardHelper)
            _ = EnsureStandardHelperAsync();
        return status;
    }

    /// <summary>Ensures a standard-integrity native helper is available without displaying UAC.</summary>
    public Task<bool> EnsureStandardHelperAsync()
    {
        ElevatedKillHelperSession? unavailableSession = null;
        TaskCompletionSource<bool>? failedCompletion = null;
        Task<bool> resultTask;
        string launchError = string.Empty;

        lock (_sync)
        {
            if (_disposed)
                return Task.FromResult(false);

            if (_helperSession != null && !_helperSession.IsReady)
            {
                unavailableSession = DetachUnavailableHelperWithoutLock(
                    "The native termination helper stopped unexpectedly.");
            }

            if (_helperSession != null)
            {
                resultTask = Task.FromResult(true);
            }
            else if (_pendingStandardTask != null)
            {
                resultTask = _pendingStandardTask;
            }
            else
            {
                TaskCompletionSource<bool> completion = new(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingStandardCompletion = completion;
                _pendingStandardTask = completion.Task;
                resultTask = completion.Task;

                try
                {
                    Thread launcherThread = new(() => RunStandardHelperLaunch(completion))
                    {
                        IsBackground = true,
                        Name = StandardLauncherThreadName
                    };
                    launcherThread.SetApartmentState(ApartmentState.STA);
                    launcherThread.Start();
                }
                catch (Exception exception)
                {
                    launchError =
                        $"The native helper launcher thread could not start: {exception.Message}";
                    _nativeHelperError = launchError;
                    _pendingStandardCompletion = null;
                    _pendingStandardTask = null;
                    failedCompletion = completion;
                }
            }
        }

        unavailableSession?.Dispose();
        if (failedCompletion != null)
        {
            _log?.Invoke(launchError);
            _ = failedCompletion.TrySetResult(false);
        }

        return resultTask;
    }

    /// <summary>Starts one owner-parented elevation attempt on a dedicated STA thread.</summary>
    public Task<ElevatedHelperStatus> EnableElevatedHelperAsync(IntPtr ownerWindowHandle)
    {
        ElevatedKillHelperSession? unavailableSession = null;
        TaskCompletionSource<ElevatedHelperStatus>? failedCompletion = null;
        Task<ElevatedHelperStatus> resultTask;
        ElevatedHelperStatus failedStatus = default;

        lock (_sync)
        {
            if (!_disposed && _helperSession != null && !_helperSession.IsReady)
            {
                unavailableSession = DetachUnavailableHelperWithoutLock(
                    "The native termination helper stopped unexpectedly.");
            }

            if (_disposed ||
                (_helperIsElevated && _helperSession != null && _helperSession.IsReady))
            {
                resultTask = Task.FromResult(CreateStatusWithoutLock());
            }
            else if (_elevatedHelperState == ElevatedHelperState.Starting)
            {
                resultTask = _pendingElevationTask
                             ?? Task.FromResult(new ElevatedHelperStatus(
                                 ElevatedHelperState.Failed,
                                 ErrorMessage: "The elevated helper launch task is unavailable."));
            }
            else if (ownerWindowHandle == IntPtr.Zero)
            {
                _elevatedHelperState = ElevatedHelperState.Failed;
                _elevatedHelperError =
                    "The Task Manager window is not ready to own the Windows approval prompt.";
                resultTask = Task.FromResult(CreateStatusWithoutLock());
                failedStatus = CreateStatusWithoutLock();
            }
            else
            {
                TaskCompletionSource<ElevatedHelperStatus> completion = new(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingElevationCompletion = completion;
                _pendingElevationTask = completion.Task;
                _elevatedHelperState = ElevatedHelperState.Starting;
                _elevatedHelperError = string.Empty;
                resultTask = completion.Task;

                try
                {
                    Thread launcherThread = new(
                        () => RunElevatedHelperLaunch(ownerWindowHandle, completion))
                    {
                        IsBackground = true,
                        Name = ElevationLauncherThreadName
                    };
                    launcherThread.SetApartmentState(ApartmentState.STA);
                    launcherThread.Start();
                }
                catch (Exception exception)
                {
                    _elevatedHelperState = ElevatedHelperState.Failed;
                    _elevatedHelperError =
                        $"The elevated helper launcher thread could not start: {exception.Message}";
                    _pendingElevationCompletion = null;
                    _pendingElevationTask = null;
                    failedStatus = CreateStatusWithoutLock();
                    failedCompletion = completion;
                }
            }
        }

        unavailableSession?.Dispose();
        if (!string.IsNullOrWhiteSpace(failedStatus.ErrorMessage))
            _log?.Invoke(failedStatus.ErrorMessage);
        if (failedCompletion != null)
            _ = failedCompletion.TrySetResult(failedStatus);
        return resultTask;
    }

    /// <summary>Pre-opens native and managed handles as soon as the target identity is known.</summary>
    public void Arm(ProcessTerminationTarget? target)
    {
        ElevatedKillHelperSession? unavailableSession = null;
        bool shouldStartStandardHelper = false;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            bool targetChanged = SetArmedTargetWithoutLock(target);

            if (_helperSession != null && !_helperSession.IsReady)
            {
                unavailableSession = DetachUnavailableHelperWithoutLock(
                    "The native termination helper stopped unexpectedly.");
                shouldStartStandardHelper = true;
            }
            else if (targetChanged &&
                     _helperSession != null &&
                     !_helperSession.TryArm(target, _targetGeneration) &&
                     !_helperSession.IsReady)
            {
                unavailableSession = DetachUnavailableHelperWithoutLock(
                    "The native termination helper stopped while arming a target.");
                shouldStartStandardHelper = true;
            }
            else if (_helperSession == null)
            {
                shouldStartStandardHelper = true;
            }
        }

        unavailableSession?.Dispose();
        if (shouldStartStandardHelper)
            _ = EnsureStandardHelperAsync();
    }

    /// <summary>Uses the native helper first and falls back to the pre-opened managed handle.</summary>
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

        ElevatedKillHelperSession? unavailableSession = null;
        ElevatedHelperStatus elevationStatus;
        string nativeHelperError;
        bool shouldStartStandardHelper = false;
        bool helperRequestPublished = false;
        bool helperResponseReceived = false;
        int helperResult = KillHelperProtocol.ResultNone;
        int helperError = 0;
        int localTerminateError;
        bool localTerminated = false;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            bool targetChanged = SetArmedTargetWithoutLock(target);

            if (_helperSession != null && !_helperSession.IsReady)
            {
                unavailableSession = DetachUnavailableHelperWithoutLock(
                    "The native termination helper stopped unexpectedly.");
                shouldStartStandardHelper = true;
            }
            else if (targetChanged &&
                     _helperSession != null &&
                     !_helperSession.TryArm(target, _targetGeneration) &&
                     !_helperSession.IsReady)
            {
                unavailableSession = DetachUnavailableHelperWithoutLock(
                    "The native termination helper stopped while arming a target.");
                shouldStartStandardHelper = true;
            }

            if (_helperSession != null)
            {
                helperRequestPublished = _helperSession.TryRequestTermination(
                    target,
                    _targetGeneration,
                    out long helperRequestSequence);
                if (helperRequestPublished)
                {
                    helperResponseReceived = _helperSession.TryWaitForResponse(
                        helperRequestSequence,
                        HelperResponseTimeoutMilliseconds,
                        out helperResult,
                        out helperError);
                }

                if ((!helperRequestPublished || !helperResponseReceived) &&
                    !_helperSession.IsReady)
                {
                    unavailableSession ??= DetachUnavailableHelperWithoutLock(
                        "The native termination helper stopped during a termination request.");
                    shouldStartStandardHelper = true;
                }
            }
            else
            {
                shouldStartStandardHelper = true;
            }

            bool helperTerminated = helperResponseReceived &&
                                    helperResult == KillHelperProtocol.ResultSuccess;
            localTerminateError = _localOpenError;
            if (!helperTerminated)
            {
                localTerminated = _localTargetHandle != IntPtr.Zero &&
                                  CriticalProcessActions.TryTerminateHandle(
                                      _localTargetHandle,
                                      out localTerminateError);
            }

            elevationStatus = CreateStatusWithoutLock();
            nativeHelperError = _nativeHelperError;
        }

        unavailableSession?.Dispose();
        if (shouldStartStandardHelper)
            _ = EnsureStandardHelperAsync();

        if (helperResponseReceived && helperResult == KillHelperProtocol.ResultSuccess)
        {
            errorMessage = string.Empty;
            return true;
        }

        if (localTerminated)
        {
            errorMessage = string.Empty;
            return true;
        }

        // A published request can still complete after a timeout on a severely loaded system
        if (helperRequestPublished && !helperResponseReceived)
        {
            errorMessage = string.Empty;
            return true;
        }

        if (helperResponseReceived)
        {
            errorMessage = CreateHelperErrorMessage(helperResult, helperError);
            return false;
        }

        string localErrorMessage = localTerminateError == 0
            ? "No managed process termination path is available."
            : new Win32Exception(localTerminateError).Message;
        errorMessage = CreateUnavailableHelperErrorMessage(
            localErrorMessage,
            nativeHelperError,
            elevationStatus);
        return false;
    }

    private void RunStandardHelperLaunch(TaskCompletionSource<bool> completion)
    {
        ElevatedKillHelperStartResult startResult;
        try
        {
            startResult = _startKillHelper(IntPtr.Zero, elevate: false, log: _log);
        }
        catch (Exception exception)
        {
            string errorMessage = $"Native kill helper launch failed unexpectedly: {exception.Message}";
            _log?.Invoke(errorMessage);
            startResult = new ElevatedKillHelperStartResult(
                ElevatedKillHelperStartOutcome.Failed,
                Session: null,
                errorMessage);
        }

        CompleteStandardHelperLaunch(startResult, completion);
    }

    private void CompleteStandardHelperLaunch(
        ElevatedKillHelperStartResult startResult,
        TaskCompletionSource<bool> completion)
    {
        ElevatedKillHelperSession? existingSessionToDispose = null;
        ElevatedKillHelperSession? startSessionToDispose = null;
        bool helperReady;

        lock (_sync)
        {
            if (_disposed || !ReferenceEquals(_pendingStandardCompletion, completion))
            {
                startSessionToDispose = startResult.Session;
                helperReady = false;
            }
            else
            {
                _pendingStandardCompletion = null;
                _pendingStandardTask = null;

                if (_helperSession != null && !_helperSession.IsReady)
                {
                    existingSessionToDispose = DetachUnavailableHelperWithoutLock(
                        "The native termination helper stopped unexpectedly.");
                }

                if (startResult.Outcome == ElevatedKillHelperStartOutcome.Ready &&
                    startResult.Session != null &&
                    _helperSession == null)
                {
                    _helperSession = startResult.Session;
                    _helperIsElevated = false;
                    _nativeHelperError = string.Empty;
                    _ = _helperSession.TryArm(_armedTarget, _targetGeneration);
                    helperReady = _helperSession.IsReady;
                    if (!helperReady)
                    {
                        startSessionToDispose = DetachUnavailableHelperWithoutLock(
                            "The standard native termination helper stopped during startup.");
                    }
                }
                else
                {
                    startSessionToDispose = startResult.Session;
                    helperReady = _helperSession?.IsReady == true;
                    if (!helperReady)
                    {
                        _nativeHelperError = string.IsNullOrWhiteSpace(startResult.ErrorMessage)
                            ? "The standard native termination helper could not be started."
                            : startResult.ErrorMessage;
                    }
                }
            }
        }

        existingSessionToDispose?.Dispose();
        if (!ReferenceEquals(startSessionToDispose, existingSessionToDispose))
            startSessionToDispose?.Dispose();
        _ = completion.TrySetResult(helperReady);
    }

    private void RunElevatedHelperLaunch(
        IntPtr ownerWindowHandle,
        TaskCompletionSource<ElevatedHelperStatus> completion)
    {
        ElevatedKillHelperStartResult startResult;
        try
        {
            startResult = _startKillHelper(ownerWindowHandle, elevate: true, log: _log);
        }
        catch (Exception exception)
        {
            string errorMessage = $"Elevated kill helper launch failed unexpectedly: {exception.Message}";
            _log?.Invoke(errorMessage);
            startResult = new ElevatedKillHelperStartResult(
                ElevatedKillHelperStartOutcome.Failed,
                Session: null,
                errorMessage);
        }

        CompleteElevatedHelperLaunch(startResult, completion);
    }

    private void CompleteElevatedHelperLaunch(
        ElevatedKillHelperStartResult startResult,
        TaskCompletionSource<ElevatedHelperStatus> completion)
    {
        ElevatedKillHelperSession? existingSessionToDispose = null;
        ElevatedKillHelperSession? startSessionToDispose = null;
        ElevatedHelperStatus completedStatus;
        bool shouldStartStandardHelper = false;

        lock (_sync)
        {
            if (_disposed || !ReferenceEquals(_pendingElevationCompletion, completion))
            {
                startSessionToDispose = startResult.Session;
                completedStatus = CreateStatusWithoutLock();
            }
            else
            {
                _pendingElevationCompletion = null;
                _pendingElevationTask = null;
                switch (startResult.Outcome)
                {
                    case ElevatedKillHelperStartOutcome.Ready when startResult.Session != null:
                        existingSessionToDispose = _helperSession;
                        _helperSession = startResult.Session;
                        _helperIsElevated = true;
                        _nativeHelperError = string.Empty;
                        _elevatedHelperState = ElevatedHelperState.Ready;
                        _elevatedHelperError = string.Empty;
                        _ = _helperSession.TryArm(_armedTarget, _targetGeneration);
                        if (!_helperSession.IsReady)
                        {
                            startSessionToDispose = DetachUnavailableHelperWithoutLock(
                                "The elevated native termination helper stopped during startup.");
                            shouldStartStandardHelper = true;
                        }
                        break;
                    case ElevatedKillHelperStartOutcome.Declined:
                        startSessionToDispose = startResult.Session;
                        _elevatedHelperState = ElevatedHelperState.Declined;
                        _elevatedHelperError = string.IsNullOrWhiteSpace(startResult.ErrorMessage)
                            ? "Windows administrator approval was canceled."
                            : startResult.ErrorMessage;
                        shouldStartStandardHelper = _helperSession?.IsReady != true;
                        break;
                    default:
                        startSessionToDispose = startResult.Session;
                        _elevatedHelperState = ElevatedHelperState.Failed;
                        _elevatedHelperError = string.IsNullOrWhiteSpace(startResult.ErrorMessage)
                            ? "The elevated termination helper could not be started."
                            : startResult.ErrorMessage;
                        shouldStartStandardHelper = _helperSession?.IsReady != true;
                        break;
                }

                completedStatus = CreateStatusWithoutLock();
            }
        }

        if (!ReferenceEquals(existingSessionToDispose, startResult.Session))
            existingSessionToDispose?.Dispose();
        if (!ReferenceEquals(startSessionToDispose, existingSessionToDispose))
            startSessionToDispose?.Dispose();
        _ = completion.TrySetResult(completedStatus);
        if (shouldStartStandardHelper)
            _ = EnsureStandardHelperAsync();
    }

    private bool SetArmedTargetWithoutLock(ProcessTerminationTarget? target)
    {
        if (_armedTarget == target) return false;

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

        return true;
    }

    private ElevatedKillHelperSession? DetachUnavailableHelperWithoutLock(string errorMessage)
    {
        ElevatedKillHelperSession? unavailableSession = _helperSession;
        if (unavailableSession == null) return null;

        bool wasElevated = _helperIsElevated;
        _helperSession = null;
        _helperIsElevated = false;
        _nativeHelperError = errorMessage;
        if (wasElevated && !_disposed)
        {
            _elevatedHelperState = ElevatedHelperState.Failed;
            _elevatedHelperError = errorMessage;
        }

        return unavailableSession;
    }

    private ElevatedHelperStatus CreateStatusWithoutLock() =>
        new(_elevatedHelperState, _elevatedHelperError);

    private static string CreateHelperErrorMessage(int result, int errorCode) => result switch
    {
        KillHelperProtocol.ResultInvalidTarget => "The selected process cannot be terminated.",
        KillHelperProtocol.ResultIdentityMismatch => "The selected process exited or its PID was reused.",
        KillHelperProtocol.ResultCriticalProcess => "Windows reports that the selected process is critical.",
        _ when errorCode != 0 => new Win32Exception(errorCode).Message,
        _ => "The native termination helper could not terminate the selected process."
    };

    private static string CreateUnavailableHelperErrorMessage(
        string localErrorMessage,
        string nativeHelperError,
        ElevatedHelperStatus elevationStatus)
    {
        if (!string.IsNullOrWhiteSpace(nativeHelperError))
            return $"{localErrorMessage} {nativeHelperError}";

        return elevationStatus.State switch
        {
            ElevatedHelperState.Starting =>
                $"{localErrorMessage} The native helper is waiting for Windows approval.",
            ElevatedHelperState.Declined =>
                $"{localErrorMessage} The native helper is unavailable at standard integrity, and " +
                "Windows administrator approval was canceled.",
            ElevatedHelperState.Failed when !string.IsNullOrWhiteSpace(elevationStatus.ErrorMessage) =>
                $"{localErrorMessage} {elevationStatus.ErrorMessage}",
            ElevatedHelperState.Disposed => localErrorMessage,
            _ => $"{localErrorMessage} The native termination helper is unavailable."
        };
    }

    private void CloseLocalTargetHandleWithoutLock()
    {
        if (_localTargetHandle == IntPtr.Zero) return;
        _ = Kernel32.CloseHandle(_localTargetHandle);
        _localTargetHandle = IntPtr.Zero;
    }

    public void Dispose()
    {
        ElevatedKillHelperSession? helperSession;
        TaskCompletionSource<ElevatedHelperStatus>? pendingElevationCompletion;
        TaskCompletionSource<bool>? pendingStandardCompletion;
        ElevatedHelperStatus disposedStatus;

        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _elevatedHelperState = ElevatedHelperState.Disposed;
            _elevatedHelperError = string.Empty;
            _nativeHelperError = string.Empty;
            CloseLocalTargetHandleWithoutLock();
            helperSession = _helperSession;
            _helperSession = null;
            _helperIsElevated = false;
            pendingElevationCompletion = _pendingElevationCompletion;
            _pendingElevationCompletion = null;
            _pendingElevationTask = null;
            pendingStandardCompletion = _pendingStandardCompletion;
            _pendingStandardCompletion = null;
            _pendingStandardTask = null;
            disposedStatus = CreateStatusWithoutLock();
        }

        _ = pendingElevationCompletion?.TrySetResult(disposedStatus);
        _ = pendingStandardCompletion?.TrySetResult(false);
        helperSession?.Dispose();
    }
}
