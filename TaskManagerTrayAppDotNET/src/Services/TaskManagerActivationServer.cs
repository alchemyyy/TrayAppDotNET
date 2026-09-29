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
/// The pipe name is scoped to the current session and the pipe is restricted to the current user, so
/// separate interactive sessions on the same machine never collide or cross-signal.
/// </summary>
internal sealed class TaskManagerActivationServer : IDisposable
{
    private const string ActivationMessage = "SHOW";
    private const string AcknowledgementMessage = "OK";
    private const int ListenerFailureBackoffMilliseconds = 250;

    private readonly Action _activate;
    private readonly Action<string>? _log;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancellation = new();
    private Thread? _listenerThread;
    private bool _disposed;

    public TaskManagerActivationServer(Action activate, Action<string>? log)
    {
        ArgumentNullException.ThrowIfNull(activate);
        _activate = activate;
        _log = log;
        _pipeName = ResolvePipeName();
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
            using NamedPipeClientStream client = new(
                serverName: ".",
                ResolvePipeName(),
                PipeDirection.InOut,
                PipeOptions.None);
            client.Connect(timeoutMilliseconds);

            using StreamReader reader = new(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            using StreamWriter writer = new(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
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

    private void RunListenerLoop()
    {
        CancellationToken cancellationToken = _cancellation.Token;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using NamedPipeServerStream server = CreateServerStream();
                server.WaitForConnectionAsync(cancellationToken).GetAwaiter().GetResult();
                HandleConnection(server);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                _log?.Invoke($"TaskManagerActivationServer listener: {exception.Message}");
                // Avoid a tight failure loop if pipe creation keeps faulting
                if (!cancellationToken.IsCancellationRequested)
                    cancellationToken.WaitHandle.WaitOne(ListenerFailureBackoffMilliseconds);
            }
        }
    }

    private void HandleConnection(NamedPipeServerStream server)
    {
        using StreamReader reader = new(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using StreamWriter writer = new(server, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
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

    private NamedPipeServerStream CreateServerStream()
    {
        PipeSecurity security = new();
        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
                                         ?? throw new InvalidOperationException("The current user SID is unavailable.");
        security.AddAccessRule(new PipeAccessRule(
            currentUser,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    private static string ResolvePipeName()
    {
        using System.Diagnostics.Process current = System.Diagnostics.Process.GetCurrentProcess();
        return $"{Constants.ApplicationName}-Activate-{Constants.AppGUID}-session{current.SessionId}";
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
