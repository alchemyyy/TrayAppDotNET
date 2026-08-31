using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Install;

namespace TrayAppDotNETCommon.Utils;

/// <summary>Runs C# uninstall preparation, then removes the unlocked payload from a small batch file.</summary>
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
                payload);
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
        TrayAppDotNETInstallPayload payload)
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
        string sharedFileCommands = string.Join(
            Environment.NewLine,
            payload.InstalledFiles(installedExecutableFileName)
                .Where(file => file.RemoveOnlyWhenInstallRootHasNoExe)
                .Select(file => DeleteFileCommands(Path.Combine(installDirectory, file.Name), indent: "  ")));
        string sharedDirectoryCommands = string.Join(
            Environment.NewLine,
            payload.InstalledDirectories
                .Where(directory => directory.RemoveOnlyWhenInstallRootHasNoExe)
                .Select(directory =>
                    DeleteDirectoryCommands(Path.Combine(installDirectory, directory.Name), indent: "  ")));
        string rootFileCommands = payload.CopySourceDirectoryRootFiles
            ? $"  del /f /q \"{Escape(Path.Combine(installDirectory, path2: "*"))}\" >nul 2>&1"
            : string.Empty;
        string settingsCommands = deleteSettings
            ? DeleteDirectoryCommands(identity.SettingsDirectory)
            : string.Empty;

        return $"""
                 @echo off
                 setlocal EnableExtensions DisableDelayedExpansion
                 set "ERR=0"

                 rem Reconcile shortcuts, registry state, and exact installed processes in C#.
                 start "" /wait "{Escape(helperExecutable)}" {TrayAppDotNETInstallOptions.PrepareUninstallArgument} --scope {InstallScopeExtensions.ToArg(scope)}
                 if errorlevel 1 set "ERR=1"

                 rem The helper can itself be the installed exe, so retry until its image is unmapped.
                 set "DELETE_ATTEMPT=0"
                 :delete_executable
                 del /f /q "{Escape(installExecutable)}" >nul 2>&1
                 if not exist "{Escape(installExecutable)}" goto delete_payload
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
                 set "HAS_SIBLING_EXE="
                 for %%F in ("{Escape(Path.Combine(installDirectory, path2: "*.exe"))}") do if exist "%%~fF" set "HAS_SIBLING_EXE=1"
                 if not defined HAS_SIBLING_EXE (
                 {sharedFileCommands}
                 {sharedDirectoryCommands}
                 {rootFileCommands}
                 )
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

    private static string DeleteFileCommands(string path, string indent = "") =>
        $"""
         {indent}del /f /q "{Escape(path)}" >nul 2>&1
         {indent}if exist "{Escape(path)}" set "ERR=1"
         """;

    private static string DeleteDirectoryCommands(string path, string indent = "") =>
        $"""
         {indent}rmdir /s /q "{Escape(path)}" >nul 2>&1
         {indent}if exist "{Escape(path)}" set "ERR=1"
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
