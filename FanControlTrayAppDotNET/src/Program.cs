namespace FanControlTrayAppDotNET;

internal static class Program
{
    public const string ApplicationName = Constants.ApplicationName;

    public static string AppLocalAppDataDirectory => AppServices.Install.SettingsDirectory;

    /// <summary>The shared %LOCALAPPDATA%\TrayAppDotNET root, which the settings folder override does not move.</summary>
    public static string LocalAppDataRoot => TrayAppDotNETAppInstallServices.LocalAppDataRoot;

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

        // The elevated registration of the account's task writes no file in the user's folders (a same-user process
        // controls them and could redirect an elevated write with a junction); its exit code is the whole report
        if (TrayAppDotNETElevatedStartTask.IsElevatedConfigureLaunch(args))
        {
            TADNLog.InitializeForElevatedProcess(forwarder: null);
            _ = AppServices.ElevatedStart.TryHandleElevatedConfigure(args, out int configureExitCode);
            return configureExitCode;
        }

        // Fan Control always runs as administrator: an unelevated plain start of the system install lets the account's
        // task start it elevated, registering the task first through one UAC prompt when it is missing
        if (AppServices.ElevatedStart.TryHandOffRequiredLaunch(args))
            return 0;

        return RunApplication(args);
    }

    private static int RunApplication(string[] args) =>
        TrayAppDotNETProgram.Run(
            args,
            ApplicationName,
            Constants.AppGUID,
            static () => AppServices.Install.CreateProgramOptions(FanAvaloniaRunner.Run));
}
