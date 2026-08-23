using System.ComponentModel;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Coordinates redundant direct and pre-armed elevated termination paths.</summary>
internal sealed class ProcessTerminationService : IDisposable
{
    private const int HelperResponseTimeoutMilliseconds = 1_000;

    private readonly ElevatedKillHelperClient? _helperClient;
    private ProcessTerminationTarget? _armedTarget;
    private IntPtr _localTargetHandle;
    private long _targetGeneration;
    private int _localOpenError;
    private bool _disposed;

    public ProcessTerminationService(Action<string>? log) =>
        _helperClient = ElevatedKillHelperClient.TryStart(log);

    /// <summary>Pre-opens handles as soon as the selected process identity is known.</summary>
    public void Arm(ProcessTerminationTarget? target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_armedTarget == target) return;

        CloseLocalTargetHandle();
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

        _ = _helperClient?.TryArm(target, _targetGeneration);
    }

    /// <summary>Signals the elevated helper first, then immediately attempts the local duplicate path.</summary>
    public bool TryTerminate(ProcessTerminationTarget target, out string errorMessage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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

        if (_armedTarget != target)
            Arm(target);

        long helperRequestSequence = 0;
        bool helperRequested = _helperClient != null &&
            _helperClient.TryRequestTermination(
                target,
                _targetGeneration,
                out helperRequestSequence);

        int localTerminateError = _localOpenError;
        if (_localTargetHandle != IntPtr.Zero &&
            CriticalProcessActions.TryTerminateHandle(_localTargetHandle, out localTerminateError))
        {
            errorMessage = string.Empty;
            return true;
        }

        if (helperRequested && _helperClient != null)
        {
            bool responseReceived = _helperClient.TryWaitForResponse(
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

        errorMessage = localTerminateError == 0
            ? "No process termination path is available."
            : new Win32Exception(localTerminateError).Message;
        return false;
    }

    private static string CreateHelperErrorMessage(int result, int errorCode) => result switch
    {
        KillHelperProtocol.ResultInvalidTarget => "The selected process cannot be terminated.",
        KillHelperProtocol.ResultIdentityMismatch => "The selected process exited or its PID was reused.",
        KillHelperProtocol.ResultCriticalProcess => "Windows reports that the selected process is critical.",
        _ when errorCode != 0 => new Win32Exception(errorCode).Message,
        _ => "The elevated termination helper could not terminate the selected process."
    };

    private void CloseLocalTargetHandle()
    {
        if (_localTargetHandle == IntPtr.Zero) return;
        _ = Kernel32.CloseHandle(_localTargetHandle);
        _localTargetHandle = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseLocalTargetHandle();
        _helperClient?.Dispose();
    }
}
