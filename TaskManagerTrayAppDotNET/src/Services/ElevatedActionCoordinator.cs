using TaskManagerTrayAppDotNET.Models;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>The UI-facing outcome of a privileged action attempted through the elevated broker.</summary>
internal readonly record struct ElevatedActionResult(bool Serviced, bool Success, bool Declined, string Message)
{
    public static ElevatedActionResult Declination { get; } = new(Serviced: false, Success: false, Declined: true, Message: "");

    public static ElevatedActionResult FailedToStart(string message) =>
        new(Serviced: false, Success: false, Declined: false, message);

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
            bool ok = ProcessNativeActions.TrySetPriority(target, priority, out string message, out _);
            return Task.FromResult(InProcessResult(ok, message));
        }

        return RunAsync(ElevationOp.SetPriority, target.ProcessID, target.CreationTimeFileTime, (ulong)(uint)priority, text: "");
    }

    public Task<ElevatedActionResult> SetAffinityAsync(ProcessTerminationTarget target, ulong affinityMask)
    {
        if (ShouldActInProcess())
        {
            bool ok = ProcessNativeActions.TrySetAffinity(target, affinityMask, out string message, out _);
            return Task.FromResult(InProcessResult(ok, message));
        }

        return RunAsync(ElevationOp.SetAffinity, target.ProcessID, target.CreationTimeFileTime, affinityMask, text: "");
    }

    /// <summary>Creates a memory dump entirely in the broker; the dump path is returned in the result message.</summary>
    public Task<ElevatedActionResult> CreateDumpAsync(ProcessTerminationTarget target)
    {
        if (ShouldActInProcess())
        {
            bool ok = ProcessNativeActions.TryCreateMemoryDump(target, out string dumpPath, out string message);
            return Task.FromResult(InProcessResult(ok, ok ? dumpPath : message));
        }

        return RunAsync(ElevationOp.CreateDump, target.ProcessID, target.CreationTimeFileTime, argument: 0, text: "");
    }

    public Task<ElevatedActionResult> DisconnectSessionAsync(int sessionId)
    {
        if (ShouldActInProcess())
        {
            UserSessionActionResult result = new WindowsUserSessionService(TADNLog.Log).Disconnect(sessionId);
            return Task.FromResult(InProcessResult(result.Succeeded, result.ErrorMessage));
        }

        return RunAsync(ElevationOp.DisconnectSession, processId: 0, creationTimeFileTime: 0, (ulong)(uint)sessionId, text: "");
    }

    public Task<ElevatedActionResult> ServiceControlAsync(string serviceName, BrokerServiceVerb verb)
    {
        if (ShouldActInProcess())
        {
            WindowsServiceOperationResult result = verb switch
            {
                BrokerServiceVerb.Start => WindowsServiceManager.Start(serviceName),
                BrokerServiceVerb.Stop => WindowsServiceManager.Stop(serviceName),
                BrokerServiceVerb.Restart => WindowsServiceManager.Restart(serviceName),
                _ => new WindowsServiceManager().Disable(serviceName)
            };
            return Task.FromResult(InProcessResult(result.Succeeded, result.ErrorMessage));
        }

        return RunAsync(ElevationOp.ServiceControl, processId: 0, creationTimeFileTime: 0, (ulong)verb, serviceName);
    }

    public Task<ElevatedActionResult> SetStartupApprovalAsync(StartupAppApprovalTarget target, bool enabled)
    {
        if (ShouldActInProcess())
        {
            StartupAppActionResult result = StartupAppsService.SetStatusByTarget(
                target,
                enabled ? StartupAppStatus.Enabled : StartupAppStatus.Disabled);
            return Task.FromResult(InProcessResult(result.Succeeded, result.ErrorMessage));
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

        return attempt.StartOutcome == BrokerStartOutcome.Declined
            ? ElevatedActionResult.Declination
            : ElevatedActionResult.FailedToStart("The elevated helper could not be started.");
    }

    public void Dispose() => _client.Dispose();
}
