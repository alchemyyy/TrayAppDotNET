using System.ComponentModel;
using System.Diagnostics;
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

/// <summary>Owns the fixed shared page and event-driven elevated helper session.</summary>
internal sealed unsafe class ElevatedKillHelperClient : IDisposable
{
    private const int ErrorCancelled = 1223;
    private const int HelperHandshakeTimeoutMilliseconds = 10_000;
    private const uint TerminationExitCode = 1;

    private readonly Action<string>? _log;
    private readonly Lock _sync = new();
    private Process? _helperProcess;
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

    /// <summary>Creates all kernel objects before crossing the UAC elevation boundary.</summary>
    public static ElevatedKillHelperStartResult TryStart(IntPtr ownerWindowHandle, Action<string>? log)
    {
        if (ownerWindowHandle == IntPtr.Zero)
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
            outcome = client.TryInitialize(ownerWindowHandle, out errorMessage);
        }
        catch (Exception exception)
        {
            outcome = ElevatedKillHelperStartOutcome.Failed;
            errorMessage = $"Elevated kill helper initialization failed: {exception.Message}";
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

    /// <summary>Pre-opens the selected target in the elevated helper.</summary>
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

                uint waitResult = Kernel32.WaitForSingleObject(
                    _responseEvent,
                    (uint)Math.Min(remaining, int.MaxValue));
                if (waitResult == Kernel32.WAIT_TIMEOUT) return false;
                if (waitResult != Kernel32.WAIT_OBJECT_0) return false;
            }
        }
    }

    internal static ProcessStartInfo CreateStartInfo(
        string helperPath,
        string arguments,
        IntPtr ownerWindowHandle) =>
        new()
        {
            FileName = helperPath,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Normal,
            ErrorDialog = true,
            ErrorDialogParentHandle = ownerWindowHandle
        };

    private ElevatedKillHelperStartOutcome TryInitialize(
        IntPtr ownerWindowHandle,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        string helperPath = Path.Combine(AppContext.BaseDirectory, Constants.KillHelperFileName);
        if (!File.Exists(helperPath))
        {
            errorMessage = $"Elevated kill helper was not found: {helperPath}";
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

        _mailbox = (KillHelperMailbox*)_mappingView;
        _mailbox->Magic = KillHelperProtocol.MailboxMagic;
        _mailbox->Version = KillHelperProtocol.ProtocolVersion;
        _mailbox->HelperState = KillHelperProtocol.StateStarting;
        _mailbox->ParentProcessID = (uint)Environment.ProcessId;
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
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            " ",
            _mappingHandle.ToInt64().ToString(format: "X", CultureInfo.InvariantCulture),
            " ",
            _requestEvent.ToInt64().ToString(format: "X", CultureInfo.InvariantCulture),
            " ",
            _responseEvent.ToInt64().ToString(format: "X", CultureInfo.InvariantCulture));
        ProcessStartInfo startInfo = CreateStartInfo(helperPath, arguments, ownerWindowHandle);

        try
        {
            _helperProcess = Process.Start(startInfo);
            if (_helperProcess == null)
            {
                errorMessage = "Windows did not create the elevated kill helper process.";
                _log?.Invoke(errorMessage);
                return ElevatedKillHelperStartOutcome.Failed;
            }

            _helperProcessHandle = _helperProcess.Handle;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            errorMessage = "Windows administrator approval was canceled.";
            _log?.Invoke(errorMessage);
            return ElevatedKillHelperStartOutcome.Declined;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            errorMessage = $"Elevated kill helper launch failed: {exception.Message}";
            _log?.Invoke(errorMessage);
            return ElevatedKillHelperStartOutcome.Failed;
        }

        uint startupWait = Kernel32.WaitForSingleObject(
            _responseEvent,
            HelperHandshakeTimeoutMilliseconds);
        if (startupWait != Kernel32.WAIT_OBJECT_0)
        {
            errorMessage = startupWait == Kernel32.WAIT_TIMEOUT
                ? "The elevated kill helper did not complete its startup handshake within 10 seconds."
                : $"Elevated kill helper startup wait failed: 0x{startupWait:X8}";
            _log?.Invoke(errorMessage);
            return ElevatedKillHelperStartOutcome.Failed;
        }

        int helperState = Volatile.Read(ref _mailbox->HelperState);
        if (helperState != KillHelperProtocol.StateReady)
        {
            int startupError = Volatile.Read(ref _mailbox->HelperStartupError);
            string startupMessage = startupError == 0
                ? $"helper state {helperState} did not become ready"
                : new Win32Exception(startupError).Message;
            errorMessage = $"Elevated kill helper startup failed: {startupMessage}";
            _log?.Invoke(errorMessage);
            return ElevatedKillHelperStartOutcome.Failed;
        }

        LogHelperHardeningState(Volatile.Read(ref _mailbox->HelperFlags));
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

    private void LogHelperHardeningState(int flags)
    {
        const int requiredFlags = KillHelperProtocol.RequiredHardeningFlags;
        if ((flags & requiredFlags) == requiredFlags)
        {
            _log?.Invoke($"Elevated kill helper ready, PID {_mailbox->HelperProcessID}; hardening 0x{flags:X8}.");
            return;
        }

        _log?.Invoke(
            $"Elevated kill helper ready with partial hardening 0x{flags:X8}, expected 0x{requiredFlags:X8}.");
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

            _helperProcess?.Dispose();
            _helperProcess = null;
            _helperProcessHandle = IntPtr.Zero;

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
