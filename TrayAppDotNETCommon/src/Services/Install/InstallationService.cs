using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.Services.Install;

public sealed record TrayAppDotNETInstallationOptions(
    TrayAppDotNETInstallIdentity Identity,
    TrayAppDotNETInstallLayout Layout,
    TrayAppDotNETInstallPayload Payload,
    int CurrentBuildNumber,
    Action<InstallScope?, bool>? SyncStartMenu = null,
    Action<Action>? PostToUIThread = null,
    TrayAppDotNETDesktopShortcutOptions? DesktopShortcutOptions = null);

/// <summary>
/// App-agnostic installer for TrayAppDotNET publish payloads.
/// The caller supplies app identity, install layout, payload contents, current build number,
/// and optional hooks for UI-thread shutdown and Start Menu reconciliation.
/// </summary>
public sealed class TrayAppDotNETInstallationService(TrayAppDotNETInstallationOptions options)
{
    private const int UninstallProcessStopAttempts = 20;

    public TrayAppDotNETInstallIdentity Identity => options.Identity;

    public TrayAppDotNETInstallLayout Layout => options.Layout;

    public TrayAppDotNETInstallPayload Payload => options.Payload;

    public TrayAppDotNETDesktopShortcut DesktopShortcut { get; } = new(
        options.DesktopShortcutOptions
        ?? new TrayAppDotNETDesktopShortcutOptions(
            options.Identity.ApplicationName,
            options.Layout,
            options.Identity.WriteLog));

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
            DetectStore(currentPath)
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
        {
            return new TrayAppDotNETInstallationInfo(scope, installExecutable, TrayAppDotNETInstallStatus.NotInstalled,
                InstalledVersion: null);
        }

        bool running = string.Equals(
            currentPath,
            PathNormalization.Normalize(installExecutable),
            StringComparison.OrdinalIgnoreCase);
        if (running)
        {
            return new TrayAppDotNETInstallationInfo(scope, installExecutable,
                TrayAppDotNETInstallStatus.CurrentlyRunning, entry?.DisplayVersion);
        }

        int? installed = entry?.DisplayVersion;
        if (installed.HasValue && installed.Value < options.CurrentBuildNumber)
        {
            return new TrayAppDotNETInstallationInfo(scope, installExecutable,
                TrayAppDotNETInstallStatus.InstalledOutOfDate, installed);
        }

        return new TrayAppDotNETInstallationInfo(scope, installExecutable, TrayAppDotNETInstallStatus.InstalledUpToDate,
            installed);
    }

    public TrayAppDotNETInstallationInfo DetectStore(string currentPath)
    {
        if (IsRunningFromWindowsStore())
        {
            return new TrayAppDotNETInstallationInfo(InstallScope.WindowsStore, currentPath,
                TrayAppDotNETInstallStatus.CurrentlyRunning, InstalledVersion: null);
        }

        return new TrayAppDotNETInstallationInfo(InstallScope.WindowsStore, string.Empty,
            TrayAppDotNETInstallStatus.NotInstalled, InstalledVersion: null);
    }

    public TrayAppDotNETInstallResult InstallToLocalAppData(
        string? sourceExe = null,
        TrayAppDotNETInstallOptions? installOptions = null)
    {
        sourceExe ??= Environment.ProcessPath ?? string.Empty;
        if (!File.Exists(sourceExe))
        {
            return new TrayAppDotNETInstallResult(Success: false,
                ErrorMessage: "Cannot determine running executable path");
        }

        try
        {
            StopInstalledProcesses(InstallScope.LocalAppData);

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

            return ApplyInstallOptions(InstallScope.LocalAppData, allUsers: false, installOptions);
        }
        catch (Exception ex)
        {
            Identity.WriteLog($"TrayAppDotNETInstallationService.InstallToLocalAppData: {ex}");
            return new TrayAppDotNETInstallResult(Success: false, ex.Message);
        }
    }

    public TrayAppDotNETInstallResult InstallSystemWide(
        string? sourceExe = null,
        TrayAppDotNETInstallOptions? installOptions = null)
    {
        sourceExe ??= Environment.ProcessPath ?? string.Empty;
        if (!File.Exists(sourceExe))
        {
            return new TrayAppDotNETInstallResult(Success: false,
                ErrorMessage: "Cannot determine running executable path");
        }

        if (IsElevated(Identity.WriteLog))
            return RunAdminInstallSystem(sourceExe, options.CurrentBuildNumber, installOptions);

        return TryInvokeElevated(
            BuildElevatedInstallArguments(sourceExe, options.CurrentBuildNumber, installOptions),
            sourceExe);
    }

    public TrayAppDotNETInstallResult RunAdminInstallSystem(
        string sourceExe,
        int buildNumber,
        TrayAppDotNETInstallOptions? installOptions = null)
    {
        try
        {
            if (!IsElevated(Identity.WriteLog))
            {
                return new TrayAppDotNETInstallResult(Success: false,
                    ErrorMessage: "System installation requires elevation");
            }

            if (!File.Exists(sourceExe))
                return new TrayAppDotNETInstallResult(Success: false, $"Source exe not found: {sourceExe}");

            StopInstalledProcesses(InstallScope.ProgramFiles);

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

            return ApplyInstallOptions(InstallScope.ProgramFiles, allUsers: true, installOptions);
        }
        catch (Exception ex)
        {
            Identity.WriteLog($"TrayAppDotNETInstallationService.RunAdminInstallSystem: {ex}");
            return new TrayAppDotNETInstallResult(Success: false, ex.Message);
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
                return new TrayAppDotNETInstallResult(Success: false, $"Source exe not found: {sourceExe}");

            string? sourceDirectory = Path.GetDirectoryName(sourceExe);
            if (string.IsNullOrWhiteSpace(sourceDirectory))
            {
                return new TrayAppDotNETInstallResult(Success: false,
                    $"Cannot determine source directory for {sourceExe}");
            }

            foreach (TrayAppDotNETInstallDirectory directory in Payload.RequiredDirectories)
            {
                string sourcePath = Path.Combine(sourceDirectory, directory.Name);
                if (!Directory.Exists(sourcePath))
                {
                    return new TrayAppDotNETInstallResult(Success: false,
                        $"Required install folder not found: {sourcePath}");
                }
            }

            foreach (TrayAppDotNETInstallFile file in Payload.RequiredFiles)
            {
                string sourceFile = Path.Combine(sourceDirectory, file.Name);
                if (!File.Exists(sourceFile))
                {
                    return new TrayAppDotNETInstallResult(Success: false,
                        $"Required install file not found: {sourceFile}");
                }
            }

            Directory.CreateDirectory(destinationDirectory);
            CopyFileIfDifferent(sourceExe, destinationExe);

            foreach (TrayAppDotNETInstallFile file in Payload.RequiredFiles)
            {
                CopyFileIfDifferent(Path.Combine(sourceDirectory, file.Name),
                    Path.Combine(destinationDirectory, file.Name));
            }

            foreach (TrayAppDotNETInstallFile file in Payload.OptionalFiles)
            {
                string sourceFile = Path.Combine(sourceDirectory, file.Name);
                if (File.Exists(sourceFile))
                    CopyFileIfDifferent(sourceFile, Path.Combine(destinationDirectory, file.Name));
            }

            if (Payload.CopySourceDirectoryRootFiles)
            {
                foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory))
                {
                    if (!ShouldCopySourceDirectoryRootFile(sourceFile, Layout.InstalledExecutableFileName))
                        continue;

                    CopyFileIfDifferent(
                        sourceFile,
                        Path.Combine(destinationDirectory, Path.GetFileName(sourceFile)));
                }
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
            return new TrayAppDotNETInstallResult(Success: false, ex.Message);
        }
    }

    public Process? RunUninstall(InstallScope scope, bool deleteSettings, Action? shutdownCurrentProcess = null)
    {
        string installDirectory = scope switch
        {
            InstallScope.LocalAppData => Layout.LocalAppDataInstallDirectory,
            InstallScope.ProgramFiles => Layout.ProgramFilesInstallDirectory,
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(installDirectory)) return null;

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
        if (shutdownCurrentProcess != null) shutdown();
        else if (options.PostToUIThread != null) options.PostToUIThread(shutdown);
        else shutdown();

        batProcess?.Dispose();
        return null;
    }

    /// <summary>Reconciles shell state and stops exact installed processes before file removal.</summary>
    public TrayAppDotNETInstallResult PrepareUninstall(InstallScope scope)
    {
        if (scope is not (InstallScope.LocalAppData or InstallScope.ProgramFiles))
            return new TrayAppDotNETInstallResult(Success: false, $"Unsupported uninstall scope: {scope}");
        if (scope == InstallScope.ProgramFiles && !IsElevated(Identity.WriteLog))
        {
            return new TrayAppDotNETInstallResult(Success: false,
                ErrorMessage: "System uninstall preparation requires elevation");
        }

        try
        {
            ReconcileStartupShortcut(scope);
            options.SyncStartMenu?.Invoke(scope, scope == InstallScope.ProgramFiles);

            TrayAppDotNETInstallResult desktopResult = DesktopShortcut.SetEnabled(scope, enabled: false);
            if (!desktopResult.Success)
                Identity.WriteLog($"PrepareUninstall: {desktopResult.ErrorMessage}");

            _ = WindowsUninstallRegistry.Remove(scope, Identity);
            RemoveLegacyRunEntry();
            StopInstalledProcesses(scope);
            return desktopResult.Success
                ? new TrayAppDotNETInstallResult(true)
                : desktopResult;
        }
        catch (Exception exception)
        {
            Identity.WriteLog($"TrayAppDotNETInstallationService.PrepareUninstall({scope}): {exception}");
            return new TrayAppDotNETInstallResult(Success: false, exception.Message);
        }
    }

    private void ReconcileStartupShortcut(InstallScope removingScope)
    {
        string shortcutPath = Identity.StartupShortcutPath;
        if (!File.Exists(shortcutPath)) return;

        string replacement = removingScope switch
        {
            InstallScope.LocalAppData when File.Exists(Layout.ProgramFilesInstallExecutable) =>
                Layout.ProgramFilesInstallExecutable,
            InstallScope.ProgramFiles when File.Exists(Layout.LocalAppDataInstallExecutable) =>
                Layout.LocalAppDataInstallExecutable,
            _ => string.Empty
        };
        if (!string.IsNullOrWhiteSpace(replacement))
        {
            Interop.ShellLink.Create(shortcutPath, replacement, Identity.ApplicationName);
            return;
        }

        string? currentTarget = Interop.ShellLink.TryRead(shortcutPath, Identity.WriteLog);
        string removedDirectory = removingScope == InstallScope.ProgramFiles
            ? Layout.ProgramFilesInstallDirectory
            : Layout.LocalAppDataInstallDirectory;
        if (currentTarget != null && IsPathWithin(currentTarget, removedDirectory))
            File.Delete(shortcutPath);
    }

    private void RemoveLegacyRunEntry()
    {
        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                Identity.LegacyRunKeyRegistryPath,
                writable: true);
            key?.DeleteValue(Identity.ApplicationName, throwOnMissingValue: false);
        }
        catch (Exception exception)
        {
            Identity.WriteLog($"PrepareUninstall: legacy Run entry cleanup failed: {exception.Message}");
        }
    }

    private void StopInstalledProcesses(InstallScope scope)
    {
        string targetExecutable = Path.GetFullPath(scope == InstallScope.ProgramFiles
            ? Layout.ProgramFilesInstallExecutable
            : Layout.LocalAppDataInstallExecutable);

        for (int attempt = 1; attempt <= UninstallProcessStopAttempts; attempt++)
        {
            List<Process> processes = FindInstalledProcesses(targetExecutable);
            if (processes.Count == 0) return;

            foreach (Process process in processes)
            {
                try
                {
                    if (!process.HasExited) process.Kill(true);
                }
                catch (Exception exception)
                {
                    Identity.WriteLog(
                        $"PrepareUninstall: could not stop PID {SafeProcessID(process)}: {exception.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (attempt < UninstallProcessStopAttempts)
                Thread.Sleep(TimeConstants.UninstallProcessRetryDelayMs);
        }

        List<Process> remaining = FindInstalledProcesses(targetExecutable);
        try
        {
            if (remaining.Count > 0)
            {
                string processIDs = string.Join(separator: ", ", remaining.Select(SafeProcessID));
                throw new IOException($"Could not stop installed process IDs: {processIDs}");
            }
        }
        finally
        {
            foreach (Process process in remaining) process.Dispose();
        }
    }

    private static List<Process> FindInstalledProcesses(string targetExecutable)
    {
        List<Process> matches = [];
        foreach (Process process in Process.GetProcesses())
        {
            bool keep = false;
            try
            {
                if (process.Id == Environment.ProcessId || process.HasExited) continue;
                string? executable = process.MainModule?.FileName;
                keep = executable != null
                       && string.Equals(
                           Path.GetFullPath(executable),
                           targetExecutable,
                           StringComparison.OrdinalIgnoreCase);
                if (keep) matches.Add(process);
            }
            catch
            {
            }
            finally
            {
                if (!keep) process.Dispose();
            }
        }

        return matches;
    }

    private static int SafeProcessID(Process process)
    {
        try { return process.Id; }
        catch { return 0; }
    }

    private static bool IsPathWithin(string path, string directory)
    {
        string normalizedPath = Path.GetFullPath(path);
        string normalizedDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)
                                     + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
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
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using Process? process = Process.Start(psi);
            if (process == null)
                return new TrayAppDotNETInstallResult(Success: false, ErrorMessage: "Failed to start elevated process");

            process.WaitForExit();
            return process.ExitCode == 0
                ? new TrayAppDotNETInstallResult(true)
                : new TrayAppDotNETInstallResult(Success: false,
                    $"Elevated process exited with code {process.ExitCode}");
        }
        catch (Win32Exception ex) when ((uint)ex.NativeErrorCode == 0x800704C7 || ex.NativeErrorCode == 1223)
        {
            return new TrayAppDotNETInstallResult(Success: false, UserCancelled: true);
        }
        catch (Exception ex)
        {
            Identity.WriteLog($"TrayAppDotNETInstallationService.TryInvokeElevated: {ex}");
            return new TrayAppDotNETInstallResult(Success: false, ex.Message);
        }
    }

    internal TrayAppDotNETInstallResult ApplyInstallOptions(
        InstallScope scope,
        bool allUsers,
        TrayAppDotNETInstallOptions? installOptions)
    {
        if (installOptions != null)
        {
            Identity.WriteLog(
                $"TrayAppDotNETInstallationService.ApplyInstallOptions: scope={scope}, "
                + $"desktopShortcut={installOptions.CreateDesktopShortcut}, "
                + $"startMenuShortcut={installOptions.CreateStartMenuShortcut}");
        }

        InstallScope? removingStartMenuScope = installOptions is { CreateStartMenuShortcut: false }
            ? scope
            : null;

        options.SyncStartMenu?.Invoke(removingStartMenuScope, allUsers);

        // Existing callers did not manage desktop shortcuts. Only an explicit choice may alter one.
        if (installOptions == null) return new TrayAppDotNETInstallResult(true);

        TrayAppDotNETInstallResult desktopResult = DesktopShortcut.SetEnabled(
            scope,
            installOptions.CreateDesktopShortcut);
        if (desktopResult.Success) return desktopResult;

        return new TrayAppDotNETInstallResult(
            Success: false,
            "Application files were installed, but the desktop shortcut could not be updated: "
            + desktopResult.ErrorMessage);
    }

    internal static string BuildElevatedInstallArguments(
        string sourceExecutable,
        int buildNumber,
        TrayAppDotNETInstallOptions? installOptions)
    {
        string arguments =
            $"{TrayAppDotNETInstallOptions.SystemInstallArgument} "
            + $"{TrayAppDotNETInstallOptions.SourceExecutableArgument} \"{sourceExecutable}\" "
            + $"{TrayAppDotNETInstallOptions.BuildNumberArgument} {buildNumber}";
        if (installOptions == null) return arguments;

        return arguments
               + $" {TrayAppDotNETInstallOptions.DesktopShortcutArgument} "
               + FormatBooleanArgument(installOptions.CreateDesktopShortcut)
               + $" {TrayAppDotNETInstallOptions.StartMenuShortcutArgument} "
               + FormatBooleanArgument(installOptions.CreateStartMenuShortcut);
    }

    private static string FormatBooleanArgument(bool value) => value ? "true" : "false";

    private static bool ShouldCopySourceDirectoryRootFile(string sourceFile, string installedExecutableFileName)
    {
        string fileName = Path.GetFileName(sourceFile);
        if (string.IsNullOrWhiteSpace(fileName)) return false;

        string extension = Path.GetExtension(fileName);
        if (string.Equals(extension, b: ".bat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, b: ".cmd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, b: ".ps1", StringComparison.OrdinalIgnoreCase))
            return false;

        if (fileName.StartsWith(value: "TrayAppDotNETCommon.XmlSourceGenerator.", StringComparison.OrdinalIgnoreCase))
            return false;

        string applicationName = Path.GetFileNameWithoutExtension(installedExecutableFileName);
        return !IsSiblingTrayAppRootFile(fileName, applicationName);
    }

    private static bool IsSiblingTrayAppRootFile(string fileName, string applicationName)
    {
        if (TryGetStructuredJsonBaseName(fileName, suffix: ".deps.json", out string? baseName)
            || TryGetStructuredJsonBaseName(fileName, suffix: ".runtimeconfig.json", out baseName))
            return baseName is not null && IsOtherTrayAppName(baseName, applicationName);

        string extension = Path.GetExtension(fileName);
        if (!string.Equals(extension, b: ".exe", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, b: ".dll", StringComparison.OrdinalIgnoreCase))
            return false;

        string stem = Path.GetFileNameWithoutExtension(fileName);
        if (IsOtherTrayAppName(stem, applicationName)) return true;

        const string watcherSuffix = "_Watcher";
        return stem.EndsWith(watcherSuffix, StringComparison.OrdinalIgnoreCase)
               && IsOtherTrayAppName(stem[..^watcherSuffix.Length], applicationName);
    }

    private static bool TryGetStructuredJsonBaseName(string fileName, string suffix, out string? baseName)
    {
        if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            baseName = null;
            return false;
        }

        baseName = fileName[..^suffix.Length];
        return true;
    }

    private static bool IsOtherTrayAppName(string name, string applicationName) =>
        name.EndsWith(value: "TrayAppDotNET", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(name, applicationName, StringComparison.OrdinalIgnoreCase);

    private static void CopyFileIfDifferent(string sourceFile, string destinationFile)
    {
        if (string.Equals(
                PathNormalization.Normalize(sourceFile),
                PathNormalization.Normalize(destinationFile),
                StringComparison.OrdinalIgnoreCase))
            return;

        string? destinationDirectory = Path.GetDirectoryName(destinationFile);
        if (!string.IsNullOrEmpty(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);

        if (File.Exists(destinationFile) && IsDllFile(sourceFile) && DllContentsMatch(sourceFile, destinationFile))
            return;

        File.Copy(sourceFile, destinationFile, overwrite: true);
    }

    private static bool IsDllFile(string path) =>
        string.Equals(Path.GetExtension(path), b: ".dll", StringComparison.OrdinalIgnoreCase);

    private static bool DllContentsMatch(string sourceFile, string destinationFile)
    {
        FileInfo source = new(sourceFile);
        FileInfo destination = new(destinationFile);
        return source.Length == destination.Length
               && File.ReadAllBytes(sourceFile).AsSpan().SequenceEqual(File.ReadAllBytes(destinationFile));
    }

    private static void CopyDirectoryMerge(string sourceDirectory, string destinationDirectory)
    {
        if (string.Equals(
                PathNormalization.Normalize(sourceDirectory),
                PathNormalization.Normalize(destinationDirectory),
                StringComparison.OrdinalIgnoreCase))
            return;

        Directory.CreateDirectory(destinationDirectory);

        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory, searchPattern: "*",
                     SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (string file in Directory.EnumerateFiles(sourceDirectory, searchPattern: "*",
                     SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, file);
            CopyFileIfDifferent(file, Path.Combine(destinationDirectory, relativePath));
        }
    }
}
