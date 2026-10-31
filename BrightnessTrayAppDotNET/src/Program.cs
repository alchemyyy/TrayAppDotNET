using BrightnessTrayAppDotNET.DDCCI;
using BrightnessTrayAppDotNET.Interop.NightLight;

namespace BrightnessTrayAppDotNET;

internal static class Program
{
    public const string ApplicationName = Constants.ApplicationName;

    public static string AppLocalAppDataDirectory => AppServices.Install.SettingsDirectory;

    [STAThread]
    public static int Main(string[] args)
    {
        if (NightLightHelperServer.TryRun(args, out int nightLightHelperExitCode))
            return nightLightHelperExitCode;

        if (DDCHelperServer.TryRun(args, out int helperExitCode))
            return helperExitCode;

        return TrayAppDotNETProgram.Run(
            args,
            ApplicationName,
            Constants.AppGUID,
            static () => AppServices.Install.CreateProgramOptions(BrightnessAvaloniaRunner.Run));
    }
}
