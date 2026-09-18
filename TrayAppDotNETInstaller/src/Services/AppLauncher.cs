using System.Diagnostics;

namespace TrayAppDotNETInstaller.Services;

/// <summary>Starts an installed app as the interactive user, even when the installer itself is elevated.</summary>
public static class AppLauncher
{
    private const string ExplorerExecutableName = "explorer.exe";

    /// <summary>Never throws; returns false when the app could not be started.</summary>
    public static bool Launch(string executablePath)
    {
        try
        {
            if (!File.Exists(executablePath))
            {
                InstallerLog.Write($"AppLauncher: executable not found: {executablePath}");
                return false;
            }

            ProcessStartInfo startInfo = SystemProbes.IsElevated()
                ? CreateUnelevatedStartInfo(executablePath)
                : CreateDirectStartInfo(executablePath);
            using Process? process = Process.Start(startInfo);
            InstallerLog.Write(process == null
                ? $"AppLauncher: Process.Start returned null for {executablePath}"
                : $"AppLauncher: started {executablePath}");
            return process != null;
        }
        catch (Exception exception)
        {
            InstallerLog.Write($"AppLauncher: could not start {executablePath}", exception);
            return false;
        }
    }

    private static ProcessStartInfo CreateDirectStartInfo(string executablePath)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty
        };
        return startInfo;
    }

    /// <summary>Explorer runs at the desktop's integrity level, so the app does not inherit elevation.</summary>
    private static ProcessStartInfo CreateUnelevatedStartInfo(string executablePath)
    {
        string explorerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            ExplorerExecutableName);
        ProcessStartInfo startInfo = new()
        {
            FileName = explorerPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty
        };
        // AddArgument stands in for ProcessStartInfo.ArgumentList, which the .NET Framework lacks
        startInfo.AddArgument(executablePath);
        return startInfo;
    }
}
