using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.Services.Ipc;

/// <summary>
/// Parent side of a helper process: the app's own executable started in a helper mode with a private command pipe.
/// The helper gets <c>--parent-pid</c>, <c>--parent-start</c> and <c>--pipe-name</c> (see
/// <see cref="TrayAppDotNETHelperHost"/>), plus the settings folder override, and its connection is accepted only from
/// the process this call started.
/// </summary>
public sealed class TrayAppDotNETHelperProcess : IDisposable
{
    private TrayAppDotNETHelperProcess(Process process, NamedPipeServerStream pipe)
    {
        Process = process;
        Pipe = pipe;
    }

    public Process Process { get; }

    /// <summary>The server end; usable once <see cref="WaitForConnectionAsync"/> completed.</summary>
    public NamedPipeServerStream Pipe { get; }

    /// <summary>
    /// Creates the pipe, then starts this executable with <paramref name="modeArgument"/>, the parent identity, the
    /// pipe name and <paramref name="extraArguments"/>. Throws when the executable path is unavailable or the start
    /// fails; nothing is left behind then.
    /// </summary>
    public static TrayAppDotNETHelperProcess Start(
        string modeArgument,
        string pipeNamePrefix,
        IEnumerable<string> extraArguments)
    {
        string executablePath = Environment.ProcessPath
                                ?? throw new InvalidOperationException("Current executable path is unavailable.");
        string pipeName = TrayAppDotNETPipeSecurity.CreatePipeName(pipeNamePrefix);
        NamedPipeServerStream pipe = TrayAppDotNETPipeSecurity.CreateServer(pipeName, PipeDirection.InOut);
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add(modeArgument);
            startInfo.ArgumentList.Add(TrayAppDotNETHelperHost.ParentProcessIdArgument);
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(TrayAppDotNETHelperHost.ParentStartArgument);
            startInfo.ArgumentList.Add(ProcessExitWatch.CurrentProcessStartFileTime.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(TrayAppDotNETHelperHost.PipeNameArgument);
            startInfo.ArgumentList.Add(pipeName);
            foreach (string argument in extraArguments) startInfo.ArgumentList.Add(argument);
            TrayAppDotNETProgram.ConfigureHelperProcess(startInfo);

            Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process.Start returned null.");
            return new TrayAppDotNETHelperProcess(process, pipe);
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Waits for the helper to connect, then rejects the connection unless its client process is the helper this
    /// object started (<see cref="UnauthorizedAccessException"/>).
    /// </summary>
    public async Task WaitForConnectionAsync(CancellationToken cancellationToken)
    {
        await Pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (TrayAppDotNETPipeSecurity.IsClient(Pipe, Process.Id)) return;

        TrayAppDotNETPipeSecurity.TryGetClientProcessId(Pipe, out int clientProcessId);
        throw new UnauthorizedAccessException(
            $"The helper pipe was opened by PID {clientProcessId}, not by helper PID {Process.Id}.");
    }

    /// <summary>Kills the helper (and its children) if it still runs; logs failures.</summary>
    public void Kill()
    {
        try
        {
            if (!Process.HasExited) Process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException
                                              or NotSupportedException)
        {
            TADNLog.Log($"TrayAppDotNETHelperProcess: could not kill helper PID {Process.Id}: {exception.Message}");
        }
    }

    public void Dispose()
    {
        Pipe.Dispose();
        Process.Dispose();
    }
}

/// <summary>Helper side of <see cref="TrayAppDotNETHelperProcess"/>.</summary>
public static class TrayAppDotNETHelperHost
{
    public const string ParentProcessIdArgument = "--parent-pid";
    public const string ParentStartArgument = "--parent-start";
    public const string PipeNameArgument = "--pipe-name";

    // Keeps the parent watch alive for the helper's lifetime
    private static IDisposable? _parentWatch;

    /// <summary>
    /// Ends this helper (exit code 0) when the parent named on the command line exits. The parent is identified by
    /// PID and start time, so a recycled PID counts as gone. Returns false, after logging, when the arguments are
    /// missing or the parent is already gone; the helper should then exit. A parent that cannot be watched (access
    /// denied) is logged and tolerated: the closing pipe still ends the helper.
    /// </summary>
    public static bool TryWatchParent(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!TryReadParent(args, out int parentProcessId, out long parentStartFileTime))
        {
            TADNLog.Log("TrayAppDotNETHelperHost: missing or invalid --parent-pid / --parent-start.");
            return false;
        }

        switch (ProcessExitWatch.TryRegister(
                    parentProcessId,
                    parentStartFileTime,
                    static () => Environment.Exit(0),
                    out IDisposable? registration))
        {
            case ProcessExitWatchResult.Watching:
                _parentWatch = registration;
                return true;
            case ProcessExitWatchResult.Exited:
                TADNLog.Log($"TrayAppDotNETHelperHost: parent PID {parentProcessId} is gone.");
                return false;
            default:
                TADNLog.Log($"TrayAppDotNETHelperHost: parent PID {parentProcessId} cannot be watched; relying on the pipe.");
                return true;
        }
    }

    /// <summary>
    /// Connects to the parent's pipe and accepts it only when it is owned by this user and served by the parent
    /// process. Returns null, after logging, otherwise.
    /// </summary>
    public static NamedPipeClientStream? TryConnectToParent(string[] args, int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? pipeName = TrayAppDotNETProgram.TryGetArgValue(args, PipeNameArgument);
        if (string.IsNullOrWhiteSpace(pipeName) || !TryReadParent(args, out int parentProcessId, out _))
        {
            TADNLog.Log("TrayAppDotNETHelperHost: missing --pipe-name or parent identity.");
            return null;
        }

        NamedPipeClientStream pipe = TrayAppDotNETPipeSecurity.CreateClient(pipeName, PipeDirection.InOut);
        try
        {
            pipe.Connect(timeoutMs);
            if (TrayAppDotNETPipeSecurity.IsServerOwnedByCurrentUser(pipe)
                && TrayAppDotNETPipeSecurity.IsServer(pipe, parentProcessId))
                return pipe;

            TADNLog.Log($"TrayAppDotNETHelperHost: pipe '{pipeName}' is not served by parent PID {parentProcessId}.");
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            TADNLog.Log($"TrayAppDotNETHelperHost: connecting to the parent failed: {exception.Message}");
        }

        pipe.Dispose();
        return null;
    }

    internal static bool TryReadParent(string[] args, out int parentProcessId, out long parentStartFileTime)
    {
        parentStartFileTime = 0;
        parentProcessId = 0;
        return int.TryParse(
                   TrayAppDotNETProgram.TryGetArgValue(args, ParentProcessIdArgument),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out parentProcessId)
               && parentProcessId > 0
               && long.TryParse(
                   TrayAppDotNETProgram.TryGetArgValue(args, ParentStartArgument),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out parentStartFileTime)
               && parentStartFileTime > 0;
    }
}

/// <summary>Outcome mapping shared by every <c>runas</c> launch.</summary>
public static class TrayAppDotNETElevatedLaunch
{
    /// <summary>True when a start failed because the user declined the UAC prompt.</summary>
    public static bool IsUserCancelled(int win32ErrorOrHResult) =>
        win32ErrorOrHResult is NativeErrors.ERROR_CANCELLED or NativeErrors.HRESULT_ERROR_CANCELLED;

    /// <summary>True when <see cref="Process.Start(ProcessStartInfo)"/> failed because the user declined the UAC prompt.</summary>
    public static bool IsUserCancelled(Win32Exception exception) =>
        IsUserCancelled(exception.NativeErrorCode);
}
