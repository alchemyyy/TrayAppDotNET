namespace FanControlTrayAppDotNET;

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

    public static string? UninstallerInstallDir => TrayAppDotNETProgram.UninstallerInstallDir;

    public static InstallScope UninstallerScope => TrayAppDotNETProgram.UninstallerScope;

    public static int Main(string[] args) =>
        TrayAppDotNETProgram.Run(args, ApplicationName, Constants.AppGUID, CreateProgramOptions);

    private static TrayAppDotNETProgramOptions CreateProgramOptions() =>
        new(
            ApplicationName,
            SharedRootFolderName,
            Constants.AppGUID,
            FanAvaloniaRunner.Run,
            (sourceExe, buildNumber) => TrayAppDotNETProgramInstallResult.From(
                AppServices.Installation.RunAdminInstallSystem(sourceExe, buildNumber)),
            (removingScope, allUsers) => AppServices.StartMenu.Sync(removingScope, allUsers),
            () => TrayAppDotNETProgramInstallResult.From(AppServices.Installation.InstallToLocalAppData()),
            () => TrayAppDotNETProgramInstallResult.From(AppServices.Installation.InstallSystemWide()),
            () => AppServices.InstallLayout.LocalAppDataInstallExecutable,
            () => AppServices.InstallLayout.ProgramFilesInstallExecutable,
            TADNLog.Log,
            TADNLog.Flush);
}
