using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using TrayAppDotNETCommon.Localization;
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
/// Describes an uninstall started by <see cref="TrayAppDotNETInstallationService.RunUninstall"/>.
/// The caller owns <see cref="Process"/> when one is returned. <see cref="Completion"/> finishes after the
/// final progress report has been delivered and never faults.
/// </summary>
public sealed record TrayAppDotNETUninstallRun(Process? Process, bool UserCancelled, Task Completion)
{
    public static readonly TrayAppDotNETUninstallRun WithoutProcess =
        new(Process: null, UserCancelled: false, Task.CompletedTask);

    public static readonly TrayAppDotNETUninstallRun Cancelled =
        new(Process: null, UserCancelled: true, Task.CompletedTask);
}

/// <summary>
/// App-agnostic installer for TrayAppDotNET publish payloads.
/// The caller supplies app identity, install layout, payload contents, current build number,
/// and optional hooks for UI-thread shutdown and Start Menu reconciliation.
/// Every long operation accepts an optional progress sink fed with localized stage messages.
/// </summary>
public sealed class TrayAppDotNETInstallationService(TrayAppDotNETInstallationOptions options)
{
    private const int UninstallProcessStopAttempts = 20;
    private const int ElevatedProgressDrainTimeoutMs = 2000;

    // Install stage percentages
    private const int InstallStoppingInstancesPercent = 0;
    private const int InstallCheckingFilesPercent = 5;
    private const int InstallCopyStartPercent = 10;
    private const int InstallCopyEndPercent = 85;
    private const int InstallRegisteringPercent = 88;
    private const int InstallShortcutsPercent = 92;

    // Uninstall stage percentages; the batch file removal stage follows the helper stages
    private const int UninstallStartupShortcutPercent = 5;
    private const int UninstallStartMenuPercent = 20;
    private const int UninstallDesktopShortcutPercent = 35;
    private const int UninstallRegistryPercent = 50;
    private const int UninstallStoppingInstancesPercent = 60;
    private const int UninstallDeletingSettingsPercent = 75;
    private const int UninstallRemovingFilesPercent = 85;

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
        TrayAppDotNETInstallOptions? installOptions = null,
        IProgress<TrayAppDotNETInstallProgress>? progress = null)
    {
        sourceExe ??= Environment.ProcessPath ?? string.Empty;
        if (!File.Exists(sourceExe))
            return Fail(progress, errorMessage: "Cannot determine running executable path");

        try
        {
            return InstallPayload(
                InstallScope.LocalAppData,
                sourceExe,
                options.CurrentBuildNumber,
                installOptions,
                progress);
        }
        catch (Exception ex)
        {
            Identity.WriteLog($"TrayAppDotNETInstallationService.InstallToLocalAppData: {ex}");
            return Fail(progress, ex.Message);
        }
    }

    public TrayAppDotNETInstallResult InstallSystemWide(
        string? sourceExe = null,
        TrayAppDotNETInstallOptions? installOptions = null,
        IProgress<TrayAppDotNETInstallProgress>? progress = null)
    {
        sourceExe ??= Environment.ProcessPath ?? string.Empty;
        if (!File.Exists(sourceExe))
            return Fail(progress, errorMessage: "Cannot determine running executable path");

        if (IsElevated(Identity.WriteLog))
            return RunAdminInstallSystem(sourceExe, options.CurrentBuildNumber, installOptions, progress);

        return InvokeElevatedInstall(sourceExe, installOptions, progress);
    }

    public TrayAppDotNETInstallResult RunAdminInstallSystem(
        string sourceExe,
        int buildNumber,
        TrayAppDotNETInstallOptions? installOptions = null,
        IProgress<TrayAppDotNETInstallProgress>? progress = null)
    {
        try
        {
            if (!IsElevated(Identity.WriteLog))
                return Fail(progress, errorMessage: "System installation requires elevation");

            if (!File.Exists(sourceExe))
                return Fail(progress, $"Source exe not found: {sourceExe}");

            return InstallPayload(InstallScope.ProgramFiles, sourceExe, buildNumber, installOptions, progress);
        }
        catch (Exception ex)
        {
            Identity.WriteLog($"TrayAppDotNETInstallationService.RunAdminInstallSystem: {ex}");
            return Fail(progress, ex.Message);
        }
    }

    /// <summary>Runs the staged install for one scope and reports every stage.</summary>
    private TrayAppDotNETInstallResult InstallPayload(
        InstallScope scope,
        string sourceExe,
        int buildNumber,
        TrayAppDotNETInstallOptions? installOptions,
        IProgress<TrayAppDotNETInstallProgress>? progress)
    {
        bool allUsers = scope == InstallScope.ProgramFiles;
        string destinationDirectory = allUsers
            ? Layout.ProgramFilesInstallDirectory
            : Layout.LocalAppDataInstallDirectory;
        string destinationExecutable = allUsers
            ? Layout.ProgramFilesInstallExecutable
            : Layout.LocalAppDataInstallExecutable;

        ReportStage(progress, InstallStoppingInstancesPercent, nameof(CommonStrings.Install_Progress_StoppingInstances));
        StopInstalledProcesses(scope);

        ReportStage(progress, InstallCheckingFilesPercent, nameof(CommonStrings.Install_Progress_CheckingFiles));
        TrayAppDotNETInstallResult copyResult = CopyInstallPayload(
            sourceExe,
            destinationDirectory,
            destinationExecutable,
            progress);
        if (!copyResult.Success) return Fail(progress, copyResult.ErrorMessage);

        ReportStage(progress, InstallRegisteringPercent, nameof(CommonStrings.Install_Progress_RegisteringUninstall));
        WindowsUninstallRegistry.Write(
            scope,
            destinationDirectory,
            buildNumber,
            Identity,
            Layout.InstalledExecutableFileName);

        ReportStage(progress, InstallShortcutsPercent, nameof(CommonStrings.Install_Progress_UpdatingShortcuts));
        TrayAppDotNETInstallResult result = ApplyInstallOptions(scope, allUsers, installOptions);
        if (!result.Success) return Fail(progress, result.ErrorMessage);

        ReportStage(
            progress,
            TrayAppDotNETInstallProgress.CompletePercent,
            nameof(CommonStrings.Install_Progress_Complete));
        return result;
    }

    /// <summary>Starts the elevated helper and relays its stage reports through a named pipe.</summary>
    private TrayAppDotNETInstallResult InvokeElevatedInstall(
        string sourceExe,
        TrayAppDotNETInstallOptions? installOptions,
        IProgress<TrayAppDotNETInstallProgress>? progress)
    {
        if (progress == null)
        {
            return TryInvokeElevated(
                BuildElevatedInstallArguments(sourceExe, options.CurrentBuildNumber, installOptions),
                sourceExe);
        }

        ReportStage(progress, percent: 0, nameof(CommonStrings.Install_Progress_RequestingElevation));
        TrayAppDotNETProgressTracker tracker = new(progress);
        using TrayAppDotNETProgressPipeServer pipeServer = new(
            TrayAppDotNETProgressPipeServer.CreatePipeName(Identity.ApplicationName),
            tracker,
            Identity.WriteLog);
        TrayAppDotNETInstallResult result = TryInvokeElevated(
            BuildElevatedInstallArguments(sourceExe, options.CurrentBuildNumber, installOptions, pipeServer.PipeName),
            sourceExe);
        DrainProgress(pipeServer);

        // The helper reports its own outcome; only fill in when it never reached a terminal update
        if (tracker.IsTerminal) return result;

        if (result.Success)
        {
            ReportStage(
                progress,
                TrayAppDotNETInstallProgress.CompletePercent,
                nameof(CommonStrings.Install_Progress_Complete));
            return result;
        }

        string failureMessage = result.UserCancelled
            ? L(nameof(CommonStrings.Install_Progress_Cancelled))
            : result.ErrorMessage ?? string.Empty;
        progress.Report(TrayAppDotNETInstallProgress.Failed(failureMessage));
        return result;
    }

    public TrayAppDotNETInstallResult CopyInstallPayload(
        string sourceExe,
        string destinationDirectory,
        string destinationExe,
        IProgress<TrayAppDotNETInstallProgress>? progress = null)
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

            List<InstallCopyItem> copyPlan = BuildCopyPlan(sourceExe, sourceDirectory, destinationDirectory, destinationExe);
            Directory.CreateDirectory(destinationDirectory);
            int copiedCount = 0;
            foreach (InstallCopyItem item in copyPlan)
            {
                if (item.IsDirectory) Directory.CreateDirectory(item.Destination);
                else CopyFileIfDifferent(item.Source, item.Destination);

                copiedCount++;
                ReportCopyProgress(progress, copiedCount, copyPlan.Count);
            }

            return new TrayAppDotNETInstallResult(true);
        }
        catch (Exception ex)
        {
            Identity.WriteLog($"TrayAppDotNETInstallationService.CopyInstallPayload: {ex}");
            return new TrayAppDotNETInstallResult(Success: false, ex.Message);
        }
    }

    /// <summary>Lists every file and directory to copy so progress can be reported against a known total.</summary>
    private List<InstallCopyItem> BuildCopyPlan(
        string sourceExe,
        string sourceDirectory,
        string destinationDirectory,
        string destinationExe)
    {
        List<InstallCopyItem> copyPlan = [];
        copyPlan.Add(new InstallCopyItem(sourceExe, destinationExe, IsDirectory: false));

        foreach (TrayAppDotNETInstallFile file in Payload.RequiredFiles)
        {
            copyPlan.Add(new InstallCopyItem(
                Path.Combine(sourceDirectory, file.Name),
                Path.Combine(destinationDirectory, file.Name),
                IsDirectory: false));
        }

        foreach (TrayAppDotNETInstallFile file in Payload.OptionalFiles)
        {
            string sourceFile = Path.Combine(sourceDirectory, file.Name);
            if (!File.Exists(sourceFile)) continue;

            copyPlan.Add(new InstallCopyItem(
                sourceFile,
                Path.Combine(destinationDirectory, file.Name),
                IsDirectory: false));
        }

        if (Payload.CopySourceDirectoryRootFiles)
        {
            foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory))
            {
                if (!ShouldCopySourceDirectoryRootFile(sourceFile, Layout.InstalledExecutableFileName))
                    continue;

                copyPlan.Add(new InstallCopyItem(
                    sourceFile,
                    Path.Combine(destinationDirectory, Path.GetFileName(sourceFile)),
                    IsDirectory: false));
            }
        }

        foreach (TrayAppDotNETInstallDirectory directory in Payload.RequiredDirectories)
        {
            AddDirectoryMergePlan(
                copyPlan,
                Path.Combine(sourceDirectory, directory.Name),
                Path.Combine(destinationDirectory, directory.Name));
        }

        foreach (TrayAppDotNETInstallDirectory directory in Payload.OptionalDirectories)
        {
            string sourcePath = Path.Combine(sourceDirectory, directory.Name);
            if (!Directory.Exists(sourcePath)) continue;

            AddDirectoryMergePlan(copyPlan, sourcePath, Path.Combine(destinationDirectory, directory.Name));
        }

        return copyPlan;
    }

    private static void AddDirectoryMergePlan(
        List<InstallCopyItem> copyPlan,
        string sourceDirectory,
        string destinationDirectory)
    {
        if (string.Equals(
                PathNormalization.Normalize(sourceDirectory),
                PathNormalization.Normalize(destinationDirectory),
                StringComparison.OrdinalIgnoreCase))
            return;

        copyPlan.Add(new InstallCopyItem(sourceDirectory, destinationDirectory, IsDirectory: true));

        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory, searchPattern: "*",
                     SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, directory);
            copyPlan.Add(new InstallCopyItem(
                directory,
                Path.Combine(destinationDirectory, relativePath),
                IsDirectory: true));
        }

        foreach (string file in Directory.EnumerateFiles(sourceDirectory, searchPattern: "*",
                     SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, file);
            copyPlan.Add(new InstallCopyItem(
                file,
                Path.Combine(destinationDirectory, relativePath),
                IsDirectory: false));
        }
    }

    private static void ReportCopyProgress(IProgress<TrayAppDotNETInstallProgress>? progress, int copied, int total)
    {
        if (progress == null || total <= 0) return;

        int percent = InstallCopyStartPercent + (InstallCopyEndPercent - InstallCopyStartPercent) * copied / total;
        string message = string.Format(
            CultureInfo.CurrentCulture,
            L(nameof(CommonStrings.Install_Progress_CopyingFiles_Format)),
            copied,
            total);
        progress.Report(TrayAppDotNETInstallProgress.At(percent, message));
    }

    /// <summary>
    /// Starts the batch uninstall. Stage reports from the helper arrive through a named pipe, the file
    /// removal stage is reported when the helper finishes, and completion follows the batch exit code.
    /// </summary>
    public TrayAppDotNETUninstallRun RunUninstall(
        InstallScope scope,
        bool deleteSettings,
        Action? shutdownCurrentProcess = null,
        IProgress<TrayAppDotNETInstallProgress>? progress = null)
    {
        string installDirectory = scope switch
        {
            InstallScope.LocalAppData => Layout.LocalAppDataInstallDirectory,
            InstallScope.ProgramFiles => Layout.ProgramFilesInstallDirectory,
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(installDirectory)) return TrayAppDotNETUninstallRun.WithoutProcess;

        TrayAppDotNETProgressTracker? tracker = progress == null ? null : new TrayAppDotNETProgressTracker(progress);
        TrayAppDotNETProgressPipeServer? pipeServer = tracker == null
            ? null
            : new TrayAppDotNETProgressPipeServer(
                TrayAppDotNETProgressPipeServer.CreatePipeName(Identity.ApplicationName),
                tracker,
                Identity.WriteLog);

        Process? batProcess = UninstallScript.Run(
            installDirectory,
            scope,
            deleteSettings,
            Identity,
            Layout.InstalledExecutableFileName,
            Payload,
            pipeServer?.PipeName,
            out bool userCancelled);

        if (userCancelled)
        {
            pipeServer?.Dispose();
            tracker?.Report(TrayAppDotNETInstallProgress.Failed(L(nameof(CommonStrings.Uninstall_Progress_Cancelled))));
            return TrayAppDotNETUninstallRun.Cancelled;
        }

        if (batProcess == null)
        {
            pipeServer?.Dispose();
            tracker?.Report(TrayAppDotNETInstallProgress.Failed(L(nameof(CommonStrings.Uninstall_Progress_NotStarted))));
            return TrayAppDotNETUninstallRun.WithoutProcess;
        }

        string runningExe = PathNormalization.Normalize(Environment.ProcessPath);
        string installExecutable = PathNormalization.Normalize(
            Path.Combine(installDirectory, Layout.InstalledExecutableFileName));
        bool runningFromInstall = !string.IsNullOrEmpty(runningExe)
                                  && string.Equals(runningExe, installExecutable, StringComparison.OrdinalIgnoreCase);

        if (!runningFromInstall)
        {
            Task completion = tracker == null
                ? Task.CompletedTask
                : MonitorUninstallAsync(batProcess.Id, pipeServer, tracker);
            return new TrayAppDotNETUninstallRun(batProcess, UserCancelled: false, completion);
        }

        // This process is the installed instance; it exits now and nothing remains to observe the stages
        pipeServer?.Dispose();
        Action shutdown = shutdownCurrentProcess ?? (() => Environment.Exit(0));
        if (shutdownCurrentProcess != null) shutdown();
        else if (options.PostToUIThread != null) options.PostToUIThread(shutdown);
        else shutdown();

        batProcess.Dispose();
        return TrayAppDotNETUninstallRun.WithoutProcess;
    }

    private async Task MonitorUninstallAsync(
        int processID,
        TrayAppDotNETProgressPipeServer? pipeServer,
        TrayAppDotNETProgressTracker tracker)
    {
        try
        {
            using Process observer = Process.GetProcessById(processID);
            // Retain the handle so ExitCode remains available after the externally opened process exits
            _ = observer.SafeHandle;
            Task exitTask = observer.WaitForExitAsync();
            if (pipeServer != null)
            {
                await Task.WhenAny(pipeServer.Completion, exitTask).ConfigureAwait(false);
                if (!exitTask.IsCompleted && !tracker.IsTerminal)
                {
                    ReportStage(
                        tracker,
                        UninstallRemovingFilesPercent,
                        nameof(CommonStrings.Uninstall_Progress_RemovingFiles));
                }
            }

            await exitTask.ConfigureAwait(false);
            if (tracker.IsTerminal) return;

            int exitCode = observer.ExitCode;
            if (exitCode == 0)
            {
                ReportStage(
                    tracker,
                    TrayAppDotNETInstallProgress.CompletePercent,
                    nameof(CommonStrings.Uninstall_Progress_Complete));
                return;
            }

            tracker.Report(TrayAppDotNETInstallProgress.Failed(string.Format(
                CultureInfo.CurrentCulture,
                L(nameof(CommonStrings.Uninstall_Progress_Failed_Format)),
                exitCode)));
        }
        catch (ArgumentException)
        {
            // The batch finished before it could be observed
            if (!tracker.IsTerminal)
            {
                ReportStage(
                    tracker,
                    TrayAppDotNETInstallProgress.CompletePercent,
                    nameof(CommonStrings.Uninstall_Progress_Complete));
            }
        }
        catch (Exception exception)
        {
            Identity.WriteLog($"TrayAppDotNETInstallationService.MonitorUninstall: {exception}");
            if (!tracker.IsTerminal) tracker.Report(TrayAppDotNETInstallProgress.Failed(exception.Message));
        }
        finally
        {
            pipeServer?.Dispose();
        }
    }

    /// <summary>Reconciles shell state and stops exact installed processes before file removal.</summary>
    public TrayAppDotNETInstallResult PrepareUninstall(
        InstallScope scope,
        bool deleteSettings = false,
        IProgress<TrayAppDotNETInstallProgress>? progress = null)
    {
        if (scope is not (InstallScope.LocalAppData or InstallScope.ProgramFiles))
            return Fail(progress, $"Unsupported uninstall scope: {scope}");
        if (scope == InstallScope.ProgramFiles && !IsElevated(Identity.WriteLog))
            return Fail(progress, errorMessage: "System uninstall preparation requires elevation");

        try
        {
            ReportStage(progress, UninstallStartupShortcutPercent, nameof(CommonStrings.Uninstall_Progress_StartupShortcut));
            ReconcileStartupShortcut(scope);

            ReportStage(progress, UninstallStartMenuPercent, nameof(CommonStrings.Uninstall_Progress_StartMenu));
            options.SyncStartMenu?.Invoke(scope, scope == InstallScope.ProgramFiles);

            ReportStage(progress, UninstallDesktopShortcutPercent, nameof(CommonStrings.Uninstall_Progress_DesktopShortcut));
            TrayAppDotNETInstallResult desktopResult = DesktopShortcut.SetEnabled(scope, enabled: false);
            if (!desktopResult.Success)
                Identity.WriteLog($"PrepareUninstall: {desktopResult.ErrorMessage}");

            ReportStage(progress, UninstallRegistryPercent, nameof(CommonStrings.Uninstall_Progress_Registry));
            _ = WindowsUninstallRegistry.Remove(scope, Identity);
            RemoveLegacyRunEntry();

            ReportStage(progress, UninstallStoppingInstancesPercent, nameof(CommonStrings.Uninstall_Progress_StoppingInstances));
            StopInstalledProcesses(scope);

            if (deleteSettings)
            {
                ReportStage(progress, UninstallDeletingSettingsPercent, nameof(CommonStrings.Uninstall_Progress_DeletingSettings));
                DeleteSettingsDirectory();
            }

            return desktopResult.Success
                ? new TrayAppDotNETInstallResult(true)
                : Fail(progress, desktopResult.ErrorMessage);
        }
        catch (Exception exception)
        {
            Identity.WriteLog($"TrayAppDotNETInstallationService.PrepareUninstall({scope}): {exception}");
            return Fail(progress, exception.Message);
        }
    }

    /// <summary>Removes the settings directory; the batch stage repeats the removal once every process is gone.</summary>
    private void DeleteSettingsDirectory()
    {
        string settingsDirectory = Identity.SettingsDirectory;
        if (string.IsNullOrWhiteSpace(settingsDirectory) || !Directory.Exists(settingsDirectory)) return;

        try
        {
            Directory.Delete(settingsDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Identity.WriteLog(
                $"PrepareUninstall: settings removal deferred to the batch stage: {exception.Message}");
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
        TrayAppDotNETInstallOptions? installOptions,
        string? progressPipeName = null)
    {
        string arguments =
            $"{TrayAppDotNETInstallOptions.SystemInstallArgument} "
            + $"{TrayAppDotNETInstallOptions.SourceExecutableArgument} \"{sourceExecutable}\" "
            + $"{TrayAppDotNETInstallOptions.BuildNumberArgument} {buildNumber}";
        if (installOptions != null)
        {
            arguments += $" {TrayAppDotNETInstallOptions.DesktopShortcutArgument} "
                         + FormatBooleanArgument(installOptions.CreateDesktopShortcut)
                         + $" {TrayAppDotNETInstallOptions.StartMenuShortcutArgument} "
                         + FormatBooleanArgument(installOptions.CreateStartMenuShortcut);
        }

        if (!string.IsNullOrWhiteSpace(progressPipeName))
            arguments += $" {TrayAppDotNETInstallOptions.ProgressPipeArgument} {progressPipeName}";

        return arguments;
    }

    private static string FormatBooleanArgument(bool value) => value ? "true" : "false";

    private static void DrainProgress(TrayAppDotNETProgressPipeServer pipeServer)
    {
        try
        {
            _ = pipeServer.Completion.Wait(ElevatedProgressDrainTimeoutMs);
        }
        catch (AggregateException exception)
        {
            TADNLog.Log($"TrayAppDotNETInstallationService.DrainProgress: {exception.InnerException?.Message}");
        }
    }

    private static TrayAppDotNETInstallResult Fail(IProgress<TrayAppDotNETInstallProgress>? progress, string? errorMessage)
    {
        progress?.Report(TrayAppDotNETInstallProgress.Failed(errorMessage ?? string.Empty));
        return new TrayAppDotNETInstallResult(Success: false, errorMessage);
    }

    private static void ReportStage(IProgress<TrayAppDotNETInstallProgress>? progress, int percent, string messageKey) =>
        progress?.Report(TrayAppDotNETInstallProgress.At(percent, L(messageKey)));

    private static string L(string key) => LocalizationManager.Instance[key];

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

    /// <summary>One planned copy: a file to copy or a directory to create.</summary>
    private readonly record struct InstallCopyItem(string Source, string Destination, bool IsDirectory);
}

/// <summary>Forwards progress updates and remembers whether a terminal update has been delivered.</summary>
internal sealed class TrayAppDotNETProgressTracker(IProgress<TrayAppDotNETInstallProgress> target)
    : IProgress<TrayAppDotNETInstallProgress>
{
    private int _terminal;

    public bool IsTerminal => Volatile.Read(ref _terminal) != 0;

    public void Report(TrayAppDotNETInstallProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.IsComplete || value.IsFailure) Volatile.Write(ref _terminal, value: 1);
        target.Report(value);
    }
}
