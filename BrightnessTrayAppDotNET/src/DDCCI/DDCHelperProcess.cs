using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;

namespace BrightnessTrayAppDotNET.DDCCI;

/// <summary>
/// Parent-side client for the killable DDC helper process.
/// </summary>
internal sealed class DDCHelperClient : IDisposable
{
    private const int CommandAttempts = 2;
    private const int HelperConnectTimeoutMs = 5000;
    private const string NoWatcherEnvironmentVariable = "TrayAppDotNET_NO_WATCHER";
    internal static readonly Encoding PipeEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

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
        string command = BuildCommand("CAPS", monitor);
        DDCCallOutcome<string[]> response = SendCommand(
            command,
            timeoutMs,
            cancellationToken,
            $"GetCapabilities('{monitor.Name}')");
        if (!response.Success) return DDCCallOutcome<string>.Fail(response.Error ?? "DDC helper failed.");
        if (response.Value.Length < 2) return DDCCallOutcome<string>.Fail("DDC helper returned a malformed CAPS reply.");

        try
        {
            return DDCCallOutcome<string>.Ok(DecodeField(response.Value[1]));
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
        string command = BuildCommand("GETVCP", monitor, code.ToString("X2", CultureInfo.InvariantCulture));
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
        string command = BuildCommand(
            "SETVCP",
            monitor,
            code.ToString("X2", CultureInfo.InvariantCulture),
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

        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            error = "Current executable path is unavailable.";
            return false;
        }

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
        startInfo.ArgumentList.Add(DDCHelperServer.ServerArg);
        startInfo.ArgumentList.Add(DDCHelperServer.ParentProcessIDArg);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(DDCHelperServer.PipeNameArg);
        startInfo.ArgumentList.Add(pipeName);
        startInfo.Environment[NoWatcherEnvironmentVariable] = "1";

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

            _writer = new StreamWriter(pipe, PipeEncoding, bufferSize: 1024, leaveOpen: true)
            {
                AutoFlush = true
            };
            _reader = new StreamReader(pipe, PipeEncoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to start DDC helper: {ex.Message}";
            StopHelperProcess(kill: true, sendExit: false);
            try { pipe.Dispose(); }
            catch (Exception cleanupException)
            {
                WPFLog.Log($"DDCHelperClient.EnsureStarted pipe cleanup failed: {cleanupException.Message}");
            }

            return false;
        }
    }

    private static int GetRemainingTimeoutMs(int timeoutMs, long startTimestamp)
    {
        if (timeoutMs <= 0) return Timeout.Infinite;

        long elapsedMs = (long)Math.Ceiling(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
        return (int)Math.Max(0, timeoutMs - elapsedMs);
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
                writer.WriteLine(DDCHelperServer.ExitCommand);
                writer.Flush();
            }
            catch (Exception ex)
            {
                WPFLog.Log($"DDCHelperClient.StopHelperProcess EXIT failed: {ex.Message}");
            }
        }

        try { writer?.Dispose(); }
        catch (Exception ex)
        {
            WPFLog.Log($"DDCHelperClient.StopHelperProcess writer dispose failed: {ex.Message}");
        }

        try { reader?.Dispose(); }
        catch (Exception ex)
        {
            WPFLog.Log($"DDCHelperClient.StopHelperProcess reader dispose failed: {ex.Message}");
        }

        try { pipe?.Dispose(); }
        catch (Exception ex)
        {
            WPFLog.Log($"DDCHelperClient.StopHelperProcess pipe dispose failed: {ex.Message}");
        }

        if (process == null) return;

        try
        {
            if (kill)
                KillProcess(process);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"DDCHelperClient.StopHelperProcess kill failed: {ex.Message}");
        }

        try
        {
            if (!kill && !process.HasExited)
                _ = process.WaitForExit(TimeConstants.ProcessExitDrainTimeoutMs);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"DDCHelperClient.StopHelperProcess wait failed: {ex.Message}");
        }

        try { process.Dispose(); }
        catch (Exception ex)
        {
            WPFLog.Log($"DDCHelperClient.StopHelperProcess process dispose failed: {ex.Message}");
        }
    }

    private static void KillProcess(Process? process)
    {
        if (process == null) return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"DDCHelperClient.KillProcess failed: {ex.Message}");
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
                try { return DDCCallOutcome<string[]>.Fail(DecodeField(fields[1])); }
                catch (Exception ex)
                {
                    return DDCCallOutcome<string[]>.Fail($"DDC helper failure decode failed: {ex.Message}");
                }
            default:
                return DDCCallOutcome<string[]>.Fail("DDC helper returned an unknown response.");
        }
    }

    private static string BuildCommand(string verb, DDCMonitor monitor, params string[] arguments)
    {
        StringBuilder builder = new(verb);
        AppendEncodedField(builder, monitor.DeviceID);
        AppendEncodedField(builder, monitor.EDIDSerial);
        AppendEncodedField(builder, monitor.Name);
        AppendEncodedField(builder, monitor.DisplayInstancePath);
        foreach (string argument in arguments)
        {
            builder.Append('\t');
            builder.Append(argument);
        }

        return builder.ToString();
    }

    private static void AppendEncodedField(StringBuilder builder, string value)
    {
        builder.Append('\t');
        builder.Append(EncodeField(value));
    }

    internal static string EncodeField(string value) =>
        Convert.ToBase64String(PipeEncoding.GetBytes(value));

    internal static string DecodeField(string value) =>
        PipeEncoding.GetString(Convert.FromBase64String(value));
}

/// <summary>
/// Helper-process entry point used before the normal tray application startup path.
/// </summary>
internal static class DDCHelperServer
{
    public const string ServerArg = "--ddc-helper-server";
    public const string ParentProcessIDArg = "--parent-pid";
    public const string PipeNameArg = "--pipe-name";
    public const string ExitCommand = "EXIT";
    private const int HelperPipeConnectTimeoutMs = 5000;

    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (!HasArg(args, ServerArg)) return false;

        StartParentWatchdog(ParseParentProcessID(args));
        string? pipeName = ParseArgValue(args, PipeNameArg);
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            exitCode = 1;
            return true;
        }

        try
        {
            using NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.InOut);
            pipe.Connect(HelperPipeConnectTimeoutMs);
            using StreamReader reader = new(
                pipe,
                DDCHelperClient.PipeEncoding,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            using StreamWriter writer = new(pipe, DDCHelperClient.PipeEncoding, bufferSize: 1024, leaveOpen: true)
            {
                AutoFlush = true
            };
            using DisplayService displayService = new(useHelperProcess: false) { OperationTimeoutMs = 0 };
            RunLoop(displayService, reader, writer);
            return true;
        }
        catch (Exception ex)
        {
            WPFLog.Log($"DDCHelperServer.TryRun failed: {ex.Message}");
            exitCode = 1;
            return true;
        }
    }

    private static void RunLoop(DisplayService displayService, StreamReader reader, StreamWriter writer)
    {
        while (reader.ReadLine() is { } line)
        {
            if (line.Equals(ExitCommand, StringComparison.Ordinal))
                return;

            string response;
            try
            {
                response = HandleCommand(displayService, line);
            }
            catch (Exception ex)
            {
                response = Fail($"DDC helper command failed: {ex.Message}");
            }

            writer.WriteLine(response);
            writer.Flush();
        }
    }

    private static string HandleCommand(DisplayService displayService, string line)
    {
        string[] fields = line.Split('\t');
        if (fields.Length < 5) return Fail("Malformed DDC helper command.");

        DDCHelperMonitorIdentity identity;
        try
        {
            identity = new DDCHelperMonitorIdentity(
                DDCHelperClient.DecodeField(fields[1]),
                DDCHelperClient.DecodeField(fields[2]),
                DDCHelperClient.DecodeField(fields[3]),
                DDCHelperClient.DecodeField(fields[4]));
        }
        catch (Exception ex)
        {
            return Fail($"Malformed DDC helper identity: {ex.Message}");
        }

        if (!TryResolveMonitor(displayService, identity, out DDCMonitor monitor, out string? resolveError))
            return Fail(resolveError ?? "Monitor not found.");

        return fields[0] switch
        {
            "CAPS" => HandleCapabilities(displayService, monitor),
            "GETVCP" => HandleGetVCP(displayService, monitor, fields),
            "SETVCP" => HandleSetVCP(displayService, monitor, fields),
            _ => Fail("Unknown DDC helper command.")
        };
    }

    private static string HandleCapabilities(DisplayService displayService, DDCMonitor monitor)
    {
        if (!displayService.TryGetCapabilities(monitor, out string capabilities, out string? error))
            return Fail(error ?? "Capabilities request failed.");

        return "OK\t" + DDCHelperClient.EncodeField(capabilities);
    }

    private static string HandleGetVCP(DisplayService displayService, DDCMonitor monitor, string[] fields)
    {
        if (fields.Length < 6) return Fail("Malformed GETVCP command.");
        if (!byte.TryParse(fields[5], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte code))
            return Fail("Malformed GETVCP code.");

        if (!displayService.TryGetVCPFeature(
                monitor,
                code,
                out uint current,
                out uint maximum,
                out string? error))
            return Fail(error ?? "GetVCP request failed.");

        return "OK\t"
               + current.ToString(CultureInfo.InvariantCulture)
               + "\t"
               + maximum.ToString(CultureInfo.InvariantCulture);
    }

    private static string HandleSetVCP(DisplayService displayService, DDCMonitor monitor, string[] fields)
    {
        if (fields.Length < 7) return Fail("Malformed SETVCP command.");
        if (!byte.TryParse(fields[5], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte code))
            return Fail("Malformed SETVCP code.");

        if (!uint.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value))
            return Fail("Malformed SETVCP value.");

        if (!displayService.TrySetVCPFeature(monitor, code, value, out string? error))
            return Fail(error ?? "SetVCP request failed.");

        return "OK";
    }

    private static bool TryResolveMonitor(
        DisplayService displayService,
        DDCHelperMonitorIdentity identity,
        out DDCMonitor monitor,
        out string? error)
    {
        monitor = null!;
        error = null;

        if (!displayService.TryGetMonitors(out IReadOnlyList<DDCMonitor> monitors, out string? enumError))
        {
            error = "Monitor enumeration failed: " + enumError;
            return false;
        }

        foreach (DDCMonitor candidate in monitors)
        {
            if (!Matches(candidate, identity)) continue;

            monitor = candidate;
            return true;
        }

        error = "No matching monitor was found in the DDC helper process.";
        return false;
    }

    private static bool Matches(DDCMonitor monitor, DDCHelperMonitorIdentity identity)
    {
        if (!string.IsNullOrEmpty(identity.DeviceID)
            && string.Equals(monitor.DeviceID, identity.DeviceID, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(identity.DisplayInstancePath)
            && string.Equals(
                monitor.DisplayInstancePath,
                identity.DisplayInstancePath,
                StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(identity.EDIDSerial)
            && string.Equals(monitor.EDIDSerial, identity.EDIDSerial, StringComparison.Ordinal))
            return true;

        return !string.IsNullOrEmpty(identity.Name)
               && string.Equals(monitor.Name, identity.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static string Fail(string error) =>
        "FAIL\t" + DDCHelperClient.EncodeField(error);

    private static bool HasArg(string[] args, string name)
    {
        foreach (string arg in args)
        {
            if (arg.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static int? ParseParentProcessID(string[] args)
    {
        string? value = ParseArgValue(args, ParentProcessIDArg);
        if (value != null
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parentProcessID))
            return parentProcessID;

        return null;
    }

    private static string? ParseArgValue(string[] args, string name)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }

    private static void StartParentWatchdog(int? parentProcessID)
    {
        if (parentProcessID is not ({ } parentPID and > 0)) return;

        Thread watchdog = new(() => WatchParent(parentPID))
        {
            IsBackground = true,
            Name = "BrightnessTrayApp.DDCHelperParentWatchdog"
        };
        watchdog.Start();
    }

    private static void WatchParent(int parentProcessID)
    {
        try
        {
            using Process parent = Process.GetProcessById(parentProcessID);
            parent.WaitForExit();
        }
        catch (Exception ex)
        {
            WPFLog.Log($"DDCHelperServer parent watchdog ended: {ex.Message}");
        }

        Environment.Exit(0);
    }

    private readonly struct DDCHelperMonitorIdentity(
        string deviceID,
        string edidSerial,
        string name,
        string displayInstancePath)
    {
        public readonly string DeviceID = deviceID;
        public readonly string EDIDSerial = edidSerial;
        public readonly string Name = name;
        public readonly string DisplayInstancePath = displayInstancePath;
    }
}
