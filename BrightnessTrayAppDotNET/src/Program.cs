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
            (sourceExe, buildNumber, installOptions, progress) => TrayAppDotNETProgramInstallResult.From(
                AppServices.Installation.RunAdminInstallSystem(sourceExe, buildNumber, installOptions, progress)),
            (removingScope, allUsers) => AppServices.StartMenu.Sync(removingScope, allUsers),
            (scope, deleteSettings, progress) =>
            {
                // A gamma ramp outlives its process, and preparation kills running instances before they can restore
                // it. Read the settings first (preparation may delete them), restore after every instance is gone.
                bool restoreGammaRamp = AppSettings.IsPersistedGammaRampTintOn();
                try
                {
                    return TrayAppDotNETProgramInstallResult.From(
                        AppServices.Installation.PrepareUninstall(scope, deleteSettings, progress));
                }
                finally
                {
                    if (restoreGammaRamp) GammaRampNightLight.RestoreIdentityOnActiveDisplays();
                }
            },
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
