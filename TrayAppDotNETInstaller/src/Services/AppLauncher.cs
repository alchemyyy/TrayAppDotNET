using System.Diagnostics;

namespace TrayAppDotNETInstaller.Services;

/// <summary>Starts an installed app as the interactive user, even when the installer itself is elevated.</summary>
public static class AppLauncher
{
    // Keep in sync with TrayAppDotNETProgram.HiddenArgument, which the watcher forwards to the app it monitors
    public const string HiddenArgument = "--hidden";

    private const string ExplorerExecutableName = "explorer.exe";

    /// <summary>
    /// Starts <paramref name="executablePath"/> with <paramref name="arguments"/>. Never throws; returns false when
    /// the app could not be started.
    /// </summary>
    public static bool Launch(string executablePath, IReadOnlyList<string> arguments)
    {
        try
        {
            if (!File.Exists(executablePath))
            {
                InstallerLog.Write($"AppLauncher: executable not found: {executablePath}");
                return false;
            }

            string workingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;
            string commandLine = FormatArguments(arguments);
            // With UAC off, or for a standard user, the installer already runs with the desktop's token, so a
            // direct start is the same as one through Explorer and keeps the arguments on every Windows setup
            if (!SystemProbes.IsSplitTokenElevated())
                return Start(CreateDirectStartInfo(executablePath, commandLine, workingDirectory), executablePath);

            if (UnelevatedLauncher.TryShellExecute(executablePath, commandLine, workingDirectory, out string? failure))
            {
                InstallerLog.Write($"AppLauncher: the desktop shell started {executablePath} {commandLine}");
                return true;
            }

            // explorer.exe still drops the elevation, but it passes no arguments to what it opens
            InstallerLog.Write(
                $"AppLauncher: the desktop shell could not start {executablePath} ({failure}); "
                + $"starting it through {ExplorerExecutableName} without \"{commandLine}\"");
            return Start(CreateExplorerStartInfo(executablePath, workingDirectory), executablePath);
        }
        catch (Exception exception)
        {
            InstallerLog.Write($"AppLauncher: could not start {executablePath}", exception);
            return false;
        }
    }

    /// <summary>Quotes each argument the way the child's command line parser expects and joins them.</summary>
    internal static string FormatArguments(IReadOnlyList<string> arguments)
    {
        FrameworkCompatibility.ThrowIfNull(arguments, nameof(arguments));

        // AddArgument stands in for ProcessStartInfo.ArgumentList, which the .NET Framework lacks
        ProcessStartInfo builder = new();
        foreach (string argument in arguments) builder.AddArgument(argument);

        return builder.Arguments;
    }

    private static bool Start(ProcessStartInfo startInfo, string executablePath)
    {
        using Process? process = Process.Start(startInfo);
        InstallerLog.Write(process == null
            ? $"AppLauncher: Process.Start returned null for {executablePath}"
            : $"AppLauncher: started {executablePath} {startInfo.Arguments}");
        return process != null;
    }

    private static ProcessStartInfo CreateDirectStartInfo(string executablePath, string commandLine, string workingDirectory)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            Arguments = commandLine,
            UseShellExecute = true,
            WorkingDirectory = workingDirectory
        };
        return startInfo;
    }

    /// <summary>Explorer runs at the desktop's integrity level, so the app does not inherit elevation.</summary>
    private static ProcessStartInfo CreateExplorerStartInfo(string executablePath, string workingDirectory)
    {
        string explorerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            ExplorerExecutableName);
        ProcessStartInfo startInfo = new()
        {
            FileName = explorerPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        startInfo.AddArgument(executablePath);
        return startInfo;
    }
}
