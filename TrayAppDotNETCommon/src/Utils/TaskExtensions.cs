namespace TrayAppDotNETCommon.Utils;

public static class TaskExtensions
{
    /// <summary>
    /// Observes a fire-and-forget task: a fault is logged with the full exception and a cancellation is ignored, so
    /// neither reaches the unobserved-task handler.
    /// </summary>
    public static void ForgetLogged(this Task task, string context)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.IsCompletedSuccessfully) return;

        task.ContinueWith(
            static (completed, state) =>
            {
                if (completed.Exception is { } exception)
                    TADNLog.Log($"{state}: {exception}");
            },
            context,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
