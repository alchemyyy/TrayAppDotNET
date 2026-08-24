using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace TrayAppDotNETCommon.UI.Controls;

/// <summary>Runs the shared installer window and exits after applying or cancelling its selection.</summary>
public static class TrayAppDotNETInstallerRunner
{
    public static void Show(Application application, TrayAppDotNETInstallerWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);

        if (application.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            throw new InvalidOperationException("The installer requires a desktop application lifetime.");

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        TrayAppDotNETInstallerWindow window = new(options);
        desktop.MainWindow = window;

        EventHandler? closedHandler = null;
        closedHandler = (_, _) =>
        {
            window.Closed -= closedHandler;
            int exitCode = window.Result is { } result
                ? TrayAppDotNETProgram.RunInstallerSelection(result.Scope, result.InstallOptions)
                : 0;
            window.Dispose();
            desktop.Shutdown(exitCode);
        };
        window.Closed += closedHandler;
        window.Show();
    }
}
