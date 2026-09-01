using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

internal enum ElevatedKillHelperStartOutcome
{
    Ready,
    Declined,
    Failed
}

internal readonly record struct ElevatedKillHelperStartResult(
    ElevatedKillHelperStartOutcome Outcome,
    ElevatedKillHelperSession? Session,
    string ErrorMessage);

/// <summary>Owns the fixed shared page and event-driven native helper session.</summary>
internal sealed unsafe class ElevatedKillHelperClient : IDisposable
{
    private const int ErrorCancelled = 1223;
    private const int HelperHandshakeTimeoutMilliseconds = 10_000;
    private const uint HelperResponseWaitHandleCount = 2;
    private const uint HelperProcessWaitOffset = 1;
    private const uint TerminationExitCode = 1;

    private readonly Action<string>? _log;
    private readonly Lock _sync = new();
    private IntPtr _helperProcessHandle;
    private IntPtr _mappingHandle;
    private IntPtr _mappingView;
    private IntPtr _requestEvent;
    private IntPtr _responseEvent;
    private KillHelperMailbox* _mailbox;
    private bool _mappingViewLocked;
    private bool _disposed;

    private ElevatedKillHelperClient(Action<string>? log) => _log = log;

    public bool IsReady
    {
        get
        {
            lock (_sync)
                return IsReadyWithoutLock();
        }
    }

    public int HardeningFlags
    {
        get
        {
            lock (_sync)
            {
                return _mailbox == null
                    ? 0
                    : Volatile.Read(ref _mailbox->HelperFlags);
            }
        }
    }

    /// <summary>Creates all kernel objects before launching the requested helper integrity level.</summary>
    public static ElevatedKillHelperStartResult TryStart(
        IntPtr ownerWindowHandle,
        bool elevate,
        Action<string>? log)
    {
        if (elevate && ownerWindowHandle == IntPtr.Zero)
        {
            const string ownerErrorMessage =
                "The Task Manager window is not ready to own the Windows approval prompt.";
            log?.Invoke(ownerErrorMessage);
            return new ElevatedKillHelperStartResult(
                ElevatedKillHelperStartOutcome.Failed,
                Session: null,
                ownerErrorMessage);
        }

        ElevatedKillHelperClient client = new(log);
        ElevatedKillHelperStartOutcome outcome;
        string errorMessage;
        try
        {
            outcome = client.TryInitialize(elevate, out errorMessage);
        }
        catch (Exception exception)
        {
            outcome = ElevatedKillHelperStartOutcome.Failed;
            errorMessage = $"Native kill helper initialization failed: {exception.Message}";
            log?.Invoke(errorMessage);
        }

        if (outcome == ElevatedKillHelperStartOutcome.Ready)
        {
            return new ElevatedKillHelperStartResult(
                ElevatedKillHelperStartOutcome.Ready,
                new ElevatedKillHelperSession(client),
                string.Empty);
        }

        client.Dispose();
        return new ElevatedKillHelperStartResult(outcome, Session: null, errorMessage);
    }

    /// <summary>Pre-opens the selected target in the native helper.</summary>
    public bool TryArm(ProcessTerminationTarget? target, long generation)
    {
        lock (_sync)
        {
            if (!IsReadyWithoutLock() || _mailbox == null) return false;

            ProcessTerminationTarget value = target ?? default;
            _ = Interlocked.Increment(ref _mailbox->ArmPayloadSequence);
            _mailbox->ArmProcessID = value.ProcessID;
            _mailbox->ArmCreationTime = value.CreationTimeFileTime;
            _mailbox->ArmGeneration = generation;
            _ = Interlocked.Increment(ref _mailbox->ArmPayloadSequence);
            _ = Interlocked.Increment(ref _mailbox->ArmRequestSequence);
            return KillHelperNativeMethods.SetEvent(_requestEvent);
        }
    }

    /// <summary>Publishes one fire request before any managed fallback work occurs.</summary>
    public bool TryRequestTermination(
        ProcessTerminationTarget target,
        long generation,
        out long requestSequence)
    {
        lock (_sync)
        {
            requestSequence = 0;
            if (!IsReadyWithoutLock() || _mailbox == null) return false;

            _ = Interlocked.Increment(ref _mailbox->FirePayloadSequence);
            _mailbox->FireProcessID = target.ProcessID;
            _mailbox->FireCreationTime = target.CreationTimeFileTime;
            _mailbox->FireGeneration = generation;
            _mailbox->FireExitCode = TerminationExitCode;
            _ = Interlocked.Increment(ref _mailbox->FirePayloadSequence);
            Interlocked.Exchange(ref _mailbox->FireResult, KillHelperProtocol.ResultNone);
            Interlocked.Exchange(ref _mailbox->FireError, value: 0);
            requestSequence = Interlocked.Increment(ref _mailbox->FireRequestSequence);
            return KillHelperNativeMethods.SetEvent(_requestEvent);
        }
    }

    /// <summary>Waits for a matching helper response without depending on the thread pool.</summary>
    public bool TryWaitForResponse(
        long requestSequence,
        int timeoutMilliseconds,
        out int result,
        out int errorCode)
    {
        lock (_sync)
        {
            result = KillHelperProtocol.ResultNone;
            errorCode = 0;
            if (_disposed || _mailbox == null || requestSequence <= 0) return false;

            IntPtr* waitHandles = stackalloc IntPtr[(int)HelperResponseWaitHandleCount];
            waitHandles[0] = _responseEvent;
            waitHandles[1] = _helperProcessHandle;
            long deadline = Environment.TickCount64 + timeoutMilliseconds;
            for (;;)
            {
                if (Volatile.Read(ref _mailbox->FireResponseSequence) == requestSequence)
                {
                    result = Volatile.Read(ref _mailbox->FireResult);
                    errorCode = Volatile.Read(ref _mailbox->FireError);
                    return true;
                }

                long remaining = deadline - Environment.TickCount64;
                if (remaining <= 0) return false;

                uint waitResult = KillHelperNativeMethods.WaitForMultipleObjects(
                    HelperResponseWaitHandleCount,
                    waitHandles,
                    waitForAll: false,
                    (uint)Math.Min(remaining, int.MaxValue));
                if (waitResult == Kernel32.WAIT_TIMEOUT) return false;
                if (waitResult == Kernel32.WAIT_OBJECT_0 + HelperProcessWaitOffset) return false;
                if (waitResult != Kernel32.WAIT_OBJECT_0) return false;
            }
        }
    }

    private ElevatedKillHelperStartOutcome TryInitialize(bool elevate, out string errorMessage)
    {
        errorMessage = string.Empty;
#if TASK_MANAGER_NATIVE_AOT
        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            errorMessage = "Task Manager could not determine its executable path for native termination.";
            _log?.Invoke(errorMessage);
            return ElevatedKillHelperStartOutcome.Failed;
        }

        string helperPath = processPath;
        string argumentPrefix = Constants.KillHelperModeArgument + " ";
#else
        string helperPath = Path.Combine(AppContext.BaseDirectory, Constants.KillHelperFileName);
        const string argumentPrefix = "";
#endif
        if (!File.Exists(helperPath))
        {
            errorMessage = $"Native termination executable was not found: {helperPath}";
            _log?.Invoke(errorMessage);
            return ElevatedKillHelperStartOutcome.Failed;
        }

        _mappingHandle = KillHelperNativeMethods.CreateFileMappingW(
            KillHelperNativeMethods.InvalidHandleValue,
            IntPtr.Zero,
            KillHelperNativeMethods.PageReadWrite,
            maximumSizeHigh: 0,
            KillHelperProtocol.MailboxSize,
            name: null);
        if (_mappingHandle == IntPtr.Zero)
        {
            errorMessage = LogWin32Failure("CreateFileMapping");
            return ElevatedKillHelperStartOutcome.Failed;
        }

        _mappingView = KillHelperNativeMethods.MapViewOfFile(
            _mappingHandle,
            KillHelperNativeMethods.FileMapAllAccess,
            fileOffsetHigh: 0,
            fileOffsetLow: 0,
            KillHelperProtocol.MailboxSize);
        if (_mappingView == IntPtr.Zero)
        {
            errorMessage = LogWin32Failure("MapViewOfFile");
            return ElevatedKillHelperStartOutcome.Failed;
        }

        lock (_sync)
        {
            _mailbox = (KillHelperMailbox*)_mappingView;
            _mailbox->Magic = KillHelperProtocol.MailboxMagic;
            _mailbox->Version = KillHelperProtocol.ProtocolVersion;
            _mailbox->HelperState = KillHelperProtocol.StateStarting;
            _mailbox->ParentProcessID = (uint)Environment.ProcessId;
        }

        _mappingViewLocked = KillHelperNativeMethods.VirtualLock(
            _mappingView,
            KillHelperProtocol.MailboxSize);
        if (!_mappingViewLocked)
            LogWin32Failure("VirtualLock managed mailbox");

        _requestEvent = KillHelperNativeMethods.CreateEventW(
            IntPtr.Zero,
            isManualReset: false,
            initialState: false,
            name: null);
        if (_requestEvent == IntPtr.Zero)
        {
            errorMessage = LogWin32Failure("CreateEvent request");
            return ElevatedKillHelperStartOutcome.Failed;
        }

        _responseEvent = KillHelperNativeMethods.CreateEventW(
            IntPtr.Zero,
            isManualReset: false,
            initialState: false,
            name: null);
        if (_responseEvent == IntPtr.Zero)
        {
            errorMessage = LogWin32Failure("CreateEvent response");
            return ElevatedKillHelperStartOutcome.Failed;
        }

        string arguments = string.Concat(
            argumentPrefix,
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            " ",
            _mappingHandle.ToInt64().ToString(format: "X", CultureInfo.InvariantCulture),
            " ",
            _requestEvent.ToInt64().ToString(format: "X", CultureInfo.InvariantCulture),
            " ",
            _responseEvent.ToInt64().ToString(format: "X", CultureInfo.InvariantCulture));
        string? launchVerb = elevate ? "runas" : null;
        if (!ExplorerProcessLauncher.TryShellExecute(
                helperPath,
                arguments,
                workingDirectory: AppContext.BaseDirectory,
                verb: launchVerb,
                out int launchError,
                out string launchErrorMessage))
        {
            bool wasCancelled = elevate && launchError == ErrorCancelled;
            errorMessage = wasCancelled
                ? "Windows administrator approval was canceled."
                : $"Native kill helper launch failed: {launchErrorMessage}";
            _log?.Invoke(errorMessage);
            return wasCancelled
                ? ElevatedKillHelperStartOutcome.Declined
                : ElevatedKillHelperStartOutcome.Failed;
        }

        uint startupWait = Kernel32.WaitForSingleObject(
            _responseEvent,
            HelperHandshakeTimeoutMilliseconds);
        if (startupWait != Kernel32.WAIT_OBJECT_0)
        {
            errorMessage = startupWait == Kernel32.WAIT_TIMEOUT
                ? "The native kill helper did not complete its startup handshake within 10 seconds."
                : $"Native kill helper startup wait failed: 0x{startupWait:X8}";
            _log?.Invoke(errorMessage);
            return ElevatedKillHelperStartOutcome.Failed;
        }

        lock (_sync)
        {
            int helperState = Volatile.Read(ref _mailbox->HelperState);
            if (helperState != KillHelperProtocol.StateReady)
            {
                int startupError = Volatile.Read(ref _mailbox->HelperStartupError);
                string startupMessage = startupError == 0
                    ? $"helper state {helperState} did not become ready"
                    : new Win32Exception(startupError).Message;
                errorMessage = $"Native kill helper startup failed: {startupMessage}";
                _log?.Invoke(errorMessage);
                return ElevatedKillHelperStartOutcome.Failed;
            }

            uint helperProcessID = _mailbox->HelperProcessID;
            _helperProcessHandle = Kernel32.OpenProcess(
                Kernel32.SYNCHRONIZE | Kernel32.PROCESS_QUERY_LIMITED_INFORMATION,
                bInheritHandle: false,
                helperProcessID);
            if (_helperProcessHandle == IntPtr.Zero)
            {
                errorMessage = helperProcessID == 0
                    ? "Native kill helper did not publish its process ID."
                    : $"Native kill helper PID {helperProcessID} could not be opened: " +
                      new Win32Exception(Marshal.GetLastWin32Error()).Message;
                _log?.Invoke(errorMessage);
                return ElevatedKillHelperStartOutcome.Failed;
            }

            LogHelperHardeningState(Volatile.Read(ref _mailbox->HelperFlags), elevate);
        }
        return ElevatedKillHelperStartOutcome.Ready;
    }

    private bool IsReadyWithoutLock() =>
        !_disposed &&
        _mailbox != null &&
        Volatile.Read(ref _mailbox->HelperState) == KillHelperProtocol.StateReady &&
        IsHelperProcessAlive();

    private bool IsHelperProcessAlive()
    {
        if (_helperProcessHandle == IntPtr.Zero) return false;
        return Kernel32.WaitForSingleObject(_helperProcessHandle, dwMilliseconds: 0) == Kernel32.WAIT_TIMEOUT;
    }

    private void LogHelperHardeningState(int flags, bool elevated)
    {
        int requiredFlags = elevated
            ? KillHelperProtocol.RequiredHardeningFlags
            : KillHelperProtocol.RequiredReliabilityFlags;
        string integrityDescription = elevated ? "elevated" : "standard";
        if ((flags & requiredFlags) == requiredFlags)
        {
            lock (_sync)
            {
                _log?.Invoke(
                    $"Native kill helper ready at {integrityDescription} integrity, " +
                    $"PID {_mailbox->HelperProcessID}; hardening 0x{flags:X8}.");
            }

            return;
        }

        _log?.Invoke(
            $"Native kill helper ready at {integrityDescription} integrity with partial hardening " +
            $"0x{flags:X8}, expected 0x{requiredFlags:X8}.");
    }

    private string LogWin32Failure(string operation)
    {
        int errorCode = Marshal.GetLastWin32Error();
        string errorMessage = $"{operation} failed: {new Win32Exception(errorCode).Message}";
        _log?.Invoke(errorMessage);
        return errorMessage;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;

            if (_mailbox != null)
            {
                _ = Interlocked.Or(ref _mailbox->ControlFlags, KillHelperProtocol.ControlShutdown);
                if (_requestEvent != IntPtr.Zero)
                    _ = KillHelperNativeMethods.SetEvent(_requestEvent);
            }

            CloseNativeHandle(ref _helperProcessHandle);

            if (_mappingViewLocked)
            {
                _ = KillHelperNativeMethods.VirtualUnlock(_mappingView, KillHelperProtocol.MailboxSize);
                _mappingViewLocked = false;
            }

            if (_mappingView != IntPtr.Zero)
            {
                _ = KillHelperNativeMethods.UnmapViewOfFile(_mappingView);
                _mappingView = IntPtr.Zero;
                _mailbox = null;
            }

            CloseNativeHandle(ref _responseEvent);
            CloseNativeHandle(ref _requestEvent);
            CloseNativeHandle(ref _mappingHandle);
        }
    }

    private static void CloseNativeHandle(ref IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        _ = Kernel32.CloseHandle(handle);
        handle = IntPtr.Zero;
    }
}
