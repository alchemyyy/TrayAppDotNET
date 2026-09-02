using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;

namespace BrightnessTrayAppDotNET.DDCCI;

/// <summary>
/// Parent-side client for the killable DDC helper process.
/// </summary>
internal sealed class DDCHelperClient : IDisposable
{
    private const int CommandAttempts = 2;
    private const int HelperConnectTimeoutMs = 5000;

    private readonly Lock _gate = new();
    private Process? _process;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private NamedPipeServerStream? _pipe;
    private volatile bool _disposed;

    public DDCCallOutcome<string> TryGetCapabilities(
        DDCMonitor monitor,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        string command = DDCHelperProtocol.BuildCommand(verb: "CAPS", monitor);
        DDCCallOutcome<string[]> response = SendCommand(
            command,
            timeoutMs,
            cancellationToken,
            $"GetCapabilities('{monitor.Name}')");
        if (!response.Success) return DDCCallOutcome<string>.Fail(response.Error ?? "DDC helper failed.");
        if (response.Value.Length < 2)
            return DDCCallOutcome<string>.Fail("DDC helper returned a malformed CAPS reply.");

        try
        {
            return DDCCallOutcome<string>.Ok(DDCHelperProtocol.DecodeField(response.Value[1]));
        }
        catch (Exception ex)
        {
            return DDCCallOutcome<string>.Fail($"DDC helper CAPS decode failed: {ex.Message}");
        }
    }

    public DDCCallOutcome<(uint Cur, uint Max)> TryGetVCPFeature(
        DDCMonitor monitor,
        byte code,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        string command = DDCHelperProtocol.BuildCommand(verb: "GETVCP", monitor,
            code.ToString(format: "X2", CultureInfo.InvariantCulture));
        DDCCallOutcome<string[]> response = SendCommand(
            command,
            timeoutMs,
            cancellationToken,
            $"TryGetVCPFeature('{monitor.Name}', 0x{code:X2})");
        if (!response.Success) return DDCCallOutcome<(uint, uint)>.Fail(response.Error ?? "DDC helper failed.");
        if (response.Value.Length < 3)
            return DDCCallOutcome<(uint, uint)>.Fail("DDC helper returned a malformed GETVCP reply.");

        if (!uint.TryParse(response.Value[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint current))
            return DDCCallOutcome<(uint, uint)>.Fail("DDC helper returned an invalid current VCP value.");

        if (!uint.TryParse(response.Value[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint maximum))
            return DDCCallOutcome<(uint, uint)>.Fail("DDC helper returned an invalid maximum VCP value.");

        return DDCCallOutcome<(uint, uint)>.Ok((current, maximum));
    }

    public DDCCallOutcome<bool> TrySetVCPFeature(
        DDCMonitor monitor,
        byte code,
        uint value,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        string command = DDCHelperProtocol.BuildCommand(
            verb: "SETVCP",
            monitor,
            code.ToString(format: "X2", CultureInfo.InvariantCulture),
            value.ToString(CultureInfo.InvariantCulture));
        DDCCallOutcome<string[]> response = SendCommand(
            command,
            timeoutMs,
            cancellationToken,
            $"TrySetVCPFeature('{monitor.Name}', 0x{code:X2}={value})");
        if (!response.Success) return DDCCallOutcome<bool>.Fail(response.Error ?? "DDC helper failed.");

        return DDCCallOutcome<bool>.Ok(true);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        KillProcess(_process);

        lock (_gate)
            StopHelperProcess(kill: false, sendExit: false);
    }

    private DDCCallOutcome<string[]> SendCommand(
        string command,
        int timeoutMs,
        CancellationToken cancellationToken,
        string opLabel)
    {
        if (cancellationToken.IsCancellationRequested)
            return DDCCallOutcome<string[]>.WithError($"DDC op '{opLabel}' cancelled by sequence deadline.");

        long startTimestamp = Stopwatch.GetTimestamp();

        lock (_gate)
        {
            if (_disposed) return DDCCallOutcome<string[]>.Fail("DDC helper client is disposed.");

            for (int attempt = 0; attempt < CommandAttempts; attempt++)
            {
                if (_disposed) return DDCCallOutcome<string[]>.Fail("DDC helper client is disposed.");

                int remainingTimeoutMs = GetRemainingTimeoutMs(timeoutMs, startTimestamp);
                if (remainingTimeoutMs == 0)
                {
                    return DDCCallOutcome<string[]>.WithError(
                        $"DDC op '{opLabel}' exceeded {timeoutMs}ms end-to-end timeout waiting for its helper slot.");
                }

                if (!EnsureStarted(remainingTimeoutMs, cancellationToken, out string? startError))
                    return DDCCallOutcome<string[]>.Fail(startError ?? "DDC helper could not start.");

                StreamWriter writer = _writer!;
                StreamReader reader = _reader!;
                try
                {
                    writer.WriteLine(command);
                    writer.Flush();
                }
                catch (Exception ex)
                {
                    StopHelperProcess(kill: true, sendExit: false);
                    if (attempt + 1 < CommandAttempts) continue;

                    return DDCCallOutcome<string[]>.Fail($"DDC helper command write failed: {ex.Message}");
                }

                Task<string?> readTask;
                try
                {
                    readTask = reader.ReadLineAsync();
                }
                catch (Exception ex)
                {
                    StopHelperProcess(kill: true, sendExit: false);
                    if (attempt + 1 < CommandAttempts) continue;

                    return DDCCallOutcome<string[]>.Fail($"DDC helper reply read failed: {ex.Message}");
                }

                int effectiveTimeoutMs = GetRemainingTimeoutMs(timeoutMs, startTimestamp);
                if (effectiveTimeoutMs == 0)
                {
                    StopHelperProcess(kill: true, sendExit: false);
                    return DDCCallOutcome<string[]>.WithError(
                        $"DDC op '{opLabel}' exceeded {timeoutMs}ms end-to-end timeout; helper process killed.");
                }

                try
                {
                    if (!readTask.Wait(effectiveTimeoutMs, cancellationToken))
                    {
                        StopHelperProcess(kill: true, sendExit: false);
                        return DDCCallOutcome<string[]>.WithError(
                            $"DDC op '{opLabel}' exceeded {timeoutMs}ms timeout; helper process killed.");
                    }
                }
                catch (OperationCanceledException)
                {
                    StopHelperProcess(kill: true, sendExit: false);
                    return DDCCallOutcome<string[]>.WithError(
                        $"DDC op '{opLabel}' cancelled by sequence deadline; helper process killed.");
                }
                catch (Exception ex)
                {
                    StopHelperProcess(kill: true, sendExit: false);
                    if (attempt + 1 < CommandAttempts) continue;

                    return DDCCallOutcome<string[]>.Fail($"DDC helper wait failed: {ex.Message}");
                }

                string? responseLine;
                try
                {
                    responseLine = readTask.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    StopHelperProcess(kill: true, sendExit: false);
                    if (attempt + 1 < CommandAttempts) continue;

                    return DDCCallOutcome<string[]>.Fail($"DDC helper reply failed: {ex.Message}");
                }

                if (responseLine == null)
                {
                    StopHelperProcess(kill: false, sendExit: false);
                    if (attempt + 1 < CommandAttempts) continue;

                    return DDCCallOutcome<string[]>.Fail("DDC helper exited without a reply.");
                }

                return ParseResponse(responseLine);
            }
        }

        return DDCCallOutcome<string[]>.Fail("DDC helper failed after retry.");
    }

    private bool EnsureStarted(
        int timeoutMs,
        CancellationToken cancellationToken,
        out string? error)
    {
        error = null;
        if (_disposed)
        {
            error = "DDC helper client is disposed.";
            return false;
        }

        if (_process is { HasExited: false } && _writer != null && _reader != null)
            return true;

        StopHelperProcess(kill: false, sendExit: false);

        string? executablePath = ResolveHelperExecutablePath(out error);
        if (executablePath == null) return false;

        long helperStartTimestamp = Stopwatch.GetTimestamp();
        string pipeName = "BrightnessTrayAppDDC_"
                          + Environment.ProcessId.ToString(CultureInfo.InvariantCulture)
                          + "_"
                          + Guid.NewGuid().ToString("N");
        NamedPipeServerStream pipe = new(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add(DDCHelperProtocol.ServerArgument);
        startInfo.ArgumentList.Add(DDCHelperProtocol.ParentProcessIDArgument);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(DDCHelperProtocol.PipeNameArgument);
        startInfo.ArgumentList.Add(pipeName);
        startInfo.Environment[Constants.NoWatcherEnvironmentVariable] = "1";

        try
        {
            Process? process = Process.Start(startInfo);
            if (process == null)
            {
                error = "Process.Start returned null.";
                pipe.Dispose();
                return false;
            }

            _process = process;
            _pipe = pipe;

            Task connectTask = pipe.WaitForConnectionAsync(cancellationToken);
            int connectTimeoutMs = timeoutMs > 0
                ? Math.Min(HelperConnectTimeoutMs, timeoutMs)
                : HelperConnectTimeoutMs;
            if (!connectTask.Wait(connectTimeoutMs, cancellationToken))
            {
                error = "DDC helper did not connect to its command pipe.";
                StopHelperProcess(kill: true, sendExit: false);
                return false;
            }

            StreamWriter writer = new(
                pipe,
                DDCHelperProtocol.PipeEncoding,
                bufferSize: 1024,
                leaveOpen: true)
            {
                AutoFlush = true
            };
            StreamReader reader = new(
                pipe,
                DDCHelperProtocol.PipeEncoding,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            _writer = writer;
            _reader = reader;

            Task<string?> readyTask = reader.ReadLineAsync();
            int handshakeTimeoutMs = GetRemainingTimeoutMs(timeoutMs, helperStartTimestamp);
            if (handshakeTimeoutMs == 0 || !readyTask.Wait(handshakeTimeoutMs, cancellationToken))
            {
                error = "DDC helper did not complete its startup handshake.";
                StopHelperProcess(kill: true, sendExit: false);
                return false;
            }

            string? readyResponse = readyTask.GetAwaiter().GetResult();
            if (!DDCHelperProtocol.IsReadyResponse(readyResponse))
            {
                error = "DDC helper returned an incompatible startup handshake.";
                StopHelperProcess(kill: true, sendExit: false);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to start DDC helper: {ex.Message}";
            StopHelperProcess(kill: true, sendExit: false);
            try { pipe.Dispose(); }
            catch (Exception cleanupException)
            {
                TADNLog.Log($"DDCHelperClient.EnsureStarted pipe cleanup failed: {cleanupException.Message}");
            }

            return false;
        }
    }

    private static string? ResolveHelperExecutablePath(out string? error)
    {
#if BRIGHTNESS_NATIVE_AOT
        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            error = "Current executable path is unavailable.";
            return null;
        }
#else
        string executablePath = Path.Combine(AppContext.BaseDirectory, Constants.NativeHelpersFileName);
        if (!File.Exists(executablePath))
        {
            error = $"Native DDC helper was not found at '{executablePath}'.";
            return null;
        }
#endif

        error = null;
        return executablePath;
    }

    private static int GetRemainingTimeoutMs(int timeoutMs, long startTimestamp)
    {
        if (timeoutMs <= 0) return Timeout.Infinite;

        long elapsedMs = (long)Math.Ceiling(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
        return (int)Math.Max(val1: 0, timeoutMs - elapsedMs);
    }

    private void StopHelperProcess(bool kill, bool sendExit)
    {
        Process? process = _process;
        StreamWriter? writer = _writer;
        StreamReader? reader = _reader;
        NamedPipeServerStream? pipe = _pipe;
        _process = null;
        _writer = null;
        _reader = null;
        _pipe = null;

        if (sendExit && writer != null)
        {
            try
            {
                writer.WriteLine(DDCHelperProtocol.ExitCommand);
                writer.Flush();
            }
            catch (Exception ex)
            {
                TADNLog.Log($"DDCHelperClient.StopHelperProcess EXIT failed: {ex.Message}");
            }
        }

        try { writer?.Dispose(); }
        catch (Exception ex)
        {
            TADNLog.Log($"DDCHelperClient.StopHelperProcess writer dispose failed: {ex.Message}");
        }

        try { reader?.Dispose(); }
        catch (Exception ex)
        {
            TADNLog.Log($"DDCHelperClient.StopHelperProcess reader dispose failed: {ex.Message}");
        }

        try { pipe?.Dispose(); }
        catch (Exception ex)
        {
            TADNLog.Log($"DDCHelperClient.StopHelperProcess pipe dispose failed: {ex.Message}");
        }

        if (process == null) return;

        try
        {
            if (kill)
                KillProcess(process);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"DDCHelperClient.StopHelperProcess kill failed: {ex.Message}");
        }

        try
        {
            if (!process.HasExited)
            {
                bool exited = process.WaitForExit(TimeConstants.ProcessExitDrainTimeoutMs);
                if (!exited)
                {
                    TADNLog.Log(
                        $"DDCHelperClient.StopHelperProcess: PID {process.Id} did not exit within "
                        + $"{TimeConstants.ProcessExitDrainTimeoutMs}ms after {(kill ? "kill" : "disconnect")}");
                }
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"DDCHelperClient.StopHelperProcess wait failed: {ex.Message}");
        }

        try { process.Dispose(); }
        catch (Exception ex)
        {
            TADNLog.Log($"DDCHelperClient.StopHelperProcess process dispose failed: {ex.Message}");
        }
    }

    private static void KillProcess(Process? process)
    {
        if (process == null) return;

        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"DDCHelperClient.KillProcess failed: {ex.Message}");
        }
    }

    private static DDCCallOutcome<string[]> ParseResponse(string responseLine)
    {
        string[] fields = responseLine.Split('\t');
        if (fields.Length == 0) return DDCCallOutcome<string[]>.Fail("DDC helper returned an empty reply.");

        switch (fields[0])
        {
            case "OK":
                return DDCCallOutcome<string[]>.Ok(fields);
            case "FAIL":
                if (fields.Length < 2) return DDCCallOutcome<string[]>.Fail("DDC helper returned failure.");
                try { return DDCCallOutcome<string[]>.Fail(DDCHelperProtocol.DecodeField(fields[1])); }
                catch (Exception ex)
                {
                    return DDCCallOutcome<string[]>.Fail($"DDC helper failure decode failed: {ex.Message}");
                }
            default:
                return DDCCallOutcome<string[]>.Fail("DDC helper returned an unknown response.");
        }
    }
}
