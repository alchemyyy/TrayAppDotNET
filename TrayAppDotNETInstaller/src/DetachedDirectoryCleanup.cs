using System.Diagnostics;
using System.Text;

namespace TrayAppDotNETInstaller;

/// <summary>
/// Deletes a directory from a detached cmd script that keeps retrying after this process has moved on. Used
/// for directories the process cannot delete itself: its own mapped native DLLs, and staging folders that a
/// child installer's helper still holds as its working directory.
/// </summary>
internal static class DetachedDirectoryCleanup
{
    private const string ScriptSuffix = ".cleanup.bat";
    private const string CommandInterpreterVariable = "ComSpec";
    private const string DefaultCommandInterpreter = "cmd.exe";
    private const int Attempts = 60;
    private const int RetryDelaySeconds = 1;

    /// <summary>Never throws; returns false when the script could not be started.</summary>
    public static bool Schedule(string directory)
    {
        try
        {
            string scriptPath = directory.TrimEnd(Path.DirectorySeparatorChar) + ScriptSuffix;
            string escapedDirectory = EscapeBatchPath(directory);
            string script = $"""
                @echo off
                setlocal
                set attempts=0
                :retry
                rmdir /s /q "{escapedDirectory}" >nul 2>&1
                if not exist "{escapedDirectory}" goto done
                set /a attempts+=1
                if %attempts% geq {Attempts} goto done
                timeout /t {RetryDelaySeconds} /nobreak >nul 2>&1
                goto retry
                :done
                del /f /q "%~f0" >nul 2>&1
                """;
            File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            ProcessStartInfo startInfo = new()
            {
                FileName = Environment.GetEnvironmentVariable(CommandInterpreterVariable) ?? DefaultCommandInterpreter,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                // The interpreter is handed only the generated leaf name, never the full path. It parses its
                // command line with its own rules rather than the argv ones, so a directory holding an
                // ampersand or a pipe would otherwise be split and part of it executed
                WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? InstallerTempPaths.Root
            };
            // AddArgument stands in for ProcessStartInfo.ArgumentList, which the .NET Framework lacks
            startInfo.AddArgument("/d");
            startInfo.AddArgument("/c");
            startInfo.AddArgument(Path.GetFileName(scriptPath));
            using Process? cleanup = Process.Start(startInfo);
            InstallerLog.Write(cleanup == null
                ? $"DetachedDirectoryCleanup: could not start {scriptPath}"
                : $"DetachedDirectoryCleanup: scheduled cleanup of {directory} as PID {cleanup.Id}");
            return cleanup != null;
        }
        catch (Exception exception)
        {
            InstallerLog.Write($"DetachedDirectoryCleanup: could not schedule cleanup of {directory}", exception);
            return false;
        }
    }

    // The .NET Framework string.Replace(string, string) is already ordinal, so the comparison argument the
    // modern overload took is simply dropped
    private static string EscapeBatchPath(string path) =>
        path.Replace(oldValue: "%", newValue: "%%");
}
