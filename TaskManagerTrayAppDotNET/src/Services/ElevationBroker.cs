using System.ComponentModel;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using TaskManagerTrayAppDotNET.Models;
using TrayAppDotNETCommon.Interop;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>
/// The elevated side of the privileged-action broker. Launched as "tmtadn.exe --elevated-broker"
/// with one UAC consent, it serves an enumerated op set from the medium-integrity UI over a named
/// pipe and exits when the UI (its parent) exits. The UI is the client; this process owns the pipe
/// ACL and authenticates the connecting client before serving any request.
///
/// This foundation handles the process-handle ops (priority, affinity). Terminate re-parenting and
/// the registry/SCM/WTS ops are layered on top later without changing this shape.
/// </summary>
internal static class ElevationBroker
{
    private const uint Synchronize = 0x00100000;
    private const int MaximumPathCharacters = 32_768;
    private const int ExitBadArguments = 2;
    private const int ExitParentUnavailable = 3;

    private static readonly Lock ServerSync = new();

    /// <summary>Returns true when this launch is the elevated broker mode.</summary>
    public static bool IsBrokerLaunch(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return Array.Exists(
            args,
            argument => string.Equals(argument, ElevationBrokerContract.ModeArgument, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Runs the broker loop until the parent exits. Never shows a UI.</summary>
    public static int Run(string[] args)
    {
        if (!TryGetArgumentValue(args, ElevationBrokerContract.ParentArgument, out string? parentText) ||
            !int.TryParse(parentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parentProcessId) ||
            !TryGetArgumentValue(args, ElevationBrokerContract.PipeArgument, out string? pipeName) ||
            string.IsNullOrWhiteSpace(pipeName))
        {
            TADNLog.Log("ElevationBroker: missing or invalid --broker-parent / --broker-pipe arguments.");
            return ExitBadArguments;
        }

        IntPtr parentHandle = Kernel32.OpenProcess(Synchronize, bInheritHandle: false, (uint)parentProcessId);
        if (parentHandle == IntPtr.Zero)
        {
            TADNLog.Log($"ElevationBroker: parent process {parentProcessId} is unavailable; exiting.");
            return ExitParentUnavailable;
        }

        string expectedClientImage = Environment.ProcessPath ?? string.Empty;
        using ManualResetEventSlim stopping = new(initialState: false);
        NamedPipeServerStream?[] activeServer = [null];

        Thread parentWatch = new(() => WatchParent(parentHandle, stopping, activeServer))
        {
            IsBackground = true,
            Name = Constants.ApplicationName + ".BrokerParentWatch"
        };
        parentWatch.Start();

        try
        {
            RunAcceptLoop(pipeName, expectedClientImage, stopping, activeServer);
        }
        finally
        {
            stopping.Set();
            _ = Kernel32.CloseHandle(parentHandle);
        }

        return 0;
    }

    private static void RunAcceptLoop(
        string pipeName,
        string expectedClientImage,
        ManualResetEventSlim stopping,
        NamedPipeServerStream?[] activeServer)
    {
        while (!stopping.IsSet)
        {
            NamedPipeServerStream server = CreateServerStream(pipeName);
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
                server.WaitForConnection();
                if (stopping.IsSet) return;
                if (AuthenticateClient(server, expectedClientImage))
                    ServeConnection(server, stopping);
            }
            catch (Exception exception) when (exception is ObjectDisposedException or IOException)
            {
                // The parent-watch thread disposed the server, or the client dropped; loop re-evaluates stopping
            }
            catch (Exception exception)
            {
                TADNLog.Log($"ElevationBroker accept loop: {exception.Message}");
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
    }

    private static void ServeConnection(NamedPipeServerStream server, ManualResetEventSlim stopping)
    {
        using StreamReader reader = new(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using StreamWriter writer = new(server, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };

        while (server.IsConnected && !stopping.IsSet)
        {
            string? line = reader.ReadLine();
            if (line == null) return;
            if (!BrokerRequest.TryParse(line, out BrokerRequest request))
            {
                TADNLog.Log("ElevationBroker: discarded a malformed request.");
                continue;
            }

            BrokerResponse response = Execute(request);
            writer.WriteLine(response.ToWire());
        }
    }

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
            TADNLog.Log($"ElevationBroker.Execute({request.Op}): {exception.Message}");
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
        // The broker performs the whole dump: it opens the target, creates the file, and writes it, then
        // returns the path. No file handle is marshaled across the process boundary.
        if (ProcessNativeActions.TryCreateMemoryDump(target, out string dumpPath, out string errorMessage))
            return new BrokerResponse(request.CorrelationId, BrokerResultCode.Success, dumpPath);

        return new BrokerResponse(request.CorrelationId, BrokerResultCode.Failed, errorMessage);
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
        if (!StartupApprovalWire.TryDecode(request.Text, out StartupAppApprovalTarget target))
            return new BrokerResponse(request.CorrelationId, BrokerResultCode.InvalidTarget, "The startup entry could not be decoded.");

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
        ErrorAccessDenied => BrokerResultCode.AccessDenied,
        ErrorInvalidParameter => BrokerResultCode.InvalidTarget,
        _ => BrokerResultCode.Failed
    };

    private static bool AuthenticateClient(NamedPipeServerStream server, string expectedClientImage)
    {
        if (string.IsNullOrEmpty(expectedClientImage)) return false;
        if (!TryGetNamedPipeClientProcessId(server.SafePipeHandle.DangerousGetHandle(), out uint clientProcessId))
        {
            TADNLog.Log("ElevationBroker: could not resolve the connecting client process.");
            return false;
        }

        if (!TryGetProcessImagePath((int)clientProcessId, out string clientImage))
        {
            TADNLog.Log("ElevationBroker: could not read the connecting client image path.");
            return false;
        }

        bool authorized = string.Equals(
            Path.GetFullPath(clientImage),
            Path.GetFullPath(expectedClientImage),
            StringComparison.OrdinalIgnoreCase);
        if (!authorized)
            TADNLog.Log("ElevationBroker: rejected a client whose image does not match this executable.");
        return authorized;
    }

    private static void WatchParent(
        IntPtr parentHandle,
        ManualResetEventSlim stopping,
        NamedPipeServerStream?[] activeServer)
    {
        _ = Kernel32.WaitForSingleObject(parentHandle, Kernel32.INFINITE);
        stopping.Set();
        lock (ServerSync)
        {
            // Unblock a pending WaitForConnection so the accept loop can exit
            activeServer[0]?.Dispose();
        }
    }

    private static NamedPipeServerStream CreateServerStream(string pipeName)
    {
        PipeSecurity security = new();
        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
                                         ?? throw new InvalidOperationException("The current user SID is unavailable.");
        security.AddAccessRule(new PipeAccessRule(
            currentUser,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    private static bool TryGetProcessImagePath(int processId, out string imagePath)
    {
        imagePath = string.Empty;
        if (processId <= 0) return false;

        IntPtr handle = Kernel32.OpenProcess(
            Kernel32.PROCESS_QUERY_LIMITED_INFORMATION,
            bInheritHandle: false,
            (uint)processId);
        if (handle == IntPtr.Zero) return false;

        try
        {
            StringBuilder pathBuffer = new(MaximumPathCharacters);
            uint characterCount = (uint)pathBuffer.Capacity;
            if (!Kernel32.QueryFullProcessImageNameW(handle, dwFlags: 0, pathBuffer, ref characterCount))
                return false;

            imagePath = pathBuffer.ToString(startIndex: 0, checked((int)characterCount));
            return true;
        }
        finally
        {
            _ = Kernel32.CloseHandle(handle);
        }
    }

    private static bool TryGetArgumentValue(string[] args, string name, out string? value)
    {
        value = null;
        int index = Array.FindIndex(args, argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length) return false;

        value = args[index + 1];
        return true;
    }

    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidParameter = 87;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr pipeHandle, out uint clientProcessId);

    private static bool TryGetNamedPipeClientProcessId(IntPtr pipeHandle, out uint clientProcessId) =>
        GetNamedPipeClientProcessId(pipeHandle, out clientProcessId) && clientProcessId != 0;
}
