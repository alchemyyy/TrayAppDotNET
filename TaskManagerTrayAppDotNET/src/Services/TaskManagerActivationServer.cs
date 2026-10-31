using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>
/// A tiny named-pipe server, hosted by the running Task Manager instance, that a second launch can
/// signal to bring the existing window forward instead of starting a duplicate. This backs the
/// graceful hand-off for taskmgr.exe launches redirected through Image File Execution Options: when
/// tmtadn is already running, the redirected process connects here, the window is activated, and the
/// redirected process exits. When no server answers, the caller knows to cold-start normally.
///
/// Named pipes are machine-global, so the session number in the name is not a scope by itself. The name
/// also carries the user SID, the pipe's DACL grants only that user, and its owner is set explicitly to the
/// user SID (an elevated instance would otherwise own it as Administrators). The client connects at
/// identification level, so the server can never impersonate it, and only trusts a server whose pipe is
/// owned by its own user SID: another account cannot create such a pipe, so it cannot answer "OK" in place
/// of the real instance. The next server instance exists before the current one is disposed, so there is
/// no window in which the name is unclaimed, and repeated creation failures back off exponentially.
/// </summary>
internal sealed class TaskManagerActivationServer : IDisposable
{
    private const string ActivationMessage = "SHOW";
    private const string AcknowledgementMessage = "OK";
    private const int InitialListenerBackoffMilliseconds = 250;
    private const int MaximumListenerBackoffMilliseconds = 30_000;

    /// <summary>The current and the pre-created next instance.</summary>
    private const int MaximumServerInstances = 2;

    /// <summary>Server pipes are overlapped so a pending wait can be cancelled at shutdown.</summary>
    internal const PipeOptions ServerPipeOptions = PipeOptions.Asynchronous;

    /// <summary>The server may identify the client but never act as it.</summary>
    internal const TokenImpersonationLevel ClientImpersonationLevel = TokenImpersonationLevel.Identification;

    private readonly Action _activate;
    private readonly Action<string>? _log;
    private readonly SecurityIdentifier _currentUser;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancellation = new();
    private Thread? _listenerThread;
    private bool _disposed;

    // Touched only by the listener thread
    private bool _clientFailureLogged;

    public TaskManagerActivationServer(Action activate, Action<string>? log)
    {
        ArgumentNullException.ThrowIfNull(activate);
        _activate = activate;
        _log = log;
        _currentUser = CurrentUserSid();
        _pipeName = ResolvePipeName(_currentUser, CurrentSessionId());
    }

    /// <summary>Hosts the server on an explicit pipe name; tests use a unique name so they never reach the app.</summary>
    internal TaskManagerActivationServer(Action activate, Action<string>? log, string pipeName)
    {
        ArgumentNullException.ThrowIfNull(activate);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _activate = activate;
        _log = log;
        _currentUser = CurrentUserSid();
        _pipeName = pipeName;
    }

    /// <summary>Starts accepting activation requests on a dedicated background thread.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_listenerThread != null) return;

        _listenerThread = new Thread(RunListenerLoop)
        {
            IsBackground = true,
            Name = Constants.ApplicationName + ".Activation"
        };
        _listenerThread.Start();
    }

    /// <summary>Signals a running instance to show its window; returns false when none is listening.</summary>
    public static bool TryActivateRunningInstance(int timeoutMilliseconds, Action<string>? log)
    {
        try
        {
            SecurityIdentifier currentUser = CurrentUserSid();
            return TryActivate(ResolvePipeName(currentUser, CurrentSessionId()), currentUser, timeoutMilliseconds, log);
        }
        catch (Exception exception)
        {
            log?.Invoke($"TaskManagerActivationServer.TryActivateRunningInstance: {exception.Message}");
            return false;
        }
    }

    internal static bool TryActivate(
        string pipeName,
        SecurityIdentifier currentUser,
        int timeoutMilliseconds,
        Action<string>? log)
    {
        try
        {
            using NamedPipeClientStream client = new(
                serverName: ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.None,
                ClientImpersonationLevel);
            client.Connect(timeoutMilliseconds);

            SecurityIdentifier? serverOwner =
                client.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (!IsTrustedServerOwner(serverOwner, currentUser))
            {
                log?.Invoke(
                    $"TaskManagerActivationServer: ignored an activation pipe owned by {serverOwner?.Value ?? "nobody"}.");
                return false;
            }

            using StreamReader reader = new(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            using StreamWriter writer = new(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
            {
                AutoFlush = true
            };
            writer.WriteLine(ActivationMessage);
            return string.Equals(reader.ReadLine(), AcknowledgementMessage, StringComparison.Ordinal);
        }
        catch (TimeoutException)
        {
            // No instance is listening, so the caller should cold-start
            return false;
        }
        catch (Exception exception)
        {
            log?.Invoke($"TaskManagerActivationServer.TryActivateRunningInstance: {exception.Message}");
            return false;
        }
    }

    /// <summary>Builds the per-user, per-session pipe name.</summary>
    internal static string ResolvePipeName(SecurityIdentifier user, int sessionId)
    {
        ArgumentNullException.ThrowIfNull(user);
        return $"{Constants.ApplicationName}-Activate-{Constants.AppGUID}-{user.Value}-session{sessionId}";
    }

    /// <summary>Returns whether a server pipe is owned by the same user, which no other account can arrange.</summary>
    internal static bool IsTrustedServerOwner(SecurityIdentifier? serverOwner, SecurityIdentifier currentUser) =>
        serverOwner != null && serverOwner.Equals(currentUser);

    /// <summary>Returns the wait before retrying after the given number of consecutive listener failures.</summary>
    internal static TimeSpan ListenerBackoff(int consecutiveFailures)
    {
        if (consecutiveFailures <= 1) return TimeSpan.FromMilliseconds(InitialListenerBackoffMilliseconds);
        int doublings = Math.Min(consecutiveFailures - 1, 16);
        long delay = Math.Min((long)InitialListenerBackoffMilliseconds << doublings, MaximumListenerBackoffMilliseconds);
        return TimeSpan.FromMilliseconds(delay);
    }

    private void RunListenerLoop()
    {
        CancellationToken cancellationToken = _cancellation.Token;
        int consecutiveFailures = 0;
        NamedPipeServerStream? current = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    current ??= CreateServerStream(firstInstance: true);
                    current.WaitForConnectionAsync(cancellationToken).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // Only a listener failure (creating or waiting on the pipe) backs off
                    current?.Dispose();
                    current = null;
                    consecutiveFailures++;
                    if (consecutiveFailures == 1)
                        _log?.Invoke($"TaskManagerActivationServer listener: {exception.Message}");
                    if (!cancellationToken.IsCancellationRequested)
                        cancellationToken.WaitHandle.WaitOne(ListenerBackoff(consecutiveFailures));
                    continue;
                }

                consecutiveFailures = 0;

                // Claim the name again before serving, so a launch arriving now still finds a listener. A client
                // that fails while being served costs only its own stream; the next listener stays up.
                NamedPipeServerStream served = current;
                current = TryCreateNextServerStream();
                ServeClient(served);
            }
        }
        finally
        {
            current?.Dispose();
        }
    }

    private void ServeClient(NamedPipeServerStream served)
    {
        try
        {
            HandleConnection(served);
            _clientFailureLogged = false;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException
                                              or InvalidOperationException)
        {
            // The client went away mid-exchange; log once until a client is served again
            if (!_clientFailureLogged) _log?.Invoke($"TaskManagerActivationServer: a client failed: {exception.Message}");
            _clientFailureLogged = true;
        }
        finally
        {
            served.Dispose();
        }
    }

    private NamedPipeServerStream? TryCreateNextServerStream()
    {
        try
        {
            return CreateServerStream(firstInstance: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"TaskManagerActivationServer: could not pre-create the next listener: {exception.Message}");
            return null;
        }
    }

    private void HandleConnection(NamedPipeServerStream server)
    {
        using StreamReader reader = new(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using StreamWriter writer = new(server, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            AutoFlush = true
        };
        if (!string.Equals(reader.ReadLine(), ActivationMessage, StringComparison.Ordinal)) return;

        try
        {
            _activate();
        }
        catch (Exception exception)
        {
            _log?.Invoke($"TaskManagerActivationServer activation callback failed: {exception}");
        }

        writer.WriteLine(AcknowledgementMessage);
    }

    private NamedPipeServerStream CreateServerStream(bool firstInstance)
    {
        PipeSecurity security = new();
        security.AddAccessRule(new PipeAccessRule(
            _currentUser,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));
        // Owned by the user even when elevated, so a client running at either integrity level trusts it
        security.SetOwner(_currentUser);

        // The first instance must create the name: if another account already holds it, fail instead of joining
        PipeOptions options = firstInstance ? ServerPipeOptions | PipeOptions.FirstPipeInstance : ServerPipeOptions;
        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            MaximumServerInstances,
            PipeTransmissionMode.Byte,
            options,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    private static SecurityIdentifier CurrentUserSid()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User ?? throw new InvalidOperationException("The current user SID is unavailable.");
    }

    private static int CurrentSessionId()
    {
        using System.Diagnostics.Process current = System.Diagnostics.Process.GetCurrentProcess();
        return current.SessionId;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cancellation.Cancel();
        }
        catch (Exception exception)
        {
            _log?.Invoke($"TaskManagerActivationServer.Dispose cancel: {exception.Message}");
        }

        _listenerThread?.Join(TimeSpan.FromSeconds(2));
        _cancellation.Dispose();
    }
}
