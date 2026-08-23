using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Owns the fixed shared page and event-driven elevated helper session.</summary>
internal sealed unsafe class ElevatedKillHelperClient : IDisposable
{
    private const int StartupTimeoutMilliseconds = 10_000;
    private const int ShutdownTimeoutMilliseconds = 2_000;
    private const uint TerminationExitCode = 1;

    private readonly Action<string>? _log;
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

    public bool IsReady =>
        !_disposed &&
        _mailbox != null &&
        Volatile.Read(ref _mailbox->HelperState) == KillHelperProtocol.StateReady &&
        IsHelperProcessAlive();

    public int HardeningFlags => _mailbox == null
        ? 0
        : Volatile.Read(ref _mailbox->HelperFlags);

    /// <summary>Creates all kernel objects before crossing the UAC elevation boundary.</summary>
    public static ElevatedKillHelperClient? TryStart(Action<string>? log)
    {
        ElevatedKillHelperClient client = new(log);
        if (client.TryInitialize()) return client;

        client.Dispose();
        return null;
    }

    /// <summary>Pre-opens the selected target in the elevated helper.</summary>
    public bool TryArm(ProcessTerminationTarget? target, long generation)
    {
        if (!IsReady || _mailbox == null) return false;

        ProcessTerminationTarget value = target ?? default;
        _ = Interlocked.Increment(ref _mailbox->ArmPayloadSequence);
        _mailbox->ArmProcessID = value.ProcessID;
        _mailbox->ArmCreationTime = value.CreationTimeFileTime;
        _mailbox->ArmGeneration = generation;
        _ = Interlocked.Increment(ref _mailbox->ArmPayloadSequence);
        _ = Interlocked.Increment(ref _mailbox->ArmRequestSequence);
        return KillHelperNativeMethods.SetEvent(_requestEvent);
    }

    /// <summary>Publishes one fire request before any managed fallback work occurs.</summary>
    public bool TryRequestTermination(
        ProcessTerminationTarget target,
        long generation,
        out long requestSequence)
    {
        requestSequence = 0;
        if (!IsReady || _mailbox == null) return false;

        _ = Interlocked.Increment(ref _mailbox->FirePayloadSequence);
        _mailbox->FireProcessID = target.ProcessID;
        _mailbox->FireCreationTime = target.CreationTimeFileTime;
        _mailbox->FireGeneration = generation;
        _mailbox->FireExitCode = TerminationExitCode;
        _ = Interlocked.Increment(ref _mailbox->FirePayloadSequence);
        Interlocked.Exchange(ref _mailbox->FireResult, KillHelperProtocol.ResultNone);
        Interlocked.Exchange(ref _mailbox->FireError, 0);
        requestSequence = Interlocked.Increment(ref _mailbox->FireRequestSequence);
        return KillHelperNativeMethods.SetEvent(_requestEvent);
    }

    /// <summary>Waits for a matching helper response without depending on the thread pool.</summary>
    public bool TryWaitForResponse(
        long requestSequence,
        int timeoutMilliseconds,
        out int result,
        out int errorCode)
    {
        result = KillHelperProtocol.ResultNone;
        errorCode = 0;
        if (_mailbox == null || requestSequence <= 0) return false;

        long deadline = Environment.TickCount64 + timeoutMilliseconds;
        for (; ; )
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

    private bool TryInitialize()
    {
        string helperPath = Path.Combine(AppContext.BaseDirectory, Constants.KillHelperFileName);
        if (!File.Exists(helperPath))
        {
            _log?.Invoke($"Elevated kill helper was not found: {helperPath}");
            return false;
        }

        _mappingHandle = KillHelperNativeMethods.CreateFileMappingW(
            KillHelperNativeMethods.InvalidHandleValue,
            IntPtr.Zero,
            KillHelperNativeMethods.PageReadWrite,
            0,
            KillHelperProtocol.MailboxSize,
            null);
        if (_mappingHandle == IntPtr.Zero)
        {
            LogWin32Failure("CreateFileMapping");
            return false;
        }

        _mappingView = KillHelperNativeMethods.MapViewOfFile(
            _mappingHandle,
            KillHelperNativeMethods.FileMapAllAccess,
            0,
            0,
            KillHelperProtocol.MailboxSize);
        if (_mappingView == IntPtr.Zero)
        {
            LogWin32Failure("MapViewOfFile");
            return false;
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
            null);
        if (_requestEvent == IntPtr.Zero)
        {
            LogWin32Failure("CreateEvent request");
            return false;
        }

        _responseEvent = KillHelperNativeMethods.CreateEventW(
            IntPtr.Zero,
            isManualReset: false,
            initialState: false,
            null);
        if (_responseEvent == IntPtr.Zero)
        {
            LogWin32Failure("CreateEvent response");
            return false;
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = helperPath,
            Arguments = string.Concat(
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                " ",
                _mappingHandle.ToInt64().ToString("X", CultureInfo.InvariantCulture),
                " ",
                _requestEvent.ToInt64().ToString("X", CultureInfo.InvariantCulture),
                " ",
                _responseEvent.ToInt64().ToString("X", CultureInfo.InvariantCulture)),
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            _helperProcess = Process.Start(startInfo);
            if (_helperProcess == null)
            {
                _log?.Invoke("Windows did not create the elevated kill helper process.");
                return false;
            }
            _helperProcessHandle = _helperProcess.Handle;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            _log?.Invoke($"Elevated kill helper launch failed: {exception.Message}");
            return false;
        }

        uint startupWait = Kernel32.WaitForSingleObject(_responseEvent, StartupTimeoutMilliseconds);
        if (startupWait != Kernel32.WAIT_OBJECT_0)
        {
            _log?.Invoke($"Elevated kill helper startup wait failed: 0x{startupWait:X8}");
            return false;
        }

        int helperState = Volatile.Read(ref _mailbox->HelperState);
        if (helperState != KillHelperProtocol.StateReady)
        {
            int startupError = Volatile.Read(ref _mailbox->HelperStartupError);
            string startupMessage = startupError == 0
                ? $"helper state {helperState} did not become ready"
                : new Win32Exception(startupError).Message;
            _log?.Invoke($"Elevated kill helper startup failed: {startupMessage}");
            return false;
        }

        LogHelperHardeningState(Volatile.Read(ref _mailbox->HelperFlags));
        return true;
    }

    private bool IsHelperProcessAlive()
    {
        if (_helperProcessHandle == IntPtr.Zero) return false;
        return Kernel32.WaitForSingleObject(_helperProcessHandle, 0) == Kernel32.WAIT_TIMEOUT;
    }

    private void LogHelperHardeningState(int flags)
    {
        int requiredFlags = KillHelperProtocol.RequiredHardeningFlags;
        if ((flags & requiredFlags) == requiredFlags)
        {
            _log?.Invoke($"Elevated kill helper ready, PID {_mailbox->HelperProcessID}; hardening 0x{flags:X8}.");
            return;
        }

        _log?.Invoke(
            $"Elevated kill helper ready with partial hardening 0x{flags:X8}, expected 0x{requiredFlags:X8}.");
    }

    private void LogWin32Failure(string operation)
    {
        int errorCode = Marshal.GetLastWin32Error();
        _log?.Invoke($"{operation} failed: {new Win32Exception(errorCode).Message}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_mailbox != null)
        {
            _ = Interlocked.Or(ref _mailbox->ControlFlags, KillHelperProtocol.ControlShutdown);
            if (_requestEvent != IntPtr.Zero)
                _ = KillHelperNativeMethods.SetEvent(_requestEvent);
        }

        if (_helperProcessHandle != IntPtr.Zero)
            _ = Kernel32.WaitForSingleObject(_helperProcessHandle, ShutdownTimeoutMilliseconds);

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

    private static void CloseNativeHandle(ref IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        _ = Kernel32.CloseHandle(handle);
        handle = IntPtr.Zero;
    }
}
