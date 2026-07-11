using System.Runtime.InteropServices;
using Avalonia.Threading;
using BrightnessTrayAppDotNET.Interop.DDCCI;
using BrightnessTrayAppDotNET.UI.Display;

namespace BrightnessTrayAppDotNET.Services;

/// <summary>
/// Mirrors the visual behavior of Windows' "Identify" button in Settings &gt; Display: flashes each monitor's
/// display number in a centered card for a few seconds. Windows' own identifier is internal to the Settings app
/// with no public API, so this reimplements the effect.
/// </summary>
public static class DisplayIdentifierService
{
    private static readonly List<DisplayIdentifierOverlayWindow> _active = [];
    private static DispatcherTimer? _timer;

    public static bool IsActive => _active.Count > 0;

    /// <summary>
    /// Hides any currently visible identify overlays and cancels the pending auto-hide timer.
    /// No-op if nothing is showing.
    /// </summary>
    public static void Hide()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Hide);
            return;
        }

        StopTimer();
        CloseAll();
    }

    /// <summary>
    /// Shows a labeled overlay on every connected monitor.
    /// If called again while overlays are already up, the timer is reset rather than stacking.
    /// </summary>
    public static void Show(TimeSpan? duration = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(duration));
            return;
        }

        TimeSpan visibleFor = duration ?? TimeSpan.FromMilliseconds(TimeConstants.DisplayIdentifierDefaultDurationMs);

        // Tear down any prior pass before opening a new one. A tick already queued by the old timer can still run;
        // OnTimerTick checks sender identity so that stale tick cannot stop the replacement timer or overlays
        StopTimer();
        CloseAll();

        foreach ((int number, int x, int y, int w, int h) in EnumerateMonitors())
        {
            DisplayIdentifierOverlayWindow overlayWindow = new(number, x, y, w, h);
            _active.Add(overlayWindow);
            overlayWindow.Show();
        }

        if (_active.Count == 0) return;

        _timer = new DispatcherTimer { Interval = visibleFor };
        _timer.Tick += OnTimerTick;
        try { _timer.Start(); }
        catch
        {
            StopTimer();
            CloseAll();
            throw;
        }
    }

    private static void OnTimerTick(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _timer)) return;
        StopTimer();
        CloseAll();
    }

    private static void StopTimer()
    {
        DispatcherTimer? timer = _timer;
        _timer = null;
        if (timer == null) return;
        try { timer.Stop(); }
        catch (Exception exception)
        {
            WPFLog.Log($"DisplayIdentifierService.StopTimer: {exception.Message}");
        }
        timer.Tick -= OnTimerTick;
    }

    private static void CloseAll()
    {
        foreach (DisplayIdentifierOverlayWindow overlayWindow in _active)
        {
            try { overlayWindow.Close(); }
            catch (Exception exception)
            {
                WPFLog.Log($"DisplayIdentifierService.CloseAll: {exception.Message}");
            }
        }

        _active.Clear();
    }

    private static List<(int Number, int X, int Y, int W, int H)> EnumerateMonitors()
    {
        List<(int, int, int, int, int)> result = [];

        // Match the flyout's badge numbers - same CCD source-id mapping, same fallback. Without this the
        // Identify overlay drifts to "26 / 27 / ..." while the flyout shows "1 / 2 / 3", and the user's mental
        // model of which display is which gets re-broken every time they hit Identify.
        Dictionary<string, int> friendlyByAdapter = CCD.BuildFriendlyDisplayNumberMap();

        User32Monitor.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        return result;

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref User32Monitor.Rect rect, IntPtr data)
        {
            User32Monitor.MonitorInfoEx info = new();
            if (User32Monitor.GetMonitorInfo(new HandleRef(null, hMonitor), info))
            {
                string device = new string(info.szDevice).TrimEnd('\0');
                int number = CCD.ResolveFriendlyDisplayNumber(device, friendlyByAdapter);
                int x = info.rcMonitor.left;
                int y = info.rcMonitor.top;
                int w = info.rcMonitor.right - info.rcMonitor.left;
                int h = info.rcMonitor.bottom - info.rcMonitor.top;
                result.Add((number, x, y, w, h));
            }

            return true;
        }
    }
}
