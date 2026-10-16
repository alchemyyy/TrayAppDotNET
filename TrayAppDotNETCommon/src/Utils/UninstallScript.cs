using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Install;

namespace TrayAppDotNETCommon.Utils;

/// <summary>
/// Runs C# uninstall preparation, which also removes the shared files when this is the last app, then removes
/// this app's own files from a small batch file once its image is unmapped.
/// </summary>
public static class UninstallScript
{
    private const int FileDeleteAttempts = 120;

    public static Process? Run(
        string installDirectory,
        InstallScope scope,
        bool deleteSettings,
        TrayAppDotNETInstallIdentity identity,
        string installedExecutableFileName,
        TrayAppDotNETInstallPayload payload,
        string? progressPipeName,
        out bool userCancelled)
    {
        userCancelled = false;
        string? batchPath = null;
        try
        {
            string helperExecutable = Environment.ProcessPath
                                      ?? throw new InvalidOperationException("Cannot resolve uninstall helper path.");
            batchPath = Path.Combine(
                Path.GetTempPath(),
                $"{identity.ApplicationName}-uninstall-{Guid.NewGuid():N}.bat");
            string content = BuildScript(
                helperExecutable,
                installDirectory,
                scope,
                deleteSettings,
                identity,
                installedExecutableFileName,
                payload,
                progressPipeName);
            File.WriteAllText(batchPath, content, Encoding.ASCII);

            ProcessStartInfo startInfo = new()
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/d /c \"\"{batchPath}\"\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            if (scope == InstallScope.ProgramFiles)
                startInfo.Verb = "runas";

            Process? process = Process.Start(startInfo);
            if (process == null)
            {
                TryDeleteBatch(batchPath, identity);
                identity.WriteLog("UninstallScript.Run: Process.Start returned null");
                return null;
            }

            process.EnableRaisingEvents = true;
            return process;
        }
        catch (Win32Exception exception) when (
            (uint)exception.NativeErrorCode == 0x800704C7 || exception.NativeErrorCode == 1223)
        {
            userCancelled = true;
            TryDeleteBatch(batchPath, identity);
            identity.WriteLog("UninstallScript.Run: UAC prompt was declined");
            return null;
        }
        catch (Exception exception)
        {
            TryDeleteBatch(batchPath, identity);
            identity.WriteLog($"UninstallScript.Run: {exception}");
            return null;
        }
    }

    internal static string BuildScript(
        string helperExecutable,
        string installDirectory,
        InstallScope scope,
        bool deleteSettings,
        TrayAppDotNETInstallIdentity identity,
        string installedExecutableFileName,
        TrayAppDotNETInstallPayload payload,
        string? progressPipeName = null)
    {
        string installExecutable = Path.Combine(installDirectory, installedExecutableFileName);
        string registryPath = (scope == InstallScope.ProgramFiles ? "HKLM\\" : "HKCU\\")
                              + identity.UninstallRegistrySubKeyPath;
        string desktopShortcut = Path.Combine(
            Environment.GetFolderPath(scope == InstallScope.ProgramFiles
                ? Environment.SpecialFolder.CommonDesktopDirectory
                : Environment.SpecialFolder.DesktopDirectory),
            identity.ApplicationName + ".lnk");
        string retryDelaySeconds = Math.Max(val1: 1, TimeConstants.UninstallFileRetryDelayMs / 1000)
            .ToString(CultureInfo.InvariantCulture);

        string regularFileCommands = string.Join(
            Environment.NewLine,
            payload.InstalledFiles(installedExecutableFileName)
                .Where(file =>
                    !file.RemoveOnlyWhenInstallRootHasNoExe
                    && !string.Equals(
                        file.Name,
                        installedExecutableFileName,
                        StringComparison.OrdinalIgnoreCase))
                .Select(file => DeleteFileCommands(Path.Combine(installDirectory, file.Name))));
        string regularDirectoryCommands = string.Join(
            Environment.NewLine,
            payload.InstalledDirectories
                .Where(directory => !directory.RemoveOnlyWhenInstallRootHasNoExe)
                .Select(directory => DeleteDirectoryCommands(Path.Combine(installDirectory, directory.Name))));
        // Where the helper moves the exe; it also removes the shared files, under the install directory lock
        string pendingExecutable = TrayAppDotNETInstallLayout.PendingRemovalExecutablePath(installExecutable);
        string settingsCommands = deleteSettings
            ? DeleteDirectoryCommands(identity.SettingsDirectory)
            : string.Empty;
        string helperArguments = BuildHelperArguments(deleteSettings, progressPipeName);

        return $"""
                 @echo off
                 setlocal EnableExtensions DisableDelayedExpansion
                 set "ERR=0"

                 rem Reconcile shortcuts, registry state, and exact installed processes in C#.
                 start "" /wait "{Escape(helperExecutable)}" {TrayAppDotNETInstallOptions.PrepareUninstallArgument} --scope {InstallScopeExtensions.ToArg(scope)}{helperArguments}
                 if errorlevel 1 set "ERR=1"

                 rem The helper renames the exe when it can, and can itself be that image, so retry until it is unmapped.
                 set "DELETE_ATTEMPT=0"
                 :delete_executable
                 del /f /q "{Escape(installExecutable)}" >nul 2>&1
                 del /f /q "{Escape(pendingExecutable)}" >nul 2>&1
                 if exist "{Escape(installExecutable)}" goto retry_executable
                 if not exist "{Escape(pendingExecutable)}" goto delete_payload
                 :retry_executable
                 set /a DELETE_ATTEMPT+=1
                 if %DELETE_ATTEMPT% GEQ {FileDeleteAttempts} goto executable_locked
                 timeout /t {retryDelaySeconds} /nobreak >nul 2>&1
                 goto delete_executable

                 :executable_locked
                 set "ERR=1"
                 goto metadata

                 :delete_payload
                 {regularFileCommands}
                 {regularDirectoryCommands}
                 rmdir "{Escape(installDirectory)}" >nul 2>&1

                 :metadata
                 del /f /q "{Escape(desktopShortcut)}" >nul 2>&1
                 reg delete "{registryPath}" /f >nul 2>&1
                 reg query "{registryPath}" >nul 2>&1
                 if not errorlevel 1 set "ERR=1"
                 reg delete "HKCU\{identity.LegacyRunKeyRegistryPath}" /v "{identity.ApplicationName}" /f >nul 2>&1
                 {settingsCommands}
                 (goto) 2>nul & del /f /q "%~f0" & exit /b %ERR%
                 """;
    }

    /// <summary>Forwards the explicit settings choice and the launcher's progress pipe to the helper stage.</summary>
    private static string BuildHelperArguments(bool deleteSettings, string? progressPipeName)
    {
        string arguments = deleteSettings
            ? $" {TrayAppDotNETInstallOptions.DeleteSettingsArgument} true"
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(progressPipeName))
            arguments += $" {TrayAppDotNETInstallOptions.ProgressPipeArgument} \"{Escape(progressPipeName)}\"";
        return arguments;
    }

    private static string DeleteFileCommands(string path) =>
        $"""
         del /f /q "{Escape(path)}" >nul 2>&1
         if exist "{Escape(path)}" set "ERR=1"
         """;

    private static string DeleteDirectoryCommands(string path) =>
        $"""
         rmdir /s /q "{Escape(path)}" >nul 2>&1
         if exist "{Escape(path)}" set "ERR=1"
         """;

    private static string Escape(string path) => path.Replace(oldValue: "%", newValue: "%%", StringComparison.Ordinal);

    private static void TryDeleteBatch(string? path, TrayAppDotNETInstallIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception)
        {
            identity.WriteLog($"UninstallScript.TryDeleteBatch({path}): {exception.Message}");
        }
    }
}
