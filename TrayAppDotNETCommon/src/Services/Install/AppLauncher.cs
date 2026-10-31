using System.Diagnostics;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.Services.Install;

/// <summary>
/// Starts an installed app as the signed-in user. An elevated caller (an elevated console, a System install helper,
/// an elevated update worker) never hands its token to the tray app: the start goes through the desktop shell.
/// </summary>
internal static class TrayAppDotNETAppLauncher
{
    private const string ExplorerExecutableName = "explorer.exe";

    /// <summary>Never throws; returns false when the app could not be started.</summary>
    public static bool Launch(string executablePath, IReadOnlyList<string> arguments, Action<string> log)
    {
        try
        {
            if (!File.Exists(executablePath))
            {
                log($"TrayAppDotNETAppLauncher: executable not found: {executablePath}");
                return false;
            }

            string workingDirectory = Path.GetDirectoryName(executablePath) ?? ".";
            string commandLine = string.Join(' ', arguments.Select(TrayAppDotNETProgram.QuoteCommandLineArgument));
            if (!TrayAppDotNETElevationPolicy.IsSplitTokenElevated(log))
            {
                ProcessStartInfo direct = new()
                {
                    FileName = executablePath,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                foreach (string argument in arguments) direct.ArgumentList.Add(argument);
                using Process? process = Process.Start(direct);
                log(process == null
                    ? $"TrayAppDotNETAppLauncher: Process.Start returned null for {executablePath}"
                    : $"TrayAppDotNETAppLauncher: started {executablePath} {commandLine} as PID {process.Id}");
                return process != null;
            }

            if (ExplorerProcessLauncher.TryShellExecute(
                    executablePath,
                    commandLine,
                    workingDirectory,
                    verb: null,
                    out int errorCode,
                    out string errorMessage))
            {
                log($"TrayAppDotNETAppLauncher: the desktop shell started {executablePath} {commandLine}");
                return true;
            }

            // explorer.exe still drops the elevation, but it passes no arguments to what it opens
            log(
                $"TrayAppDotNETAppLauncher: the desktop shell could not start {executablePath} "
                + $"({errorCode}: {errorMessage}); starting it through {ExplorerExecutableName} without \"{commandLine}\"");
            ProcessStartInfo explorer = new()
            {
                FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    ExplorerExecutableName),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            explorer.ArgumentList.Add(executablePath);
            using Process? explorerProcess = Process.Start(explorer);
            return explorerProcess != null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                              or InvalidOperationException or IOException)
        {
            log($"TrayAppDotNETAppLauncher: could not start {executablePath}: {exception}");
            return false;
        }
    }
}
