using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.Services.Ipc;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Whether a brokered request was serviced, and why not when it was not.</summary>
internal enum BrokerAttemptOutcome
{
    Ready,
    Declined,
    Failed,

    /// <summary>The request was sent but no answer came in time; the broker may still complete it.</summary>
    TimedOut
}

/// <summary>The result of asking the broker to run one op: either it was serviced, or why it was not.</summary>
internal readonly record struct BrokerAttempt(
    bool Serviced,
    BrokerAttemptOutcome Outcome,
    BrokerResponse Response)
{
    public static BrokerAttempt NotServiced(BrokerAttemptOutcome outcome) => new(Serviced: false, outcome, default);
    public static BrokerAttempt FromResponse(BrokerResponse response) => new(Serviced: true, BrokerAttemptOutcome.Ready, response);
}

/// <summary>
/// Medium-integrity client for the elevated broker. On the first request it prompts (in-app
/// confirmation, then the Windows UAC prompt) to launch "tmtadn.exe --elevated-broker"; the broker
/// serves this one connection for as long as it stays up, so later requests reuse it with no further
/// prompt. When the connection drops (the broker exited, crashed or timed out) the next request
/// prompts again and launches a new broker. A decline is never remembered.
///
/// All pipe I/O is asynchronous with a per-operation timeout (<see cref="ElevationBrokerContract.ResponseTimeout"/>),
/// so a slow or hung broker never blocks the UI thread; a timed-out request drops the connection. The broker's log
/// lines arrive on the same connection ahead of each response and are written into this app's log.
/// </summary>
internal sealed class ElevationBrokerClient : IDisposable
{
    private const int ConnectAttempts = 25;
    private const int ConnectRetryDelayMilliseconds = 200;
    private const int ConnectTimeoutMilliseconds = 250;

    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);

    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private long _correlationCounter;
    private bool _disposed;

    public ElevationBrokerClient(Action<string>? log) => _log = log;

    /// <summary>
    /// Runs one op through the broker, starting it on demand. <paramref name="confirmAsync"/> shows the
    /// in-app explanation before the first UAC prompt and is invoked at most once per session.
    /// </summary>
    public async Task<BrokerAttempt> RequestAsync(
        ElevationOp op,
        int processId,
        long creationTimeFileTime,
        ulong argument,
        string text,
        Func<Task<bool>> confirmAsync)
    {
        ArgumentNullException.ThrowIfNull(confirmAsync);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            BrokerAttemptOutcome startOutcome = await EnsureConnectedAsync(confirmAsync).ConfigureAwait(true);
            if (startOutcome != BrokerAttemptOutcome.Ready)
                return BrokerAttempt.NotServiced(startOutcome);

            long correlationId = Interlocked.Increment(ref _correlationCounter);
            BrokerRequest request = new(correlationId, op, processId, creationTimeFileTime, argument, text);
            (BrokerResponse? response, bool timedOut) = await TrySendAndReceiveAsync(
                request,
                ElevationBrokerContract.ResponseTimeout(op)).ConfigureAwait(true);
            if (response is { } received)
                return BrokerAttempt.FromResponse(received);

            // The connection dropped or timed out mid-request; drop it so a later request can relaunch. A timed-out
            // request was delivered, so the broker may still finish it after the UI stops waiting.
            DropConnection();
            return BrokerAttempt.NotServiced(timedOut ? BrokerAttemptOutcome.TimedOut : BrokerAttemptOutcome.Failed);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<BrokerAttemptOutcome> EnsureConnectedAsync(Func<Task<bool>> confirmAsync)
    {
        if (_pipe is { IsConnected: true }) return BrokerAttemptOutcome.Ready;

        DropConnection();

        // A decline is never remembered: every admin action re-prompts until elevation is actually granted
        bool confirmed = await confirmAsync().ConfigureAwait(true);
        if (!confirmed) return BrokerAttemptOutcome.Declined;

        string pipeName = ElevationBrokerContract.CreatePipeName();
        BrokerAttemptOutcome launchOutcome = await Task.Run(() => Launch(pipeName)).ConfigureAwait(true);
        if (launchOutcome != BrokerAttemptOutcome.Ready) return launchOutcome;

        return await ConnectAsync(pipeName).ConfigureAwait(true)
            ? BrokerAttemptOutcome.Ready
            : BrokerAttemptOutcome.Failed;
    }

    private BrokerAttemptOutcome Launch(string pipeName)
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            _log?.Invoke("ElevationBrokerClient: the process path is unavailable.");
            return BrokerAttemptOutcome.Failed;
        }

        string arguments = ElevationBrokerContract.BuildArguments(
            Environment.ProcessId,
            ProcessExitWatch.CurrentProcessStartFileTime,
            pipeName);
        if (ExplorerProcessLauncher.TryShellExecute(
                executable,
                arguments,
                workingDirectory: AppContext.BaseDirectory,
                verb: "runas",
                out int launchError,
                out string launchErrorMessage))
            return BrokerAttemptOutcome.Ready;

        if (TrayAppDotNETElevatedLaunch.IsUserCancelled(launchError))
        {
            _log?.Invoke("ElevationBrokerClient: administrator approval was canceled.");
            return BrokerAttemptOutcome.Declined;
        }

        _log?.Invoke($"ElevationBrokerClient: broker launch failed: {launchErrorMessage}");
        return BrokerAttemptOutcome.Failed;
    }

    private async Task<bool> ConnectAsync(string pipeName)
    {
        for (int attempt = 0; attempt < ConnectAttempts; attempt++)
        {
            NamedPipeClientStream pipe = TrayAppDotNETPipeSecurity.CreateClient(pipeName, PipeDirection.InOut);
            try
            {
                await pipe.ConnectAsync(ConnectTimeoutMilliseconds).ConfigureAwait(true);
                if (!IsOurBroker(pipe))
                {
                    pipe.Dispose();
                    _log?.Invoke("ElevationBrokerClient: the broker pipe is not served by this app's broker; refusing it.");
                    return false;
                }

                _pipe = pipe;
                _reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                _writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
                return true;
            }
            catch (Exception exception) when (exception is TimeoutException or IOException)
            {
                // The broker has not created its pipe yet (or is briefly busy); back off and retry
                pipe.Dispose();
                await Task.Delay(ConnectRetryDelayMilliseconds).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                pipe.Dispose();
                _log?.Invoke($"ElevationBrokerClient.ConnectAsync: {exception.Message}");
                return false;
            }
        }

        _log?.Invoke("ElevationBrokerClient: timed out connecting to the broker.");
        return false;
    }

    // The broker was started through the shell, so its PID is unknown here: accept a server owned by this account whose
    // process runs this executable
    private static bool IsOurBroker(NamedPipeClientStream pipe)
    {
        if (!TrayAppDotNETPipeSecurity.IsServerOwnedByCurrentUser(pipe)
            || !TrayAppDotNETPipeSecurity.TryGetServerProcessId(pipe, out int serverProcessId))
            return false;

        string serverImage = PathNormalization.Normalize(ProcessImagePath.TryGet(serverProcessId));
        return serverImage.Length > 0
               && string.Equals(serverImage, PathNormalization.Normalize(Environment.ProcessPath), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(BrokerResponse? Response, bool TimedOut)> TrySendAndReceiveAsync(
        BrokerRequest request,
        TimeSpan timeout)
    {
        if (_writer == null || _reader == null) return (null, false);

        using CancellationTokenSource timeoutSource = new(timeout);
        try
        {
            await _writer.WriteLineAsync(request.ToWire().AsMemory(), timeoutSource.Token).ConfigureAwait(true);
            while (true)
            {
                string? line = await _reader.ReadLineAsync(timeoutSource.Token).ConfigureAwait(true);

                // The broker writes no log file of its own; its lines arrive ahead of the response
                if (BrokerLogLine.TryDecode(line, out string brokerMessage))
                {
                    _log?.Invoke("[elevated broker] " + brokerMessage);
                    continue;
                }

                if (line != null && BrokerResponse.TryParse(line, out BrokerResponse response)
                                 && response.CorrelationId == request.CorrelationId)
                    return (response, false);

                _log?.Invoke($"ElevationBrokerClient: no matching response from the broker for {request.Op}.");
                return (null, false);
            }
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            _log?.Invoke($"ElevationBrokerClient: {request.Op} timed out after {timeout.TotalSeconds:0} s.");
            return (null, true);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            _log?.Invoke($"ElevationBrokerClient.TrySendAndReceiveAsync: {exception.Message}");
            return (null, false);
        }
    }

    private void DropConnection()
    {
        _reader?.Dispose();
        _reader = null;
        _writer?.Dispose();
        _writer = null;
        _pipe?.Dispose();
        _pipe = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DropConnection();
        _gate.Dispose();
    }
}
