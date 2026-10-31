namespace FanControlTrayAppDotNET;

internal static class Program
{
    public const string ApplicationName = Constants.ApplicationName;

    public static string AppLocalAppDataDirectory => AppServices.Install.SettingsDirectory;

    /// <summary>The shared %LOCALAPPDATA%\TrayAppDotNET root, which the settings folder override does not move.</summary>
    public static string LocalAppDataRoot => TrayAppDotNETAppInstallServices.LocalAppDataRoot;

    [STAThread]
    public static int Main(string[] args) =>
        TrayAppDotNETProgram.Run(
            args,
            ApplicationName,
            Constants.AppGUID,
            static () => AppServices.Install.CreateProgramOptions(FanAvaloniaRunner.Run));
}
