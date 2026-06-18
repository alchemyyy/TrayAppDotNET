using Avalonia.Controls;
using Avalonia.Threading;
using TrayAppDotNETCommon.Timing;

namespace TrayAppDotNETCommon.UI.WarmWindows;

public interface ITrayAppDotNETWarmWindow
{
    bool IsWarmPriming { get; set; }
    bool IsManagedByWarmSlot { get; set; }
    event EventHandler? WarmDismissed;
    void DismissForWarmCache();
    void CloseForWarmEviction();
}

public static class TrayAppDotNETWarmWindowDefaults
{
    public const int OffscreenPosition = -32000;
}

public sealed class TrayAppDotNETWarmWindowSlot<TWindow> : IDisposable
    where TWindow : Window
{
    private readonly Func<bool> _isKeepWarmEnabled;
    private readonly Action<Exception>? _logError;
    private DispatcherTimer? _evictionTimer;
    private bool _disposed;
    private bool _evicting;

    public TrayAppDotNETWarmWindowSlot(Func<bool> isKeepWarmEnabled, Action<Exception>? logError = null)
    {
        _isKeepWarmEnabled = isKeepWarmEnabled;
        _logError = logError;
    }

    public TWindow? Cached { get; private set; }

    public async Task PrimeAsync(Func<TWindow> createWindow)
    {
        if (_disposed || !_isKeepWarmEnabled()) return;

        TWindow window = TakeOrCreate(createWindow);
        if (window.IsVisible) return;

        await TrayAppDotNETWindowPrimer.PrimeAsync(window);
    }

    public TWindow TakeOrCreate(Func<TWindow> createWindow)
    {
        ThrowIfDisposed();
        CancelIdleEviction();
        if (Cached != null) return Cached;

        TWindow window = createWindow();
        Cached = window;
        window.Closed += OnWindowClosed;
        if (window is ITrayAppDotNETWarmWindow warmWindow)
        {
            warmWindow.IsManagedByWarmSlot = true;
            warmWindow.WarmDismissed += OnWarmDismissed;
        }

        return window;
    }

    public void MarkDismissed()
    {
        if (_disposed) return;
        if (_isKeepWarmEnabled())
        {
            CancelIdleEviction();
            return;
        }

        ScheduleIdleEviction();
    }

    public void ApplyKeepWarmPolicy(Func<TWindow> createWindow)
    {
        if (_disposed) return;
        if (_isKeepWarmEnabled())
        {
            CancelIdleEviction();
            _ = PrimeAsync(createWindow);
        }
        else if (Cached is { IsVisible: false })
        {
            ScheduleIdleEviction();
        }
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
            _logError?.Invoke(ex);
        }
        finally
        {
            _evicting = false;
            Detach(window);
            if (ReferenceEquals(Cached, window)) Cached = null;
            TrayAppDotNETWarmWindowResourcePurger.RequestAfterEviction(_logError);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        EvictNow();
        _disposed = true;
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

        _evictionTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(TrayAppDotNETTimeConstants.WarmWindowIdleEvictionDelayMs),
        };
        _evictionTimer.Tick -= OnEvictionTimerTick;
        _evictionTimer.Tick += OnEvictionTimerTick;
        _evictionTimer.Stop();
        _evictionTimer.Start();
    }

    private void CancelIdleEviction()
    {
        if (_evictionTimer == null) return;
        _evictionTimer.Stop();
        _evictionTimer.Tick -= OnEvictionTimerTick;
    }

    private void OnEvictionTimerTick(object? sender, EventArgs e)
    {
        CancelIdleEviction();
        if (Cached is { IsVisible: true }) return;
        if (_isKeepWarmEnabled()) return;

        EvictNow();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not TWindow window || !ReferenceEquals(window, Cached)) return;
        Detach(window);
        Cached = null;
        CancelIdleEviction();
    }

    private void Detach(TWindow window)
    {
        window.Closed -= OnWindowClosed;
        if (window is ITrayAppDotNETWarmWindow warmWindow)
        {
            warmWindow.WarmDismissed -= OnWarmDismissed;
            warmWindow.IsManagedByWarmSlot = false;
            warmWindow.IsWarmPriming = false;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }
}
