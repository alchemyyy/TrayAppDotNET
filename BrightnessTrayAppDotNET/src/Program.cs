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
        // Elevated, so it writes no file: a same-user process controls the user's folders and could redirect an
        // elevated write with a junction. Its exit code is the whole report.
        if (NightLightLeakFix.IsElevatedConfigureLaunch(args))
        {
            TADNLog.InitializeForElevatedProcess(forwarder: null);
            return NightLightLeakFix.RunElevatedConfigure(args);
        }

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
