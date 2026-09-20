using System.Runtime.InteropServices;
using TrayAppDotNETInstaller.Services;

namespace TrayAppDotNETInstaller;

internal static class Program
{
    private const string FatalCaption = "TrayAppDotNET Installer";
    private const int FatalExitCode = 1;
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxIconError = 0x00000010;

    // WPF on the .NET Framework keeps its pre-4.6.2 behaviour of ignoring DPI changes unless this switch is
    // turned off. The application manifest already declares per-monitor v2 awareness, so switching the
    // legacy behaviour off is what lets the window rescale when it moves to a display with another scale.
    private const string DoNotScaleForDpiChangesSwitch = "Switch.System.Windows.DoNotScaleForDpiChanges";

    [STAThread]
    public static int Main(string[] args)
    {
        // The elevated worker never opens a window, so it must not drag WPF into the process
        if (ElevatedInstallWorker.IsWorkerInvocation(args))
            return ElevatedInstallWorker.Run(args);

        // Factory mode is a console tool that stamps a copy of this image, so it stays clear of WPF too
        if (InstallerFactory.IsFactoryInvocation(args))
            return InstallerFactory.Run(args);

        try
        {
            // Must precede the first touch of WPF, which reads the switch when its static state is built
            AppContext.SetSwitch(DoNotScaleForDpiChangesSwitch, isEnabled: false);

            bool isExample = ExampleMode.IsRequested(args, out string? exampleApplicationName);
            // The catalog holds a handle on the appended archive until the process is on its way out
            EmbeddedPayloadCatalog catalog = isExample
                ? ExampleMode.CreateCatalog(exampleApplicationName)
                : EmbeddedPayloadCatalog.Load();
#if DEBUG
            // Running the project produces a payload-free Debug build, which as a real installer could only
            // report that it has nothing to install. A stamped installer is a Release build and always
            // carries a payload, so this can never turn one of those into an example.
            if (!isExample && catalog.Payloads.Count == 0)
            {
                InstallerLog.Write("Program: a Debug build with no payload; opening the example window instead");
                catalog.Dispose();
                catalog = ExampleMode.CreateCatalog(applicationName: null);
                isExample = true;
            }
#endif
            using (catalog)
            {
                App application = new(catalog, isExample, ExampleMode.IsFailureRequested(args));
                return application.Run();
            }
        }
        catch (Exception exception)
        {
            InstallerLog.Write("Program.Main: fatal startup failure", exception);
            ShowFatalError(exception);
            return FatalExitCode;
        }
    }

    private static void ShowFatalError(Exception exception)
    {
        string text =
            $"The installer could not start.{Environment.NewLine}{Environment.NewLine}" +
            $"{exception.Message}{Environment.NewLine}{Environment.NewLine}" +
            $"Log: {InstallerLog.FilePath}";
        _ = MessageBoxW(IntPtr.Zero, text, FatalCaption, MessageBoxOk | MessageBoxIconError);
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr windowHandle, string text, string caption, uint type);
}
