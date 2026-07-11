using Avalonia.Controls;
using Avalonia.Threading;

namespace TrayAppDotNETCommon.UI.WarmWindows;

public interface ITrayAppDotNETWarmWindow
{
    bool IsWarmPriming { get; set; }
    bool IsManagedByWarmSlot { get; set; }
    event EventHandler? WarmDismissed;
    void DismissForWarmCache();
    void CloseForWarmEviction();
}

public interface ITrayAppDotNETWarmResourceOwner
{
    /// <summary>Releases heavyweight hidden resources while keeping the warm window alive.</summary>
    void TrimHiddenWarmResources();

    /// <summary>Releases all warm-window owned resources before final eviction or close.</summary>
    void DisposeWarmResources();
}

public static class TrayAppDotNETWarmWindowDefaults
{
    public const int OffscreenPosition = -32000;
}

public sealed class TrayAppDotNETWarmWindowSlot<TWindow>(
    Func<bool> isKeepWarmEnabled,
    Action<Exception>? logError = null)
    : IDisposable
    where TWindow : Window
{
    private DispatcherTimer? _evictionTimer;
    private EventHandler? _evictionTickHandler;
    private long _evictionVersion;
    private bool _disposed;
    private bool _evicting;

    public TWindow? Cached { get; private set; }

    public async Task PrimeAsync(Func<TWindow> createWindow)
    {
        if (_disposed || !isKeepWarmEnabled()) return;

        TWindow window = TakeOrCreate(createWindow);
        if (window.IsVisible) return;

        try
        {
            await TrayAppDotNETWindowPrimer.PrimeAsync(window);
        }
        catch
        {
            if (ReferenceEquals(Cached, window))
                EvictNow();
            throw;
        }
    }

    public TWindow TakeOrCreate(Func<TWindow> createWindow)
    {
        ThrowIfDisposed();
        CancelIdleEviction();
        if (Cached != null) return Cached;

        TWindow window = createWindow();
        Cached = window;
        try
        {
            window.Closed += OnWindowClosed;
            if (window is ITrayAppDotNETWarmWindow warmWindow)
            {
                warmWindow.IsManagedByWarmSlot = true;
                warmWindow.WarmDismissed += OnWarmDismissed;
            }

            return window;
        }
        catch
        {
            Detach(window);
            DisposeWindowWarmResources(window);
            if (ReferenceEquals(Cached, window)) Cached = null;
            TryCloseWindow(window);
            throw;
        }
    }

    public void MarkDismissed()
    {
        if (_disposed) return;
        if (isKeepWarmEnabled())
        {
            CancelIdleEviction();
            return;
        }

        ScheduleIdleEviction();
    }

    public void ApplyKeepWarmPolicy(Func<TWindow> createWindow)
    {
        if (_disposed) return;
        if (isKeepWarmEnabled())
        {
            CancelIdleEviction();
            _ = PrimeWithoutThrowAsync(createWindow);
        }
        else if (Cached is { IsVisible: false }) ScheduleIdleEviction();
    }

    public void Invalidate()
    {
        if (_disposed) return;
        if (Cached == null) return;
        if (Cached.IsVisible) return;

        EvictNow();
    }

    public void EvictNow()
    {
        if (_disposed) return;
        CancelIdleEviction();
        EvictCachedWindow();
    }

    private void EvictCachedWindow()
    {
        TWindow? window = Cached;
        if (window == null) return;

        _evicting = true;
        try
        {
            if (window is ITrayAppDotNETWarmWindow warmWindow)
                warmWindow.CloseForWarmEviction();
            else
                window.Close();
        }
        catch (Exception ex)
        {
            Log(ex);
        }
        finally
        {
            _evicting = false;
            if (ReferenceEquals(Cached, window))
            {
                DisposeWindowWarmResources(window);
                Detach(window);
                Cached = null;
            }
            TrayAppDotNETWarmWindowResourcePurger.RequestAfterEviction(logError);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelIdleEviction();
        EvictCachedWindow();
    }

    private void OnWarmDismissed(object? sender, EventArgs e)
    {
        if (_disposed || _evicting) return;
        if (sender is not TWindow window || !ReferenceEquals(window, Cached)) return;
        MarkDismissed();
    }

    private void ScheduleIdleEviction()
    {
        if (Cached == null) return;

        CancelIdleEviction();
        long version = ++_evictionVersion;
        DispatcherTimer timer = new()
        {
            Interval = TimeSpan.FromMilliseconds(TimeConstants.WarmWindowIdleEvictionDelayMs)
        };
        EventHandler tickHandler = (sender, e) => OnEvictionTimerTick(timer, version);
        _evictionTimer = timer;
        _evictionTickHandler = tickHandler;
        timer.Tick += tickHandler;
        try
        {
            timer.Start();
        }
        catch
        {
            CancelIdleEviction();
            throw;
        }
    }

    private void CancelIdleEviction()
    {
        _evictionVersion++;
        DispatcherTimer? timer = _evictionTimer;
        EventHandler? tickHandler = _evictionTickHandler;
        _evictionTimer = null;
        _evictionTickHandler = null;
        if (timer == null) return;

        TryCleanup(timer.Stop);
        if (tickHandler != null)
            TryCleanup(() => timer.Tick -= tickHandler);
    }

    private void OnEvictionTimerTick(DispatcherTimer timer, long version)
    {
        if (_disposed) return;
        if (!ReferenceEquals(timer, _evictionTimer) || version != _evictionVersion) return;

        CancelIdleEviction();
        if (Cached is { IsVisible: true }) return;
        if (isKeepWarmEnabled()) return;

        EvictNow();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not TWindow window || !ReferenceEquals(window, Cached)) return;
        CancelIdleEviction();
        DisposeWindowWarmResources(window);
        Detach(window);
        Cached = null;
    }

    private void Detach(TWindow window)
    {
        TryCleanup(() => window.Closed -= OnWindowClosed);
        if (window is ITrayAppDotNETWarmWindow warmWindow)
        {
            TryCleanup(() => warmWindow.WarmDismissed -= OnWarmDismissed);
            TryCleanup(() => warmWindow.IsManagedByWarmSlot = false);
            TryCleanup(() => warmWindow.IsWarmPriming = false);
        }
    }

    private void DisposeWindowWarmResources(TWindow window)
    {
        try
        {
            if (window is ITrayAppDotNETWarmResourceOwner resourceOwner)
                resourceOwner.DisposeWarmResources();
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    private async Task PrimeWithoutThrowAsync(Func<TWindow> createWindow)
    {
        try
        {
            await PrimeAsync(createWindow);
        }
        catch (Exception exception)
        {
            Log(exception);
        }
    }

    private void TryCloseWindow(TWindow window)
    {
        try
        {
            if (window is ITrayAppDotNETWarmWindow warmWindow)
                warmWindow.CloseForWarmEviction();
            else
                window.Close();
        }
        catch (Exception exception)
        {
            Log(exception);
        }
    }

    private void TryCleanup(Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            Log(exception);
        }
    }

    private void Log(Exception exception)
    {
        if (logError != null)
        {
            try
            {
                logError(exception);
                return;
            }
            catch (Exception loggerException)
            {
                TADNLog.Log($"Warm-window logger failed: {loggerException.Message}");
            }
        }

        TADNLog.Log($"Warm-window cleanup failed: {exception.Message}");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }
}
