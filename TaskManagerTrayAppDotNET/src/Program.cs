using TaskManagerTrayAppDotNET.Services;

namespace TaskManagerTrayAppDotNET;

internal static class Program
{
    public const string ApplicationName = Constants.ApplicationName;

    public static string AppLocalAppDataDirectory => AppServices.Install.SettingsDirectory;

    [STAThread]
    public static int Main(string[] args)
    {
        // Elevated modes write no file in the user's folders: a same-user process controls them and could redirect an
        // elevated write with a junction. The configure mode reports through its exit code; the broker sends its log
        // lines to the UI over its pipe. Both set this up before anything can log.
        if (TaskManagerReplacement.IsElevatedConfigureLaunch(args))
        {
            TADNLog.InitializeForElevatedProcess(forwarder: null);
            _ = TaskManagerReplacement.TryHandleElevatedConfigure(args, out int configureExitCode);
            return configureExitCode;
        }

        // Elevated privileged-action broker: serve the UI's requests over a pipe, no window
        if (ElevationBroker.IsBrokerLaunch(args))
        {
            TADNLog.InitializeForElevatedProcess(ElevationBroker.QueueLogLine);
            return ElevationBroker.Run(args);
        }

        // A taskmgr.exe launch redirected here: bring a running instance forward instead of duplicating.
        // When nothing is running this returns false and we cold-start normally below.
        if (TaskManagerReplacement.TryHandOffRedirectedLaunch(args))
            return 0;

        return TrayAppDotNETProgram.Run(
            args,
            ApplicationName,
            Constants.AppGUID,
            static () => AppServices.Install.CreateProgramOptions(TaskManagerAvaloniaRunner.Run));
    }
}
