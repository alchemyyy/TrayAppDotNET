using Avalonia.Threading;

namespace TrayAppDotNETCommon.UI.WarmWindows;

public static class TrayAppDotNETWarmWindowResourcePurger
{
    private static int _purgeQueued;

    public static async Task PurgeAsync(Action<Exception>? logError = null)
    {
        if (Interlocked.Exchange(ref _purgeQueued, 1) != 0) return;

        await RunPurgeAsync(logError);
    }

    public static void RequestAfterEviction(Action<Exception>? logError = null)
    {
        if (Interlocked.Exchange(ref _purgeQueued, 1) != 0) return;

        Dispatcher.UIThread.Post(
            () => _ = RunPurgeAsync(logError),
            DispatcherPriority.ContextIdle);
    }

    private static async Task RunPurgeAsync(Action<Exception>? logError)
    {
        try
        {
            await DrainUiAsync();
            await DrainUiAsync();
        }
        catch (Exception ex)
        {
            logError?.Invoke(ex);
        }
        finally
        {
            Interlocked.Exchange(ref _purgeQueued, 0);
        }
    }

    private static async Task DrainUiAsync() =>
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle);
}
