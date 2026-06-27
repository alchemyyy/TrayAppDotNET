using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace TrayAppDotNETCommon.UI.WarmWindows;

public static class TrayAppDotNETWindowPrimer
{
    public static async Task PrimeAsync(Window window)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            TaskCompletionSource completion = new();
            Dispatcher.UIThread.Post(async void () =>
            {
                try
                {
                    await PrimeAsync(window);
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            });
            await completion.Task;
            return;
        }

        if (window.IsVisible) return;

        bool oldShowActivated = window.ShowActivated;
        bool oldShowInTaskbar = window.ShowInTaskbar;
        double oldOpacity = window.Opacity;
        PixelPoint oldPosition = window.Position;
        ITrayAppDotNETWarmWindow? warmWindow = window as ITrayAppDotNETWarmWindow;

        try
        {
            warmWindow?.IsWarmPriming = true;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Opacity = 0;
            window.Position = new PixelPoint(
                TrayAppDotNETWarmWindowDefaults.OffscreenPosition,
                TrayAppDotNETWarmWindowDefaults.OffscreenPosition);

            ((Window)window).Show();
            window.UpdateLayout();

            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle);

            window.Hide();
        }
        finally
        {
            window.ShowActivated = oldShowActivated;
            window.ShowInTaskbar = oldShowInTaskbar;
            window.Opacity = oldOpacity;
            window.Position = oldPosition;
            if (warmWindow != null) warmWindow.IsWarmPriming = false;
        }
    }
}
