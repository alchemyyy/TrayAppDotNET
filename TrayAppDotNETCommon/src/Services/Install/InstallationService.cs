using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.Services.Install;

public sealed record TrayAppDotNETInstallationOptions(
    TrayAppDotNETInstallIdentity Identity,
    TrayAppDotNETInstallLayout Layout,
    TrayAppDotNETInstallPayload Payload,
    int CurrentBuildNumber,
    Action<InstallScope?, bool>? SyncStartMenu = null,
    Action<Action>? PostToUIThread = null);

/// <summary>
/// App-agnostic installer for TrayAppDotNET publish payloads.
/// The caller supplies app identity, install layout, payload contents, current build number,
/// and optional hooks for UI-thread shutdown and Start Menu reconciliation.
/// </summary>
public sealed class TrayAppDotNETInstallationService(TrayAppDotNETInstallationOptions options)
{
    public TrayAppDotNETInstallIdentity Identity => options.Identity;

    public TrayAppDotNETInstallLayout Layout => options.Layout;

    public TrayAppDotNETInstallPayload Payload => options.Payload;

    public static bool IsElevated(Action<string>? log = null)
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            log?.Invoke($"TrayAppDotNETInstallationService.IsElevated: {ex.Message}");
            return false;
        }
    }

    public bool IsRunningFromWindowsStore()
    {
        string? current = Environment.ProcessPath;
        if (string.IsNullOrEmpty(current)) return false;

        try
        {
            return current.StartsWith(Layout.WindowsAppsRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public List<TrayAppDotNETInstallationInfo> DetectAll()
    {
        string currentPath = PathNormalization.Normalize(Environment.ProcessPath);

        return
        [
            DetectFile(InstallScope.LocalAppData, Layout.LocalAppDataInstallExecutable, currentPath),
            DetectFile(InstallScope.ProgramFiles, Layout.ProgramFilesInstallExecutable, currentPath),
            DetectStore(currentPath),
        ];
    }

    public TrayAppDotNETInstallationInfo DetectFile(InstallScope scope, string installExecutable, string currentPath)
    {
        bool fileExists = File.Exists(installExecutable);

        WindowsUninstallRegistry.Entry? entry = WindowsUninstallRegistry.Read(scope, Identity);
        if (!fileExists && entry != null)
        {
            WindowsUninstallRegistry.Remove(scope, Identity);
            entry = null;
        }

        if (!fileExists)
            return new TrayAppDotNETInstallationInfo(scope, installExecutable, TrayAppDotNETInstallStatus.NotInstalled,
                null);

        bool running = string.Equals(
            currentPath,
            PathNormalization.Normalize(installExecutable),
            StringComparison.OrdinalIgnoreCase);
        if (running)
            return new TrayAppDotNETInstallationInfo(scope, installExecutable,
                TrayAppDotNETInstallStatus.CurrentlyRunning, entry?.DisplayVersion);

        int? installed = entry?.DisplayVersion;
        if (installed.HasValue && installed.Value < options.CurrentBuildNumber)
            return new TrayAppDotNETInstallationInfo(scope, installExecutable,
                TrayAppDotNETInstallStatus.InstalledOutOfDate, installed);

        return new TrayAppDotNETInstallationInfo(scope, installExecutable, TrayAppDotNETInstallStatus.InstalledUpToDate,
            installed);
    }

    public TrayAppDotNETInstallationInfo DetectStore(string currentPath)
    {
        if (IsRunningFromWindowsStore())
            return new TrayAppDotNETInstallationInfo(InstallScope.WindowsStore, currentPath,
                TrayAppDotNETInstallStatus.CurrentlyRunning, null);

        return new TrayAppDotNETInstallationInfo(InstallScope.WindowsStore, string.Empty,
            TrayAppDotNETInstallStatus.NotInstalled, null);
    }

    public TrayAppDotNETInstallResult InstallToLocalAppData(string? sourceExe = null)
    {
        sourceExe ??= Environment.ProcessPath ?? string.Empty;
        if (!File.Exists(sourceExe))
            return new TrayAppDotNETInstallResult(false, "Cannot determine running executable path");

        try
        {
            TrayAppDotNETInstallResult copyResult = CopyInstallPayload(
                sourceExe,
                Layout.LocalAppDataInstallDirectory,
                Layout.LocalAppDataInstallExecutable);
            if (!copyResult.Success) return copyResult;

            WindowsUninstallRegistry.Write(
                InstallScope.LocalAppData,
                Layout.LocalAppDataInstallDirectory,
                options.CurrentBuildNumber,
                Identity,
                Layout.InstalledExecutableFileName);

            options.SyncStartMenu?.Invoke(null, false);
            return new TrayAppDotNETInstallResult(true);
        }
        catch (Exception ex)
        {
            Identity.WriteLog($"TrayAppDotNETInstallationService.InstallToLocalAppData: {ex}");
            return new TrayAppDotNETInstallResult(false, ex.Message);
        }
    }

    public TrayAppDotNETInstallResult InstallSystemWide(string? sourceExe = null)
    {
        sourceExe ??= Environment.ProcessPath ?? string.Empty;
        if (!File.Exists(sourceExe))
            return new TrayAppDotNETInstallResult(false, "Cannot determine running executable path");

        if (IsElevated(Identity.WriteLog))
            return RunAdminInstallSystem(sourceExe, options.CurrentBuildNumber);

        return TryInvokeElevated(
            $"--admin-action install-system \"{sourceExe}\" {options.CurrentBuildNumber}",
            sourceExe);
    }

    public TrayAppDotNETInstallResult RunAdminInstallSystem(string sourceExe, int buildNumber)
    {
        try
        {
            if (!File.Exists(sourceExe))
                return new TrayAppDotNETInstallResult(false, $"Source exe not found: {sourceExe}");

            TrayAppDotNETInstallResult copyResult = CopyInstallPayload(
                sourceExe,
                Layout.ProgramFilesInstallDirectory,
                Layout.ProgramFilesInstallExecutable);
            if (!copyResult.Success) return copyResult;

            WindowsUninstallRegistry.Write(
                InstallScope.ProgramFiles,
                Layout.ProgramFilesInstallDirectory,
                buildNumber,
                Identity,
                Layout.InstalledExecutableFileName);

            options.SyncStartMenu?.Invoke(null, true);
            return new TrayAppDotNETInstallResult(true);
        }
        catch (Exception ex)
        {
            Identity.WriteLog($"TrayAppDotNETInstallationService.RunAdminInstallSystem: {ex}");
            return new TrayAppDotNETInstallResult(false, ex.Message);
        }
    }

    public TrayAppDotNETInstallResult CopyInstallPayload(
        string sourceExe,
        string destinationDirectory,
        string destinationExe)
    {
        try
        {
            if (!File.Exists(sourceExe))
                return new TrayAppDotNETInstallResult(false, $"Source exe not found: {sourceExe}");

            string? sourceDirectory = Path.GetDirectoryName(sourceExe);
            if (string.IsNullOrWhiteSpace(sourceDirectory))
                return new TrayAppDotNETInstallResult(false, $"Cannot determine source directory for {sourceExe}");

            foreach (TrayAppDotNETInstallDirectory directory in Payload.RequiredDirectories)
            {
                string sourcePath = Path.Combine(sourceDirectory, directory.Name);
                if (!Directory.Exists(sourcePath))
                    return new TrayAppDotNETInstallResult(false, $"Required install folder not found: {sourcePath}");
            }

            foreach (TrayAppDotNETInstallFile file in Payload.RequiredFiles)
            {
                string sourceFile = Path.Combine(sourceDirectory, file.Name);
                if (!File.Exists(sourceFile))
                    return new TrayAppDotNETInstallResult(false, $"Required install file not found: {sourceFile}");
            }

            Directory.CreateDirectory(destinationDirectory);
            CopyFileIfDifferent(sourceExe, destinationExe);

            foreach (TrayAppDotNETInstallFile file in Payload.RequiredFiles)
                CopyFileIfDifferent(Path.Combine(sourceDirectory, file.Name),
                    Path.Combine(destinationDirectory, file.Name));

            foreach (TrayAppDotNETInstallFile file in Payload.OptionalFiles)
            {
                string sourceFile = Path.Combine(sourceDirectory, file.Name);
                if (File.Exists(sourceFile))
                    CopyFileIfDifferent(sourceFile, Path.Combine(destinationDirectory, file.Name));
            }

            foreach (TrayAppDotNETInstallDirectory directory in Payload.RequiredDirectories)
            {
                CopyDirectoryMerge(
                    Path.Combine(sourceDirectory, directory.Name),
                    Path.Combine(destinationDirectory, directory.Name));
            }

            foreach (TrayAppDotNETInstallDirectory directory in Payload.OptionalDirectories)
            {
                string sourcePath = Path.Combine(sourceDirectory, directory.Name);
                if (Directory.Exists(sourcePath))
                    CopyDirectoryMerge(sourcePath, Path.Combine(destinationDirectory, directory.Name));
            }

            return new TrayAppDotNETInstallResult(true);
        }
        catch (Exception ex)
        {
            Identity.WriteLog($"TrayAppDotNETInstallationService.CopyInstallPayload: {ex}");
            return new TrayAppDotNETInstallResult(false, ex.Message);
        }
    }

    public Process? RunUninstall(InstallScope scope, bool deleteSettings, Action? shutdownCurrentProcess = null)
    {
        string installDirectory = scope switch
        {
            InstallScope.LocalAppData => Layout.LocalAppDataInstallDirectory,
            InstallScope.ProgramFiles => Layout.ProgramFilesInstallDirectory,
            _ => string.Empty,
        };
        if (string.IsNullOrEmpty(installDirectory)) return null;

        options.SyncStartMenu?.Invoke(scope, false);

        Process? batProcess = UninstallScript.Run(
            installDirectory,
            scope,
            deleteSettings,
            Identity,
            Layout.InstalledExecutableFileName,
            Payload,
            out bool userCancelled);

        if (userCancelled) return null;

        string runningExe = PathNormalization.Normalize(Environment.ProcessPath);
        string installExecutable = PathNormalization.Normalize(
            Path.Combine(installDirectory, Layout.InstalledExecutableFileName));
        bool runningFromInstall = !string.IsNullOrEmpty(runningExe)
                                  && string.Equals(runningExe, installExecutable, StringComparison.OrdinalIgnoreCase);

        if (!runningFromInstall) return batProcess;

        Action shutdown = shutdownCurrentProcess ?? (() => Environment.Exit(0));
        if (options.PostToUIThread != null) options.PostToUIThread(shutdown);
        else shutdown();

        batProcess?.Dispose();
        return null;
    }

    public TrayAppDotNETInstallResult TryInvokeElevated(string arguments, string sourceExe)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = sourceExe,
                Arguments = arguments,
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using Process? process = Process.Start(psi);
            if (process == null) return new TrayAppDotNETInstallResult(false, "Failed to start elevated process");

            process.WaitForExit();
            return process.ExitCode == 0
                ? new TrayAppDotNETInstallResult(true)
                : new TrayAppDotNETInstallResult(false, $"Elevated process exited with code {process.ExitCode}");
        }
        catch (Win32Exception ex) when ((uint)ex.NativeErrorCode == 0x800704C7 || ex.NativeErrorCode == 1223)
        {
            return new TrayAppDotNETInstallResult(false, UserCancelled: true);
        }
        catch (Exception ex)
        {
            Identity.WriteLog($"TrayAppDotNETInstallationService.TryInvokeElevated: {ex}");
            return new TrayAppDotNETInstallResult(false, ex.Message);
        }
    }

    private static void CopyFileIfDifferent(string sourceFile, string destinationFile)
    {
        if (string.Equals(
                PathNormalization.Normalize(sourceFile),
                PathNormalization.Normalize(destinationFile),
                StringComparison.OrdinalIgnoreCase))
            return;

        string? destinationDirectory = Path.GetDirectoryName(destinationFile);
        if (!string.IsNullOrEmpty(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);
        File.Copy(sourceFile, destinationFile, overwrite: true);
    }

    private static void CopyDirectoryMerge(string sourceDirectory, string destinationDirectory)
    {
        if (string.Equals(
                PathNormalization.Normalize(sourceDirectory),
                PathNormalization.Normalize(destinationDirectory),
                StringComparison.OrdinalIgnoreCase))
            return;

        Directory.CreateDirectory(destinationDirectory);

        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, file);
            CopyFileIfDifferent(file, Path.Combine(destinationDirectory, relativePath));
        }
    }
}
