namespace BatteryTrayAppDotNET;

internal static class Program
{
    public const string ApplicationName = Constants.ApplicationName;

    public static string AppLocalAppDataDirectory => AppServices.Install.SettingsDirectory;

    [STAThread]
    public static int Main(string[] args) =>
        TrayAppDotNETProgram.Run(
            args,
            ApplicationName,
            Constants.AppGUID,
            static () => AppServices.Install.CreateProgramOptions(BatteryAvaloniaRunner.Run));
}
