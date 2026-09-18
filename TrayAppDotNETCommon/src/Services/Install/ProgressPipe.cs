using System.IO.Pipes;
using System.Text;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.Services.Install;

/// <summary>
/// Receives progress lines from an elevated install helper over a named pipe. The normal-integrity app
/// owns the server, the helper started through the UAC "runas" verb is the client, mirroring UpdateInstaller.
/// </summary>
public sealed class TrayAppDotNETProgressPipeServer : IDisposable
{
    private const string PipeNamePrefix = "TrayAppDotNET.Progress";
    private const string LogPrefix = "TrayAppDotNETProgressPipeServer";
    private const int ReaderBufferSize = 1024;
    private const int DisposeWaitMilliseconds = 2_000;

    private readonly NamedPipeServerStream _stream;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly IProgress<TrayAppDotNETInstallProgress> _target;
    private readonly Action<string> _log;
    private readonly Task _loop;
    private int _hasConnected;
    private int _disposed;
    private int _reportingThreadID;

    public TrayAppDotNETProgressPipeServer(
        string pipeName,
        IProgress<TrayAppDotNETInstallProgress> target,
        Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(target);

        PipeName = pipeName;
        _target = target;
        _log = log ?? TADNLog.Log;
        _stream = new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        _loop = ReadLoopAsync(_cancellation.Token);
    }

    /// <summary>Builds a pipe name that is unique per application, process, and install attempt.</summary>
    public static string CreatePipeName(string applicationName) =>
        $"{PipeNamePrefix}.{applicationName}.{Environment.ProcessId}.{Guid.NewGuid():N}";

    public string PipeName { get; }

    /// <summary>True once a client has connected, even if it has since disconnected.</summary>
    public bool HasConnected => Volatile.Read(ref _hasConnected) != 0;

    /// <summary>
    /// Completes, and never faults, when the client disconnects, when the read loop ends for any other reason,
    /// or when <see cref="Dispose"/> runs before any client connected.
    /// </summary>
    public Task Completion => _loop;

    /// <summary>Stops listening and waits briefly for the read loop. Idempotent and never throws.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        try
        {
            _cancellation.Cancel();
        }
        catch (Exception exception)
        {
            _log($"{LogPrefix}: cancelling the read loop failed: {exception.Message}");
        }

        try
        {
            _stream.Dispose();
        }
        catch (Exception exception)
        {
            _log($"{LogPrefix}: closing the pipe failed: {exception.Message}");
        }

        // A target that disposes the server from inside Report runs on the loop thread; waiting there
        // would only stall until the timeout because the loop cannot finish until Report returns
        bool calledFromReport = Volatile.Read(ref _reportingThreadID) == Environment.CurrentManagedThreadId;
        if (!calledFromReport)
        {
            try
            {
                if (!_loop.Wait(DisposeWaitMilliseconds))
                    _log($"{LogPrefix}: read loop did not finish within {DisposeWaitMilliseconds} ms");
            }
            catch (Exception exception)
            {
                _log($"{LogPrefix}: waiting for the read loop failed: {exception.Message}");
            }
        }

        Safe.Dispose(_cancellation);
    }

    /// <summary>Waits for the client, then forwards every parsable line until the client disconnects.</summary>
    private async Task ReadLoopAsync(CancellationToken token)
    {
        bool ignoredLineLogged = false;
        try
        {
            await _stream.WaitForConnectionAsync(token).ConfigureAwait(false);
            Volatile.Write(ref _hasConnected, 1);

            using StreamReader reader = new(
                _stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: false,
                ReaderBufferSize,
                leaveOpen: true);
            while (true)
            {
                string? line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line == null) break;

                if (TrayAppDotNETInstallProgress.TryParseLine(line, out TrayAppDotNETInstallProgress? progress))
                {
                    Volatile.Write(ref _reportingThreadID, Environment.CurrentManagedThreadId);
                    try
                    {
                        _target.Report(progress);
                    }
                    finally
                    {
                        Volatile.Write(ref _reportingThreadID, 0);
                    }

                    continue;
                }

                // Only the first stray line is worth a log entry; the helper may echo diagnostics
                if (ignoredLineLogged) continue;
                ignoredLineLogged = true;
                _log($"{LogPrefix}: ignored non-progress line: {line}");
            }
        }
        catch (Exception exception)
        {
            // Cancellation and disposal are the normal shutdown paths and are not worth logging
            if (token.IsCancellationRequested) return;
            if (exception is OperationCanceledException or ObjectDisposedException) return;
            _log($"{LogPrefix}: read loop ended: {exception}");
        }
    }
}

/// <summary>
/// Sends progress lines from the elevated install helper to the server owned by the app that launched it.
/// Reports are dropped silently after the first write failure so a vanished parent never aborts the install.
/// </summary>
public sealed class TrayAppDotNETProgressPipeClient : IProgress<TrayAppDotNETInstallProgress>, IDisposable
{
    private const string LogPrefix = "TrayAppDotNETProgressPipeClient";
    private const string LineTerminator = "\n";
    private const int WriterBufferSize = 1024;

    private readonly Lock _gate = new();
    private readonly NamedPipeClientStream _stream;
    private readonly StreamWriter _writer;
    private readonly Action<string> _log;
    private bool _broken;
    private bool _disposed;

    private TrayAppDotNETProgressPipeClient(NamedPipeClientStream stream, Action<string> log)
    {
        _stream = stream;
        _log = log;
        _writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            WriterBufferSize,
            leaveOpen: true) { AutoFlush = true };
    }

    /// <summary>
    /// Connects to the server pipe within <paramref name="timeoutMilliseconds"/>. Returns null, after logging,
    /// when the pipe does not exist, times out, or refuses the caller.
    /// </summary>
    public static TrayAppDotNETProgressPipeClient? TryConnect(
        string pipeName,
        int timeoutMilliseconds,
        Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        Action<string> logger = log ?? TADNLog.Log;

        NamedPipeClientStream stream = new(
            serverName: ".",
            pipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous);
        try
        {
            stream.Connect(timeoutMilliseconds);
            return new TrayAppDotNETProgressPipeClient(stream, logger);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException)
        {
            logger($"{LogPrefix}: could not connect to {pipeName}: {exception.Message}");
            Safe.Dispose(stream);
            return null;
        }
    }

    /// <summary>Writes one progress line. Drops the line, after logging once, when the pipe is broken or disposed.</summary>
    public void Report(TrayAppDotNETInstallProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (_gate)
        {
            if (_disposed || _broken) return;

            try
            {
                _writer.Write(value.ToLine() + LineTerminator);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                _broken = true;
                _log($"{LogPrefix}: progress write failed; later reports are dropped: {exception.Message}");
            }
        }
    }

    /// <summary>Flushes and closes the pipe. Idempotent and never throws.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            if (!_broken)
            {
                try
                {
                    _writer.Flush();
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                    _log($"{LogPrefix}: final flush failed: {exception.Message}");
                }
            }

            // A writer over a broken pipe throws again from Dispose, so the swallowing helper is deliberate here
            Safe.Dispose(_writer);
            Safe.Dispose(_stream);
        }
    }
}
