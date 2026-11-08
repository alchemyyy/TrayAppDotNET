using TaskManagerTrayAppDotNET.Services;

namespace TaskManagerTrayAppDotNET;

internal static class Program
{
    public const string ApplicationName = Constants.ApplicationName;

    public static string AppLocalAppDataDirectory => AppServices.Install.SettingsDirectory;

    [STAThread]
    public static int Main(string[] args)
    {
        // A start by the Always run as administrator task runs elevated with arguments any program running as the user
        // can set, so it is checked before every other mode and may only become a plain start
        if (TrayAppDotNETElevatedStartTask.IsTaskLaunch(args))
        {
            return TrayAppDotNETElevatedStartTask.TryGetTaskLaunchArguments(args, out string[] launchArguments)
                ? RunApplication(launchArguments)
                : TrayAppDotNETElevatedStartTask.RejectedTaskLaunchExitCode;
        }

        // Elevated modes write no file in the user's folders: a same-user process controls them and could redirect an
        // elevated write with a junction. The configure modes report through their exit code; the broker sends its log
        // lines to the UI over its pipe. All of them set this up before anything can log.
        if (TaskManagerReplacement.IsElevatedConfigureLaunch(args))
        {
            TADNLog.InitializeForElevatedProcess(forwarder: null);
            _ = TaskManagerReplacement.TryHandleElevatedConfigure(args, out int configureExitCode);
            return configureExitCode;
        }

        if (TrayAppDotNETElevatedStartTask.IsElevatedConfigureLaunch(args))
        {
            TADNLog.InitializeForElevatedProcess(forwarder: null);
            _ = AppServices.ElevatedStart.TryHandleElevatedConfigure(args, out int configureExitCode);
            return configureExitCode;
        }

        // Elevated privileged-action broker: serve the UI's requests over a pipe, no window
        if (ElevationBroker.IsBrokerLaunch(args))
        {
            TADNLog.InitializeForElevatedProcess(ElevationBroker.QueueLogLine);
            return ElevationBroker.Run(args);
        }

        // A plain start while an instance runs brings that instance forward instead of replacing it. Checked before the
        // Always run as administrator hand-off, whose elevated start would otherwise take the running instance over.
        if (TaskManagerActivationServer.TryHandOffPlainLaunch(args))
            return 0;

        // A taskmgr.exe launch redirected here: bring a running instance forward instead of duplicating.
        // When nothing is running this returns false and we cold-start normally below.
        if (TaskManagerReplacement.TryHandOffRedirectedLaunch(args))
            return 0;

        // Always run as administrator: an unelevated plain start lets the account's task start this exe elevated
        if (AppServices.ElevatedStart.TryHandOffLaunch(args))
            return 0;

        return RunApplication(args);
    }

    private static int RunApplication(string[] args) =>
        TrayAppDotNETProgram.Run(
            args,
            ApplicationName,
            Constants.AppGUID,
            static () => AppServices.Install.CreateProgramOptions(TaskManagerAvaloniaRunner.Run));
}
