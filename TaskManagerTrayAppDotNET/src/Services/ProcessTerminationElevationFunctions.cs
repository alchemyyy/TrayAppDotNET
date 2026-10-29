namespace TaskManagerTrayAppDotNET.Services;

/// <summary>How the user answered an on-demand request to enable elevated termination.</summary>
internal enum ElevatedTerminationRequestOutcome
{
    /// <summary>The elevated helper is running; refused targets can be retried.</summary>
    Approved,

    /// <summary>The user canceled the in-app prompt or the Windows approval prompt.</summary>
    Declined,

    /// <summary>The elevated helper could not be started for a reason other than the user's choice.</summary>
    Unavailable
}

internal delegate ProcessTerminationBatchResult ProcessTerminationBatchAction(
    IReadOnlyList<ProcessEndTaskItem> processes);

/// <summary>
/// Asks for administrator approval only when ending a selection was refused for lack of rights, then retries just
/// the refused targets through the elevated helper.
/// </summary>
internal static class ProcessTerminationElevationFunctions
{
    /// <summary>Returns whether a refused termination should prompt for the elevated helper.</summary>
    public static bool ShouldRequestElevation(
        ProcessTerminationBatchResult result,
        ElevatedHelperState helperState,
        bool appIsElevated)
    {
        ArgumentNullException.ThrowIfNull(result);

        // An elevated app already ends processes with administrator rights, so approval cannot help
        if (appIsElevated) return false;

        // Starting means a Windows prompt is already open; Ready and Disposed leave nothing to approve
        if (helperState is not (ElevatedHelperState.NotRequested
            or ElevatedHelperState.Declined
            or ElevatedHelperState.Failed))
            return false;

        for (int failureIndex = 0; failureIndex < result.Failures.Count; failureIndex++)
        {
            if (result.Failures[failureIndex].AccessDenied) return true;
        }

        return false;
    }

    /// <summary>Returns each refused selected process once, in selection order.</summary>
    public static List<ProcessEndTaskItem> SelectRetryTargets(ProcessTerminationBatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        List<ProcessEndTaskItem> retryTargets = [];
        for (int failureIndex = 0; failureIndex < result.Failures.Count; failureIndex++)
        {
            ProcessTerminationFailure failure = result.Failures[failureIndex];
            if (failure.AccessDenied && !retryTargets.Contains(failure.Process))
                retryTargets.Add(failure.Process);
        }

        return retryTargets;
    }

    /// <summary>
    /// Replaces every first-pass failure of a retried process with the retry outcome. A retried child keeps its
    /// child labeling, since the retry ran it as its own root.
    /// </summary>
    public static ProcessTerminationBatchResult MergeRetry(
        ProcessTerminationBatchResult first,
        IReadOnlyList<ProcessEndTaskItem> retryTargets,
        ProcessTerminationBatchResult retry)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(retryTargets);
        ArgumentNullException.ThrowIfNull(retry);

        List<ProcessTerminationFailure> failures = new(first.Failures.Count + retry.Failures.Count);
        for (int failureIndex = 0; failureIndex < first.Failures.Count; failureIndex++)
        {
            ProcessTerminationFailure failure = first.Failures[failureIndex];
            if (!ContainsProcess(retryTargets, failure.Process)) failures.Add(failure);
        }

        for (int failureIndex = 0; failureIndex < retry.Failures.Count; failureIndex++)
        {
            ProcessTerminationFailure failure = retry.Failures[failureIndex];
            failures.Add(!failure.IsDescendant && WasDescendant(first, failure.Process)
                ? failure with { IsDescendant = true }
                : failure);
        }

        return new ProcessTerminationBatchResult(
            first.RefreshNeeded || retry.RefreshNeeded,
            failures,
            first.IsMultiple);
    }

    private static bool WasDescendant(ProcessTerminationBatchResult result, ProcessEndTaskItem process)
    {
        for (int failureIndex = 0; failureIndex < result.Failures.Count; failureIndex++)
        {
            ProcessTerminationFailure failure = result.Failures[failureIndex];
            if (failure.IsDescendant && failure.Process == process) return true;
        }

        return false;
    }

    /// <summary>Drops refused failures after the user declined approval; other failures still report.</summary>
    public static ProcessTerminationBatchResult WithoutAccessDenied(ProcessTerminationBatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        List<ProcessTerminationFailure> failures = new(result.Failures.Count);
        for (int failureIndex = 0; failureIndex < result.Failures.Count; failureIndex++)
        {
            ProcessTerminationFailure failure = result.Failures[failureIndex];
            if (!failure.AccessDenied) failures.Add(failure);
        }

        return new ProcessTerminationBatchResult(result.RefreshNeeded, failures, result.IsMultiple);
    }

    /// <summary>
    /// Runs the batch off the UI thread, then, when standard rights were refused, asks once for elevated
    /// termination and retries only the refused processes.
    /// </summary>
    public static async Task<ProcessTerminationBatchResult> ExecuteAsync(
        IReadOnlyList<ProcessEndTaskItem> processes,
        ProcessTerminationBatchAction execute,
        Func<ElevatedHelperState> getHelperState,
        bool appIsElevated,
        Func<Task<ElevatedTerminationRequestOutcome>> requestElevation)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(getHelperState);
        ArgumentNullException.ThrowIfNull(requestElevation);

        ProcessTerminationBatchResult first = await Task.Run(() => execute(processes));
        if (!ShouldRequestElevation(first, getHelperState(), appIsElevated)) return first;

        ElevatedTerminationRequestOutcome outcome = await requestElevation();
        switch (outcome)
        {
            case ElevatedTerminationRequestOutcome.Declined:
                return WithoutAccessDenied(first);
            case ElevatedTerminationRequestOutcome.Unavailable:
                return first;
        }

        List<ProcessEndTaskItem> retryTargets = SelectRetryTargets(first);
        ProcessTerminationBatchResult retry = await Task.Run(() => execute(retryTargets));
        return MergeRetry(first, retryTargets, retry);
    }

    private static bool ContainsProcess(IReadOnlyList<ProcessEndTaskItem> processes, ProcessEndTaskItem process)
    {
        for (int processIndex = 0; processIndex < processes.Count; processIndex++)
        {
            if (processes[processIndex] == process) return true;
        }

        return false;
    }
}
