using BrightnessTrayAppDotNET.DDCCI;
using BrightnessTrayAppDotNET.Interop.NightLight;
using TrayAppDotNETCommon.Models;

namespace BrightnessTrayAppDotNET;

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

    public static int Main(string[] args)
    {
        if (NightLightHelperServer.TryRun(args, out int nightLightHelperExitCode))
            return nightLightHelperExitCode;

        if (DDCHelperServer.TryRun(args, out int helperExitCode))
            return helperExitCode;

        return TrayAppDotNETProgram.Run(args, ApplicationName, Constants.AppGUID, CreateProgramOptions);
    }

    private static TrayAppDotNETProgramOptions CreateProgramOptions() =>
        new(
            ApplicationName,
            SharedRootFolderName,
            Constants.AppGUID,
            BrightnessAvaloniaRunner.Run,
            (sourceExe, buildNumber, installOptions) => TrayAppDotNETProgramInstallResult.From(
                AppServices.Installation.RunAdminInstallSystem(sourceExe, buildNumber, installOptions)),
            (removingScope, allUsers) => AppServices.StartMenu.Sync(removingScope, allUsers),
            scope => TrayAppDotNETProgramInstallResult.From(AppServices.Installation.PrepareUninstall(scope)),
            (scope, deleteSettings) => AppServices.Installation.RunUninstall(
                scope,
                deleteSettings,
                static () => Environment.Exit(0)),
            installOptions => TrayAppDotNETProgramInstallResult.From(
                AppServices.Installation.InstallToLocalAppData(installOptions: installOptions)),
            installOptions => TrayAppDotNETProgramInstallResult.From(
                AppServices.Installation.InstallSystemWide(installOptions: installOptions)),
            () => AppServices.InstallLayout.LocalAppDataInstallExecutable,
            () => AppServices.InstallLayout.ProgramFilesInstallExecutable,
            WPFLog.Log,
            WPFLog.Flush);
}
