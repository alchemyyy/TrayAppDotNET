namespace VolumeTrayAppDotNET;

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

    public static string? UninstallerInstallDir => TrayAppDotNETProgram.UninstallerInstallDir;

    public static InstallScope UninstallerScope => TrayAppDotNETProgram.UninstallerScope;

    public static int Main(string[] args) =>
        TrayAppDotNETProgram.Run(args, ApplicationName, Constants.AppGUID, CreateProgramOptions);

    private static TrayAppDotNETProgramOptions CreateProgramOptions() =>
        new(
            ApplicationName,
            SharedRootFolderName,
            Constants.AppGUID,
            VolumeAvaloniaRunner.Run,
            (sourceExe, buildNumber, installOptions, progress) => TrayAppDotNETProgramInstallResult.From(
                AppServices.Installation.RunAdminInstallSystem(sourceExe, buildNumber, installOptions, progress)),
            (removingScope, allUsers) => AppServices.StartMenu.Sync(removingScope, allUsers),
            (scope, deleteSettings, progress) => TrayAppDotNETProgramInstallResult.From(
                AppServices.Installation.PrepareUninstall(scope, deleteSettings, progress)),
            (scope, deleteSettings, progress) => AppServices.Installation.RunUninstall(
                scope,
                deleteSettings,
                static () => Environment.Exit(0),
                progress),
            (installOptions, progress) => TrayAppDotNETProgramInstallResult.From(
                AppServices.Installation.InstallToLocalAppData(installOptions: installOptions, progress: progress)),
            (installOptions, progress) => TrayAppDotNETProgramInstallResult.From(
                AppServices.Installation.InstallSystemWide(installOptions: installOptions, progress: progress)),
            () => AppServices.InstallLayout.LocalAppDataInstallExecutable,
            () => AppServices.InstallLayout.ProgramFilesInstallExecutable,
            TADNLog.Log,
            TADNLog.Flush);
}
