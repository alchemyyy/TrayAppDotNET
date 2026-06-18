using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using SkiaSharp;

namespace TrayAppDotNETCommon.UI.WarmWindows;

public static class TrayAppDotNETWarmWindowResourcePurger
{
    private const int FirstRenderDrainDelayMs = 150;
    private const int SecondCollectionDelayMs = 150;
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
            await Task.Delay(FirstRenderDrainDelayMs).ConfigureAwait(false);
            await DrainUiAsync();
            await Dispatcher.UIThread.InvokeAsync(TryPurgeSkiaCaches, DispatcherPriority.ContextIdle);

            ForceFullManagedCleanup();
            await Task.Delay(SecondCollectionDelayMs).ConfigureAwait(false);
            ForceFullManagedCleanup();
            TryTrimWorkingSet();
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

    private static void TryPurgeSkiaCaches()
    {
        try
        {
            SKGraphics.PurgeAllCaches();
            SKGraphics.PurgeFontCache();
            SKGraphics.PurgeResourceCache();
        }
        catch
        {
        }
    }

    private static void ForceFullManagedCleanup()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    private static void TryTrimWorkingSet()
    {
        try
        {
            EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        }
        catch
        {
        }
    }

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr process);
}
