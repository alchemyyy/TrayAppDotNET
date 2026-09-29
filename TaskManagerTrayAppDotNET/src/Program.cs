using TaskManagerTrayAppDotNET.Services;

namespace TaskManagerTrayAppDotNET;

internal static class Program
{
    public static int? WatcherPID => TrayAppDotNETProgram.WatcherPID;

    public const string ApplicationName = Constants.ApplicationName;
    public const string SharedRootFolderName = Constants.SharedRootFolderName;

    public static string LocalAppDataRoot =>
        TrayAppDotNETProgram.LocalAppDataRoot(SharedRootFolderName);

    public static string AppLocalAppDataDirectory =>
        TrayAppDotNETProgram.AppLocalAppDataDirectory(ApplicationName, SharedRootFolderName);

    public static bool IsUninstallerMode => TrayAppDotNETProgram.IsUninstallerMode;

    public static bool IsInstallerMode => TrayAppDotNETProgram.IsInstallerMode;

    public static bool IsStartupLaunch => TrayAppDotNETProgram.IsStartupLaunch;

    public static string? UninstallerInstallDir => TrayAppDotNETProgram.UninstallerInstallDir;

    public static InstallScope UninstallerScope => TrayAppDotNETProgram.UninstallerScope;

    public static int Main(string[] args)
    {
        // Elevated self-relaunch that only flips the taskmgr.exe redirect, then exits without a UI
        if (TaskManagerReplacement.TryHandleElevatedConfigure(args, out int configureExitCode))
            return configureExitCode;

        // Elevated privileged-action broker: serve the UI's requests over a pipe, no window
        if (ElevationBroker.IsBrokerLaunch(args))
            return ElevationBroker.Run(args);

        // A taskmgr.exe launch redirected here: bring a running instance forward instead of duplicating.
        // When nothing is running this returns false and we cold-start normally below.
        if (TaskManagerReplacement.TryHandOffRedirectedLaunch(args))
            return 0;

        return TrayAppDotNETProgram.Run(args, ApplicationName, Constants.AppGUID, CreateProgramOptions);
    }

    private static TrayAppDotNETProgramOptions CreateProgramOptions() =>
        new(
            ApplicationName,
            SharedRootFolderName,
            Constants.AppGUID,
            TaskManagerAvaloniaRunner.Run,
            (sourceExecutable, buildNumber, installOptions, progress) => TrayAppDotNETProgramInstallResult.From(
                AppServices.Installation.RunAdminInstallSystem(sourceExecutable, buildNumber, installOptions, progress)),
            (removingScope, allUsers) => AppServices.StartMenu.Sync(removingScope, allUsers),
            (scope, deleteSettings, progress) =>
            {
                // Clear the taskmgr.exe redirect before removal so Ctrl+Shift+Esc never points at a deleted exe
                TaskManagerReplacement.RemoveForUninstall();
                return TrayAppDotNETProgramInstallResult.From(
                    AppServices.Installation.PrepareUninstall(scope, deleteSettings, progress));
            },
            (scope, deleteSettings, progress) => AppServices.Installation.RunUninstall(
                scope,
                deleteSettings,
                static () => Environment.Exit(0),
                progress),
            (installOptions, progress) => TrayAppDotNETProgramInstallResult.From(AppServices.Installation.InstallToLocalAppData(installOptions: installOptions, progress: progress)),
            (installOptions, progress) => TrayAppDotNETProgramInstallResult.From(AppServices.Installation.InstallSystemWide(installOptions: installOptions, progress: progress)),
            () => AppServices.InstallLayout.LocalAppDataInstallExecutable,
            () => AppServices.InstallLayout.ProgramFilesInstallExecutable,
            TADNLog.Log,
            TADNLog.Flush);
}
