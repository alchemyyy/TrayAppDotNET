using System.Globalization;
using Microsoft.Win32.SafeHandles;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>The UI-facing outcome of a privileged action attempted through the elevated broker.</summary>
internal readonly record struct ElevatedActionResult(
    bool Serviced,
    bool Success,
    bool Declined,
    string Message,
    bool TimedOut = false)
{
    public static ElevatedActionResult Declination { get; } = new(Serviced: false, Success: false, Declined: true, Message: "");

    /// <summary>The broker never answered: it could not start, the connection dropped, or the request timed out.</summary>
    public static ElevatedActionResult NotServiced(string message, bool timedOut = false) =>
        new(Serviced: false, Success: false, Declined: false, message, timedOut);

    public static ElevatedActionResult FromResponse(BrokerResponse response) =>
        new(Serviced: true, response.Code == BrokerResultCode.Success, Declined: false, response.Message);
}

/// <summary>
/// The single app-level entry point the UI uses to run a privileged process action through the elevated
/// broker. It owns the broker client and the one-time in-app consent prompt, so call sites only need
/// <c>AppServices.ElevatedActions</c> and never thread the client or a confirmation dialog through their
/// constructors. Consent is requested lazily on the first action that needs it and reused thereafter.
/// </summary>
internal sealed class ElevatedActionCoordinator : IDisposable
{
    private readonly ElevationBrokerClient _client;
    private readonly Func<Task<bool>> _confirmAsync;

    public ElevatedActionCoordinator(Func<Task<bool>> confirmAsync, Action<string>? log)
    {
        ArgumentNullException.ThrowIfNull(confirmAsync);
        _confirmAsync = confirmAsync;
        _client = new ElevationBrokerClient(log);
    }

    public Task<ElevatedActionResult> SetPriorityAsync(ProcessTerminationTarget target, ProcessPriorityLevel priority)
    {
        if (ShouldActInProcess())
        {
            return RunInProcess(() => InProcessResult(
                ProcessNativeActions.TrySetPriority(target, priority, out string message, out _),
                message));
        }

        return RunAsync(ElevationOp.SetPriority, target.ProcessID, target.CreationTimeFileTime, (ulong)(uint)priority, text: "");
    }

    public Task<ElevatedActionResult> SetAffinityAsync(ProcessTerminationTarget target, ulong affinityMask)
    {
        if (ShouldActInProcess())
        {
            return RunInProcess(() => InProcessResult(
                ProcessNativeActions.TrySetAffinity(target, affinityMask, out string message, out _),
                message));
        }

        return RunAsync(ElevationOp.SetAffinity, target.ProcessID, target.CreationTimeFileTime, affinityMask, text: "");
    }

    /// <summary>
    /// Creates a memory dump; on success the result message is the dump path. Through the broker, this unelevated
    /// process creates the file in its own temp folder and sends only the handle value: the broker copies the
    /// handle out of this process and writes into it, so no administrator process ever creates a file in a folder a
    /// same-user process controls.
    /// </summary>
    public Task<ElevatedActionResult> CreateDumpAsync(ProcessTerminationTarget target)
    {
        if (ShouldActInProcess())
        {
            return RunInProcess(() =>
            {
                bool ok = ProcessNativeActions.TryCreateMemoryDump(target, out string dumpPath, out string message);
                return InProcessResult(ok, ok ? dumpPath : message);
            });
        }

        return CreateDumpThroughBrokerAsync(target);
    }

    private async Task<ElevatedActionResult> CreateDumpThroughBrokerAsync(ProcessTerminationTarget target)
    {
        (bool created, string dumpPath, SafeFileHandle? file, string fileError) = await Task.Run(() =>
        {
            bool ok = ProcessNativeActions.TryCreateDumpFile(
                target,
                out string path,
                out SafeFileHandle? handle,
                out string error);
            return (ok, path, handle, error);
        }).ConfigureAwait(true);
        if (!created || file == null) return InProcessResult(succeeded: false, fileError);

        ElevatedActionResult result;
        using (file)
        {
            // The handle stays open until the broker answered; the broker writes through its own duplicate
            result = await RunAsync(
                ElevationOp.CreateDump,
                target.ProcessID,
                target.CreationTimeFileTime,
                argument: (ulong)file.DangerousGetHandle(),
                text: "").ConfigureAwait(true);
        }

        if (result is { Serviced: true, Success: true }) return result with { Message = dumpPath };

        // A timed-out broker may still be writing through its copy, so the file stays and its path is reported
        if (result.TimedOut) return result with { Message = FormatDumpTimedOutMessage(dumpPath) };

        ProcessNativeActions.DiscardDumpFile(dumpPath);
        return result;
    }

    public Task<ElevatedActionResult> DisconnectSessionAsync(int sessionId)
    {
        if (ShouldActInProcess())
        {
            return RunInProcess(() =>
            {
                UserSessionActionResult result = new WindowsUserSessionService(TADNLog.Log).Disconnect(sessionId);
                return InProcessResult(result.Succeeded, result.ErrorMessage);
            });
        }

        return RunAsync(ElevationOp.DisconnectSession, processId: 0, creationTimeFileTime: 0, (ulong)(uint)sessionId, text: "");
    }

    public Task<ElevatedActionResult> ServiceControlAsync(string serviceName, BrokerServiceVerb verb)
    {
        if (ShouldActInProcess())
        {
            return RunInProcess(() =>
            {
                WindowsServiceOperationResult result = verb switch
                {
                    BrokerServiceVerb.Start => WindowsServiceManager.Start(serviceName),
                    BrokerServiceVerb.Stop => WindowsServiceManager.Stop(serviceName),
                    BrokerServiceVerb.Restart => WindowsServiceManager.Restart(serviceName),
                    _ => new WindowsServiceManager().Disable(serviceName)
                };
                return InProcessResult(result.Succeeded, result.ErrorMessage);
            });
        }

        return RunAsync(ElevationOp.ServiceControl, processId: 0, creationTimeFileTime: 0, (ulong)verb, serviceName);
    }

    public Task<ElevatedActionResult> SetStartupApprovalAsync(StartupAppApprovalTarget target, bool enabled)
    {
        if (ShouldActInProcess())
        {
            return RunInProcess(() =>
            {
                StartupAppActionResult result = StartupAppsService.SetStatusByTarget(
                    target,
                    enabled ? StartupAppStatus.Enabled : StartupAppStatus.Disabled);
                return InProcessResult(result.Succeeded, result.ErrorMessage);
            });
        }

        return RunAsync(
            ElevationOp.SetStartupApproval,
            processId: 0,
            creationTimeFileTime: 0,
            enabled ? 1UL : 0UL,
            StartupApprovalWire.Encode(target));
    }

    /// <summary>An elevated process with the bypass setting enabled performs admin actions itself, no broker.</summary>
    private static bool ShouldActInProcess() =>
        AppElevation.IsElevated && (AppServices.Settings?.BypassElevationBrokerWhenElevated ?? false);

    private static ElevatedActionResult InProcessResult(bool succeeded, string message) =>
        new(Serviced: true, succeeded, Declined: false, message);

    /// <summary>
    /// Runs an in-process admin action on the thread pool: service waits and dumps can take many seconds, and the
    /// callers are UI continuations.
    /// </summary>
    private static Task<ElevatedActionResult> RunInProcess(Func<ElevatedActionResult> action) => Task.Run(action);

    private async Task<ElevatedActionResult> RunAsync(
        ElevationOp op,
        int processId,
        long creationTimeFileTime,
        ulong argument,
        string text)
    {
        BrokerAttempt attempt = await _client.RequestAsync(
            op,
            processId,
            creationTimeFileTime,
            argument,
            text,
            _confirmAsync).ConfigureAwait(true);
        if (attempt.Serviced) return ElevatedActionResult.FromResponse(attempt.Response);

        return attempt.Outcome switch
        {
            BrokerAttemptOutcome.Declined => ElevatedActionResult.Declination,
            BrokerAttemptOutcome.TimedOut => ElevatedActionResult.NotServiced(FormatTimedOutMessage(op), timedOut: true),
            _ => ElevatedActionResult.NotServiced(
                LocalizationManager.Instance[nameof(AppStrings.Elevation_HelperStartFailed)])
        };
    }

    /// <summary>The broker received the request but did not answer in time, so the action may still complete.</summary>
    internal static string FormatTimedOutMessage(ElevationOp op) =>
        string.Format(
            CultureInfo.CurrentCulture,
            LocalizationManager.Instance[nameof(AppStrings.Elevation_TimedOutFormat)],
            ElevationBrokerContract.ResponseTimeout(op).TotalSeconds);

    /// <summary>A timed-out dump keeps its file: the broker may still be writing into it through its copy.</summary>
    internal static string FormatDumpTimedOutMessage(string dumpPath) =>
        string.Format(
            CultureInfo.CurrentCulture,
            LocalizationManager.Instance[nameof(AppStrings.Elevation_DumpTimedOutFormat)],
            ElevationBrokerContract.DumpOperationTimeout.TotalSeconds,
            dumpPath);

    public void Dispose() => _client.Dispose();
}
