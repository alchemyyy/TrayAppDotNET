using System.IO.Pipes;
using System.Text;
using Microsoft.Win32.SafeHandles;
using TrayAppDotNETCommon.Services.Ipc;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>
/// The elevated side of the privileged-action broker. Launched as "tmtadn.exe --elevated-broker"
/// with one UAC consent, it serves an enumerated op set (priority, affinity, memory dumps, session
/// disconnect, service control, startup approval) from the medium-integrity UI over a named pipe.
///
/// The UI is the client and the only process the broker serves: the command line names its PID and
/// start time, the broker opens that process and checks the start time before creating the pipe, and a
/// connecting client must be that PID (the broker's open handle keeps the PID from being recycled). A
/// same-user process that connects first is rejected and never served. The broker serves exactly one
/// accepted connection and exits when it drops or the UI exits, so a later request launches a new broker.
/// Every served operation is logged with its target, the client PID and the result. The broker writes no log file:
/// its lines go to the UI over the pipe and the UI writes them into its own log.
/// </summary>
internal static class ElevationBroker
{
    private const int ExitBadArguments = 2;
    private const int ExitParentUnavailable = 3;
    private const int ExitParentMismatch = 4;

    private static readonly Lock ServerSync = new();
    private static readonly BrokerLogBuffer PendingLog = new();

    // The pinned UI process, opened with PROCESS_DUP_HANDLE so the dump op can take the UI's file handle
    private static IntPtr _parentProcess;

    /// <summary>Returns true when this launch is the elevated broker mode.</summary>
    public static bool IsBrokerLaunch(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return Array.Exists(
            args,
            argument => string.Equals(argument, ElevationBrokerContract.ModeArgument, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The broker's log sink (see <see cref="TADNLog.InitializeForElevatedProcess"/>): the elevated broker writes no
    /// file, so lines wait here and travel to the UI ahead of the next response. Lines logged when no UI ever
    /// connects (bad arguments, a parent mismatch) are lost; the UI then logs its own connect failure.
    /// </summary>
    public static void QueueLogLine(string message) => PendingLog.Add(message);

    /// <summary>Serves the launching UI's connection until it drops or the UI exits. Never shows a UI.</summary>
    public static int Run(string[] args)
    {
        if (!ElevationBrokerContract.TryParseArguments(
                args,
                out int parentProcessId,
                out long parentStartFileTime,
                out string pipeName))
        {
            TADNLog.Log("ElevationBroker: missing or invalid --broker-parent / --broker-parent-start / --broker-pipe arguments.");
            return ExitBadArguments;
        }

        using ManualResetEventSlim stopping = new(initialState: false);
        NamedPipeServerStream?[] activeServer = [null];
        ProcessExitWatchResult parentWatch = ProcessExitWatch.TryRegister(
            parentProcessId,
            parentStartFileTime,
            () => StopServing(stopping, activeServer),
            out IDisposable? parentRegistration);
        switch (parentWatch)
        {
            case ProcessExitWatchResult.Exited:
                TADNLog.Log($"ElevationBroker: process {parentProcessId} is not the running UI that launched the broker; exiting.");
                return ExitParentMismatch;
            case ProcessExitWatchResult.Unavailable:
                TADNLog.Log($"ElevationBroker: parent process {parentProcessId} is unavailable; exiting.");
                return ExitParentUnavailable;
        }

        // PROCESS_DUP_HANDLE on the same pinned parent lets the dump op take the file handle the UI created
        if (!ProcessNativeActions.TryOpenValidatedProcess(
                new ProcessTerminationTarget(parentProcessId, parentStartFileTime),
                DumpFileHandoff.ProcessDuplicateHandle,
                out _parentProcess,
                out string parentError))
            TADNLog.Log($"ElevationBroker: cannot open the UI to take dump file handles: {parentError}");

        using (parentRegistration)
        {
            try
            {
                ServeParent(pipeName, parentProcessId, stopping, activeServer);
            }
            finally
            {
                stopping.Set();
                if (_parentProcess != IntPtr.Zero) _ = Kernel32.CloseHandle(_parentProcess);
                _parentProcess = IntPtr.Zero;
            }
        }

        return 0;
    }

    private static void ServeParent(
        string pipeName,
        int parentProcessId,
        ManualResetEventSlim stopping,
        NamedPipeServerStream?[] activeServer)
    {
        NamedPipeServerStream server = TrayAppDotNETPipeSecurity.CreateServer(pipeName, PipeDirection.InOut);
        lock (ServerSync)
        {
            if (stopping.IsSet)
            {
                server.Dispose();
                return;
            }

            activeServer[0] = server;
        }

        try
        {
            while (!stopping.IsSet)
            {
                server.WaitForConnection();
                if (stopping.IsSet) return;

                if (TrayAppDotNETPipeSecurity.TryGetClientProcessId(server, out int clientProcessId)
                    && ElevationBrokerContract.IsAuthorizedClient((uint)clientProcessId, parentProcessId))
                {
                    TADNLog.Log($"ElevationBroker: serving the UI (PID {clientProcessId}).");
                    ServeConnection(server, clientProcessId, stopping);
                    TADNLog.Log("ElevationBroker: the UI disconnected; exiting.");
                    return;
                }

                // Somebody else reached the pipe first: refuse it and keep waiting for the UI
                TADNLog.Log($"ElevationBroker: rejected client PID {clientProcessId}; only PID {parentProcessId} is served.");
                server.Disconnect();
            }
        }
        catch (Exception exception) when (exception is ObjectDisposedException or IOException)
        {
            // The parent-watch thread disposed the server, or the client dropped
        }
        finally
        {
            lock (ServerSync)
            {
                activeServer[0] = null;
            }

            server.Dispose();
        }
    }

    private static void ServeConnection(NamedPipeServerStream server, int clientProcessId, ManualResetEventSlim stopping)
    {
        using StreamReader reader = new(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using StreamWriter writer = new(server, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            AutoFlush = true
        };

        while (server.IsConnected && !stopping.IsSet)
        {
            string? line = reader.ReadLine();
            if (line == null) return;

            BrokerResponse response;
            if (BrokerRequest.TryParse(line, out BrokerRequest request))
            {
                response = Execute(request);
                TADNLog.Log(
                    $"ElevationBroker: {request.Op} {DescribeTarget(request)} for client PID {clientProcessId}: " +
                    $"{response.Code}{(response.Code == BrokerResultCode.Success ? string.Empty : " (" + response.Message + ")")}");
            }
            else
            {
                // Always answer, so the client never waits on a line the broker could not read
                response = new BrokerResponse(
                    BrokerRequest.ReadCorrelationId(line),
                    BrokerResultCode.Failed,
                    "The request was malformed.");
                TADNLog.Log($"ElevationBroker: rejected a malformed request from client PID {clientProcessId}.");
            }

            SendPendingLog(writer);
            writer.WriteLine(response.ToWire());
        }
    }

    // Log lines ride ahead of the response on the same connection, so the UI writes them in order
    private static void SendPendingLog(StreamWriter writer)
    {
        foreach (string message in PendingLog.TakeAll())
            writer.WriteLine(BrokerLogLine.Encode(message));
    }

    private static string DescribeTarget(BrokerRequest request) => request.Op switch
    {
        ElevationOp.SetPriority or ElevationOp.SetAffinity => $"PID {request.ProcessID} value {request.Argument}",
        ElevationOp.CreateDump => $"PID {request.ProcessID}",
        ElevationOp.DisconnectSession => $"session {(int)(uint)request.Argument}",
        ElevationOp.ServiceControl => $"service '{request.Text}' verb {request.Argument}",
        ElevationOp.SetStartupApproval => StartupApprovalWire.TryDecode(request.Text, out StartupAppApprovalTarget target)
            ? $"{target.Scope} '{target.RegistrySubKey}\\{target.ValueName}' enabled={request.Argument == 1UL}"
            : "an undecodable startup entry",
        _ => string.Empty
    };

    private static BrokerResponse Execute(BrokerRequest request)
    {
        ProcessTerminationTarget target = new(request.ProcessID, request.CreationTimeFileTime);
        try
        {
            return request.Op switch
            {
                ElevationOp.SetPriority => ExecuteSetPriority(request, target),
                ElevationOp.SetAffinity => ExecuteSetAffinity(request, target),
                ElevationOp.CreateDump => ExecuteCreateDump(request, target),
                ElevationOp.DisconnectSession => ExecuteDisconnectSession(request),
                ElevationOp.ServiceControl => ExecuteServiceControl(request),
                ElevationOp.SetStartupApproval => ExecuteSetStartupApproval(request),
                _ => new BrokerResponse(request.CorrelationId, BrokerResultCode.Failed, "Unsupported operation.")
            };
        }
        catch (Exception exception)
        {
            TADNLog.Log($"ElevationBroker.Execute({request.Op}): {exception}");
            return new BrokerResponse(request.CorrelationId, BrokerResultCode.Failed, exception.Message);
        }
    }

    private static BrokerResponse ExecuteSetPriority(BrokerRequest request, ProcessTerminationTarget target)
    {
        if (request.Argument > (ulong)uint.MaxValue || !Enum.IsDefined((ProcessPriorityLevel)(uint)request.Argument))
            return new BrokerResponse(request.CorrelationId, BrokerResultCode.Failed, "The requested priority is invalid.");

        bool succeeded = ProcessNativeActions.TrySetPriority(
            target,
            (ProcessPriorityLevel)(uint)request.Argument,
            out string errorMessage,
            out int win32Error);
        return CreateResponse(request.CorrelationId, succeeded, errorMessage, win32Error);
    }

    private static BrokerResponse ExecuteSetAffinity(BrokerRequest request, ProcessTerminationTarget target)
    {
        bool succeeded = ProcessNativeActions.TrySetAffinity(
            target,
            request.Argument,
            out string errorMessage,
            out int win32Error);
        return CreateResponse(request.CorrelationId, succeeded, errorMessage, win32Error);
    }

    private static BrokerResponse ExecuteCreateDump(BrokerRequest request, ProcessTerminationTarget target)
    {
        // The UI created the file in its own temp folder and sent its handle value; the broker never opens a path
        // there (a same-user junction could redirect an elevated create). It copies the handle out of the pinned
        // UI process, checks it is an empty regular file, and writes the dump into it; the UI reports the path.
        using SafeFileHandle? file = DumpFileHandoff.TryTake(_parentProcess, request.Argument, out string errorMessage);
        if (file == null)
            return new BrokerResponse(request.CorrelationId, BrokerResultCode.Failed, errorMessage);

        return ProcessNativeActions.TryWriteMemoryDump(target, file, out errorMessage)
            ? new BrokerResponse(request.CorrelationId, BrokerResultCode.Success, string.Empty)
            : new BrokerResponse(request.CorrelationId, BrokerResultCode.Failed, errorMessage);
    }

    private static BrokerResponse ExecuteDisconnectSession(BrokerRequest request)
    {
        int sessionId = unchecked((int)(uint)request.Argument);
        UserSessionActionResult result = new WindowsUserSessionService(TADNLog.Log).Disconnect(sessionId);
        return result.Succeeded
            ? new BrokerResponse(request.CorrelationId, BrokerResultCode.Success, string.Empty)
            : new BrokerResponse(request.CorrelationId, MapWin32(result.NativeErrorCode), result.ErrorMessage);
    }

    private static BrokerResponse ExecuteServiceControl(BrokerRequest request)
    {
        if (string.IsNullOrEmpty(request.Text))
            return new BrokerResponse(request.CorrelationId, BrokerResultCode.InvalidTarget, "The service name is missing.");
        if (request.Argument > (ulong)int.MaxValue || !Enum.IsDefined((BrokerServiceVerb)(int)request.Argument))
            return new BrokerResponse(request.CorrelationId, BrokerResultCode.Failed, "Unknown service verb.");

        WindowsServiceOperationResult result = (BrokerServiceVerb)(int)request.Argument switch
        {
            BrokerServiceVerb.Start => WindowsServiceManager.Start(request.Text),
            BrokerServiceVerb.Stop => WindowsServiceManager.Stop(request.Text),
            BrokerServiceVerb.Restart => WindowsServiceManager.Restart(request.Text),
            _ => new WindowsServiceManager().Disable(request.Text)
        };
        return result.Succeeded
            ? new BrokerResponse(request.CorrelationId, BrokerResultCode.Success, string.Empty)
            : new BrokerResponse(request.CorrelationId, MapWin32(result.Win32ErrorCode), result.ErrorMessage);
    }

    private static BrokerResponse ExecuteSetStartupApproval(BrokerRequest request)
    {
        // TryDecode only accepts the StartupApproved subkeys with a named value
        if (!StartupApprovalWire.TryDecode(request.Text, out StartupAppApprovalTarget target))
            return new BrokerResponse(request.CorrelationId, BrokerResultCode.InvalidTarget, "The startup entry is not a StartupApproved target.");
        if (request.Argument > 1UL)
            return new BrokerResponse(request.CorrelationId, BrokerResultCode.Failed, "The requested startup state is invalid.");

        StartupAppStatus desiredStatus = request.Argument == 1UL ? StartupAppStatus.Enabled : StartupAppStatus.Disabled;
        StartupAppActionResult result = StartupAppsService.SetStatusByTarget(target, desiredStatus);
        return result.Succeeded
            ? new BrokerResponse(request.CorrelationId, BrokerResultCode.Success, string.Empty)
            : new BrokerResponse(request.CorrelationId, BrokerResultCode.Failed, result.ErrorMessage);
    }

    private static BrokerResponse CreateResponse(long correlationId, bool succeeded, string errorMessage, int win32Error) =>
        succeeded
            ? new BrokerResponse(correlationId, BrokerResultCode.Success, string.Empty)
            : new BrokerResponse(correlationId, MapWin32(win32Error), errorMessage);

    private static BrokerResultCode MapWin32(int win32Error) => win32Error switch
    {
        NativeErrors.ERROR_ACCESS_DENIED => BrokerResultCode.AccessDenied,
        NativeErrors.ERROR_INVALID_PARAMETER => BrokerResultCode.InvalidTarget,
        _ => BrokerResultCode.Failed
    };

    // Runs on a thread-pool thread once the UI exits
    private static void StopServing(ManualResetEventSlim stopping, NamedPipeServerStream?[] activeServer)
    {
        stopping.Set();
        lock (ServerSync)
        {
            // Unblock a pending WaitForConnection or read so the broker can exit
            activeServer[0]?.Dispose();
        }
    }
}
