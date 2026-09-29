using System.Globalization;
using System.IO.Pipes;
using System.Text;
using TrayAppDotNETCommon.Services;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Why a brokered request could not be serviced, when it was not.</summary>
internal enum BrokerStartOutcome
{
    Ready,
    Declined,
    Failed
}

/// <summary>The result of asking the broker to run one op: either it was serviced, or it never started.</summary>
internal readonly record struct BrokerAttempt(
    bool Serviced,
    BrokerStartOutcome StartOutcome,
    BrokerResponse Response)
{
    public static BrokerAttempt NotStarted(BrokerStartOutcome outcome) => new(Serviced: false, outcome, default);
    public static BrokerAttempt FromResponse(BrokerResponse response) => new(Serviced: true, BrokerStartOutcome.Ready, response);
}

/// <summary>
/// Medium-integrity client for the elevated broker. On the first request it prompts once (in-app
/// confirmation, then the Windows UAC prompt) to launch "tmtadn.exe --elevated-broker"; the broker
/// then persists for the session and every later request reuses the same connection with no further
/// prompt. A declined consent is remembered for the session so the user is not re-prompted on every
/// subsequent privileged action.
/// </summary>
internal sealed class ElevationBrokerClient : IDisposable
{
    private const int ConnectAttempts = 25;
    private const int ConnectRetryDelayMilliseconds = 200;
    private const int ConnectTimeoutMilliseconds = 250;
    private const int ErrorCancelled = 1223;

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
            BrokerStartOutcome startOutcome = await EnsureConnectedAsync(confirmAsync).ConfigureAwait(true);
            if (startOutcome != BrokerStartOutcome.Ready)
                return BrokerAttempt.NotStarted(startOutcome);

            long correlationId = Interlocked.Increment(ref _correlationCounter);
            BrokerRequest request = new(correlationId, op, processId, creationTimeFileTime, argument, text);
            if (TrySendAndReceive(request, out BrokerResponse response))
                return BrokerAttempt.FromResponse(response);

            // The connection dropped mid-request (broker gone); drop it so a later request can relaunch
            DropConnection();
            return BrokerAttempt.NotStarted(BrokerStartOutcome.Failed);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<BrokerStartOutcome> EnsureConnectedAsync(Func<Task<bool>> confirmAsync)
    {
        if (_pipe is { IsConnected: true }) return BrokerStartOutcome.Ready;

        DropConnection();

        // A decline is never remembered: every admin action re-prompts until elevation is actually granted
        bool confirmed = await confirmAsync().ConfigureAwait(true);
        if (!confirmed) return BrokerStartOutcome.Declined;

        string pipeName = ElevationBrokerContract.CreatePipeName();
        BrokerStartOutcome launchOutcome = await Task.Run(() => Launch(pipeName)).ConfigureAwait(true);
        if (launchOutcome != BrokerStartOutcome.Ready) return launchOutcome;

        return await ConnectAsync(pipeName).ConfigureAwait(true)
            ? BrokerStartOutcome.Ready
            : BrokerStartOutcome.Failed;
    }

    private BrokerStartOutcome Launch(string pipeName)
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            _log?.Invoke("ElevationBrokerClient: the process path is unavailable.");
            return BrokerStartOutcome.Failed;
        }

        string arguments = string.Create(
            CultureInfo.InvariantCulture,
            $"{ElevationBrokerContract.ModeArgument} {ElevationBrokerContract.ParentArgument} {Environment.ProcessId} {ElevationBrokerContract.PipeArgument} {pipeName}");
        if (ExplorerProcessLauncher.TryShellExecute(
                executable,
                arguments,
                workingDirectory: AppContext.BaseDirectory,
                verb: "runas",
                out int launchError,
                out string launchErrorMessage))
            return BrokerStartOutcome.Ready;

        if (launchError == ErrorCancelled)
        {
            _log?.Invoke("ElevationBrokerClient: administrator approval was canceled.");
            return BrokerStartOutcome.Declined;
        }

        _log?.Invoke($"ElevationBrokerClient: broker launch failed: {launchErrorMessage}");
        return BrokerStartOutcome.Failed;
    }

    private async Task<bool> ConnectAsync(string pipeName)
    {
        for (int attempt = 0; attempt < ConnectAttempts; attempt++)
        {
            NamedPipeClientStream pipe = new(
                serverName: ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(ConnectTimeoutMilliseconds).ConfigureAwait(true);
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

    private bool TrySendAndReceive(BrokerRequest request, out BrokerResponse response)
    {
        response = default;
        if (_writer == null || _reader == null) return false;

        try
        {
            _writer.WriteLine(request.ToWire());
            string? line = _reader.ReadLine();
            if (line != null && BrokerResponse.TryParse(line, out response) && response.CorrelationId == request.CorrelationId)
                return true;

            _log?.Invoke("ElevationBrokerClient: no matching response from the broker.");
            return false;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            _log?.Invoke($"ElevationBrokerClient.TrySendAndReceive: {exception.Message}");
            return false;
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
