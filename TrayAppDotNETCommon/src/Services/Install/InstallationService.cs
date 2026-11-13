using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using TrayAppDotNETCommon.Contracts;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Ipc;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.Services.Install;

public sealed record TrayAppDotNETInstallationOptions(
    TrayAppDotNETInstallIdentity Identity,
    TrayAppDotNETInstallLayout Layout,
    TrayAppDotNETInstallPayload Payload,
    int CurrentBuildNumber,
    Action<InstallScope?, bool>? SyncStartMenu = null,
    Action<Action>? PostToUIThread = null,
    TrayAppDotNETDesktopShortcutOptions? DesktopShortcutOptions = null)
{
    /// <summary>
    /// Starts the removal that runs after the uninstall helper exits. Tests replace it; everything else uses
    /// <see cref="DetachedCleanup.Schedule"/>.
    /// </summary>
    internal Func<IReadOnlyList<DetachedCleanupTarget>, Action<string>, int?> ScheduleRemovalAfterExit { get; init; } =
        static (targets, log) => DetachedCleanup.Schedule(targets, log);

    /// <summary>
    /// Returns why the uninstall of a scope must not start, or null to let it start. <see
    /// cref="TrayAppDotNETInstallationService.RunUninstall"/> asks before it starts the helper, so the reason reaches
    /// the uninstaller window or the <c>--uninstall-headless</c> output even when the helper would replace this
    /// process. Comes from <see cref="TrayAppDotNETUninstallHook.CheckUninstall"/>.
    /// </summary>
    public Func<InstallScope, string?>? CheckUninstall { get; init; }

    /// <summary>
    /// The app's Always run as administrator setting, which a system install turns on or off when
    /// <see cref="TrayAppDotNETInstallOptions.RunAsAdministrator"/> asks. Comes from
    /// <see cref="TrayAppDotNETAppInstallServicesOptions.RunAsAdministrator"/>.
    /// </summary>
    public TrayAppDotNETRunAsAdministratorOption? RunAsAdministrator { get; init; }
}

/// <summary>
/// Describes an uninstall started by <see cref="TrayAppDotNETInstallationService.RunUninstall"/>.
/// The caller owns <see cref="Process"/> when one is returned. <see cref="Completion"/> finishes after the
/// final progress report has been delivered, never faults, and is true only when the app's files are gone.
/// </summary>
public sealed record TrayAppDotNETUninstallRun(Process? Process, bool UserCancelled, Task<bool> Completion)
{
    public static readonly TrayAppDotNETUninstallRun WithoutProcess =
        new(Process: null, UserCancelled: false, Task.FromResult(false));

    public static readonly TrayAppDotNETUninstallRun Cancelled =
        new(Process: null, UserCancelled: true, Task.FromResult(false));

    /// <summary>Why no uninstall process was started, when it was refused before starting.</summary>
    public string? ErrorMessage { get; init; }
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
    // With UninstallFileRetryDelayMs between attempts, ten seconds for killed processes to unmap the libraries
    private const int SharedFileDeleteAttempts = 10;
    private const int ElevatedProgressDrainTimeoutMs = 2000;

    // Install stage percentages
    private const int InstallStoppingInstancesPercent = 0;
    private const int InstallCheckingFilesPercent = 5;
    private const int InstallCopyStartPercent = 10;
    private const int InstallCopyEndPercent = 85;
    private const int InstallRegisteringPercent = 88;
    private const int InstallShortcutsPercent = 92;

    // Uninstall stage percentages; the detached removal of the helper's own exe follows the helper stages
    private const int UninstallStartupShortcutPercent = 5;
    private const int UninstallStartMenuPercent = 20;
    private const int UninstallDesktopShortcutPercent = 35;
    private const int UninstallRegistryPercent = 50;
    private const int UninstallStoppingInstancesPercent = 60;
    private const int UninstallDeletingSettingsPercent = 75;
    private const int UninstallRemovingFilesPercent = 85;

    private TrayAppDotNETStartupManager? _startup;

    public TrayAppDotNETInstallIdentity Identity => options.Identity;

    public TrayAppDotNETInstallLayout Layout => options.Layout;

    public TrayAppDotNETInstallPayload Payload => options.Payload;

    /// <summary>The build of the running exe, which a transitional update worker records as the installed build.</summary>
    public int CurrentBuildNumber => options.CurrentBuildNumber;

    public TrayAppDotNETDesktopShortcut DesktopShortcut { get; } = new(
        options.DesktopShortcutOptions
        ?? new TrayAppDotNETDesktopShortcutOptions(
            options.Identity.ApplicationName,
            options.Layout,
            options.Identity.WriteLog));

    private TrayAppDotNETStartupManager Startup => _startup ??= new TrayAppDotNETStartupManager(
        new TrayAppDotNETStartupOptions(Identity.ApplicationName, Layout, DetectAll, Identity.WriteLog)
        {
            ShortcutPath = Identity.StartupShortcutPath,
            LegacyRunKeyRegistryPath = Identity.LegacyRunKeyRegistryPath
        });

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

    /// <summary>The Local or System scope whose installed exe is <paramref name="executable"/>, or null (portable).</summary>
    public InstallScope? ScopeOfExecutable(string executable)
    {
        string normalized = PathNormalization.Normalize(executable);
        if (string.Equals(normalized, PathNormalization.Normalize(Layout.ProgramFilesInstallExecutable), StringComparison.OrdinalIgnoreCase))
            return InstallScope.ProgramFiles;
        if (string.Equals(normalized, PathNormalization.Normalize(Layout.LocalAppDataInstallExecutable), StringComparison.OrdinalIgnoreCase))
            return InstallScope.LocalAppData;
        return null;
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

        // In-app updates rewrite DisplayVersion, so the entry tracks the installed build
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

        if (TrayAppDotNETElevationPolicy.ElevationWouldUseAnotherAccount(Identity.WriteLog))
            return Fail(progress, L(nameof(CommonStrings.Elevation_Error_DifferentAdministratorAccount)));

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
        bool scopeWasInstalled = File.Exists(destinationExecutable);

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
        TrayAppDotNETInstallResult result = ApplyInstallOptions(scope, allUsers, installOptions, scopeWasInstalled);
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

    /// <summary>
    /// Copies the payload under the install directory lock. When a shared native DLL changes, every app running
    /// from the folder is stopped first and restarted afterwards; this app's own instances are always stopped.
    /// Shared native DLLs are copied first and the app exe last.
    /// </summary>
    public TrayAppDotNETInstallResult CopyInstallPayload(
        string sourceExe,
        string destinationDirectory,
        string destinationExe,
        IProgress<TrayAppDotNETInstallProgress>? progress = null)
    {
        List<string> restartAfterCopy = [];
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
            // Other apps write the same shared files into this folder, so concurrent installs copy one at a time
            using TrayAppDotNETInstallDirectoryLock? directoryLock = TrayAppDotNETInstallDirectoryLock.Acquire(
                destinationDirectory,
                TimeSpan.FromMilliseconds(TimeConstants.InstallDirectoryLockTimeoutMs),
                () => ReportStage(progress, InstallCheckingFilesPercent, nameof(CommonStrings.Install_Progress_WaitingForInstallDirectory)),
                Identity.WriteLog);
            if (directoryLock == null)
            {
                return new TrayAppDotNETInstallResult(Success: false,
                    string.Format(CultureInfo.CurrentCulture, L(nameof(CommonStrings.Install_Error_InstallDirectoryBusy_Format)), destinationDirectory));
            }

            Directory.CreateDirectory(destinationDirectory);
            // An update of this app must not run while its files are overwritten; one left interrupted is rolled
            // back first so its journal and backups do not outlive this install
            using FileStream? updateLock = TryTakeUpdateLock(destinationDirectory);
            if (updateLock == null)
            {
                return new TrayAppDotNETInstallResult(Success: false,
                    string.Format(CultureInfo.CurrentCulture, L(nameof(CommonStrings.Install_Error_UpdateRunning_Format)), Identity.ApplicationName));
            }

            if (!UpdateFileTransaction.RecoverInterrupted(destinationDirectory, Layout.ApplicationName, Identity.WriteLog))
                return new TrayAppDotNETInstallResult(Success: false, "An interrupted update could not be rolled back.");

            bool sharedNativeDllChanges = ChangesSharedNativeDlls(copyPlan);
            HashSet<string> stopExecutables = TrayAppDotNETInstalledProcesses.ExecutablePaths(destinationExe, sharedNativeDllChanges);
            restartAfterCopy = TrayAppDotNETInstalledProcesses.RunningExecutables(stopExecutables)
                .Where(executable => !string.Equals(executable, Path.GetFullPath(destinationExe), StringComparison.OrdinalIgnoreCase))
                .ToList();
            ReportStage(progress, InstallStoppingInstancesPercent, nameof(CommonStrings.Install_Progress_StoppingInstances));
            TrayAppDotNETInstalledProcesses.Stop(
                stopExecutables,
                UninstallProcessStopAttempts,
                TimeConstants.UninstallProcessRetryDelayMs,
                Identity.WriteLog);

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
        finally
        {
            // The other apps were running before the shared DLLs changed; they come back whatever the outcome
            if (restartAfterCopy.Count > 0)
            {
                TrayAppDotNETInstalledProcesses.Start(
                    restartAfterCopy,
                    _ => [TrayAppDotNETInstallContract.HiddenArgument],
                    Identity.WriteLog);
            }
        }
    }

    /// <summary>Takes this app's update lock without waiting; null while an update of the app holds it.</summary>
    private FileStream? TryTakeUpdateLock(string installDirectory)
    {
        try
        {
            return new FileStream(
                UpdateFileTransaction.LockPath(installDirectory, Layout.ApplicationName),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
        }
        catch (IOException exception)
        {
            Identity.WriteLog($"TrayAppDotNETInstallationService: an update of {Layout.ApplicationName} is running: {exception.Message}");
            return null;
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

        foreach (TrayAppDotNETInstallFile file in Payload.RequiredFiles)
            AddFile(copyPlan, sourceDirectory, destinationDirectory, file.Name);

        foreach (TrayAppDotNETInstallFile file in Payload.OptionalFiles)
        {
            if (File.Exists(Path.Combine(sourceDirectory, file.Name)))
                AddFile(copyPlan, sourceDirectory, destinationDirectory, file.Name);
        }

        if (Payload.CopySourceDirectoryRootFiles)
        {
            foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory))
            {
                if (!ShouldCopySourceDirectoryRootFile(sourceFile, Layout.InstalledExecutableFileName))
                    continue;

                AddFile(copyPlan, sourceDirectory, destinationDirectory, Path.GetFileName(sourceFile));
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

        // Native DLLs first and the exe last: a failure part way never leaves a new exe beside old native code
        List<InstallCopyItem> ordered =
        [
            .. copyPlan.Where(item => item.IsSharedNativeDll),
            .. copyPlan.Where(item => !item.IsSharedNativeDll)
        ];
        ordered.Add(new InstallCopyItem(sourceExe, destinationExe, IsDirectory: false));
        return ordered;
    }

    /// <summary>
    /// The install's copy order and whether it replaces a shared native DLL that differs from the installed one,
    /// which means every app in the folder must stop. Call it under the install directory lock.
    /// </summary>
    internal (IReadOnlyList<string> FileDestinations, bool ChangesSharedNativeDlls) PlanInstallCopy(
        string sourceExe,
        string destinationDirectory,
        string destinationExe)
    {
        string sourceDirectory = Path.GetDirectoryName(sourceExe)
                                 ?? throw new InvalidOperationException($"Cannot determine source directory for {sourceExe}");
        List<InstallCopyItem> copyPlan = BuildCopyPlan(sourceExe, sourceDirectory, destinationDirectory, destinationExe);
        return (
            [.. copyPlan.Where(item => !item.IsDirectory).Select(item => item.Destination)],
            ChangesSharedNativeDlls(copyPlan));
    }

    private static bool ChangesSharedNativeDlls(IEnumerable<InstallCopyItem> copyPlan) =>
        copyPlan.Any(item => item.IsSharedNativeDll
                             && File.Exists(item.Destination)
                             && !SamePath(item.Source, item.Destination)
                             && !DllContentsMatch(item.Source, item.Destination));

    private static void AddFile(
        List<InstallCopyItem> copyPlan,
        string sourceDirectory,
        string destinationDirectory,
        string fileName) =>
        copyPlan.Add(new InstallCopyItem(
            Path.Combine(sourceDirectory, fileName),
            Path.Combine(destinationDirectory, fileName),
            IsDirectory: false,
            TrayAppDotNETInstalledProcesses.IsSharedNativeDll(fileName)));

    private static void AddDirectoryMergePlan(
        List<InstallCopyItem> copyPlan,
        string sourceDirectory,
        string destinationDirectory)
    {
        if (SamePath(sourceDirectory, destinationDirectory)) return;

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
    /// Starts the uninstall helper: the installed exe of <paramref name="scope"/> itself, run with
    /// <c>--uninstall-prepare</c> and elevated for a System install. It never runs a script or an exe from a
    /// user-writable folder elevated. Stage reports arrive through a named pipe; completion waits for the helper
    /// and then for the detached removal of its own exe.
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
        string? blockingReason = CheckUninstallAllowed(scope);
        if (blockingReason != null) return RefuseUninstall(tracker, blockingReason);

        string helperExecutable = Path.Combine(installDirectory, Layout.InstalledExecutableFileName);
        if (!File.Exists(helperExecutable))
        {
            return RefuseUninstall(tracker, string.Format(
                CultureInfo.CurrentCulture,
                L(nameof(CommonStrings.Uninstall_Error_InstalledCopyMissing_Format)),
                helperExecutable));
        }

        bool elevate = scope == InstallScope.ProgramFiles && !IsElevated(Identity.WriteLog);
        if (elevate && TrayAppDotNETElevationPolicy.ElevationWouldUseAnotherAccount(Identity.WriteLog))
            return RefuseUninstall(tracker, L(nameof(CommonStrings.Elevation_Error_DifferentAdministratorAccount)));

        TrayAppDotNETProgressPipeServer? pipeServer = tracker == null
            ? null
            : new TrayAppDotNETProgressPipeServer(
                TrayAppDotNETProgressPipeServer.CreatePipeName(Identity.ApplicationName),
                tracker,
                Identity.WriteLog);

        Process? helper;
        ProcessExitHandle? observer;
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = helperExecutable,
                WorkingDirectory = Path.GetDirectoryName(helperExecutable) ?? installDirectory,
                UseShellExecute = elevate,
                CreateNoWindow = !elevate,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            if (elevate) startInfo.Verb = "runas";
            foreach (string argument in BuildHelperArguments(
                         scope,
                         deleteSettings,
                         TrayAppDotNETProgram.SettingsDirectoryOverride,
                         pipeServer?.PipeName))
                startInfo.ArgumentList.Add(argument);

            helper = Process.Start(startInfo);
            if (helper == null)
            {
                pipeServer?.Dispose();
                tracker?.Report(TrayAppDotNETInstallProgress.Failed(L(nameof(CommonStrings.Uninstall_Progress_NotStarted))));
                return TrayAppDotNETUninstallRun.WithoutProcess;
            }

            // A handle of its own, so the caller may dispose the returned process at once. Its limited rights are all
            // this process gets on an elevated helper; the helper's handle keeps the ID from being reused meanwhile
            observer = ProcessExitHandle.Open(helper.Id);
        }
        catch (Win32Exception exception) when (TrayAppDotNETElevatedLaunch.IsUserCancelled(exception))
        {
            pipeServer?.Dispose();
            Identity.WriteLog("TrayAppDotNETInstallationService.RunUninstall: UAC prompt was declined");
            tracker?.Report(TrayAppDotNETInstallProgress.Failed(L(nameof(CommonStrings.Uninstall_Progress_Cancelled))));
            return TrayAppDotNETUninstallRun.Cancelled;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException
                                              or ArgumentException or IOException)
        {
            pipeServer?.Dispose();
            Identity.WriteLog($"TrayAppDotNETInstallationService.RunUninstall: {exception}");
            tracker?.Report(TrayAppDotNETInstallProgress.Failed(L(nameof(CommonStrings.Uninstall_Progress_NotStarted))));
            return TrayAppDotNETUninstallRun.WithoutProcess;
        }

        string runningExe = PathNormalization.Normalize(Environment.ProcessPath);
        bool runningFromInstall = !string.IsNullOrEmpty(runningExe)
                                  && string.Equals(runningExe, PathNormalization.Normalize(helperExecutable),
                                      StringComparison.OrdinalIgnoreCase);

        if (!runningFromInstall)
        {
            Task<bool> completion = MonitorUninstallAsync(
                observer,
                pipeServer,
                tracker,
                RemovalTargets(scope, deleteSettings));
            return new TrayAppDotNETUninstallRun(helper, UserCancelled: false, completion);
        }

        // This process is the installed instance; it exits now and nothing remains to observe the stages
        observer.Dispose();
        pipeServer?.Dispose();
        Action shutdown = shutdownCurrentProcess ?? (() => Environment.Exit(0));
        if (shutdownCurrentProcess != null) shutdown();
        else if (options.PostToUIThread != null) options.PostToUIThread(shutdown);
        else shutdown();

        helper.Dispose();
        return TrayAppDotNETUninstallRun.WithoutProcess;
    }

    /// <summary>
    /// The helper's arguments: scope, the settings choice, the settings folder override of this process (so the
    /// helper deletes the folder the user sees, not the default one) and the progress pipe.
    /// </summary>
    internal static List<string> BuildHelperArguments(
        InstallScope scope,
        bool deleteSettings,
        string? settingsDirectory,
        string? progressPipeName)
    {
        List<string> arguments =
        [
            TrayAppDotNETInstallContract.PrepareUninstallArgument,
            "--scope",
            InstallScopeExtensions.ToArg(scope)
        ];
        if (deleteSettings)
        {
            arguments.Add(TrayAppDotNETInstallContract.DeleteSettingsArgument);
            arguments.Add("true");
        }

        if (settingsDirectory != null)
        {
            arguments.Add(TrayAppDotNETProgram.SettingsDirectoryArgument);
            arguments.Add(settingsDirectory);
        }

        if (!string.IsNullOrWhiteSpace(progressPipeName))
        {
            arguments.Add(TrayAppDotNETInstallContract.ProgressPipeArgument);
            arguments.Add(progressPipeName);
        }

        return arguments;
    }

    private static TrayAppDotNETUninstallRun RefuseUninstall(TrayAppDotNETProgressTracker? tracker, string message)
    {
        tracker?.Report(TrayAppDotNETInstallProgress.Failed(message));
        return TrayAppDotNETUninstallRun.WithoutProcess with { ErrorMessage = message };
    }

    private string? CheckUninstallAllowed(InstallScope scope)
    {
        if (options.CheckUninstall == null) return null;

        try
        {
            string? reason = options.CheckUninstall(scope);
            if (reason != null) Identity.WriteLog($"TrayAppDotNETInstallationService.RunUninstall: refused: {reason}");
            return reason;
        }
        catch (Exception exception)
        {
            // The helper runs the app's uninstall hook again and stops there if its cleanup really fails
            Identity.WriteLog($"TrayAppDotNETInstallationService.RunUninstall: uninstall check failed: {exception}");
            return null;
        }
    }

    private async Task<bool> MonitorUninstallAsync(
        ProcessExitHandle observer,
        TrayAppDotNETProgressPipeServer? pipeServer,
        TrayAppDotNETProgressTracker? tracker,
        IReadOnlyList<DetachedCleanupTarget> removalTargets)
    {
        try
        {
            Task<int> exitTask = observer.WaitForExitAsync();
            if (pipeServer != null)
            {
                await Task.WhenAny(pipeServer.Completion, exitTask).ConfigureAwait(false);
                if (!exitTask.IsCompleted && tracker is { IsTerminal: false })
                {
                    ReportStage(
                        tracker,
                        UninstallRemovingFilesPercent,
                        nameof(CommonStrings.Uninstall_Progress_RemovingFiles));
                }
            }

            int exitCode = await exitTask.ConfigureAwait(false);
            if (exitCode != 0)
            {
                if (tracker is { IsTerminal: false })
                {
                    tracker.Report(TrayAppDotNETInstallProgress.Failed(string.Format(
                        CultureInfo.CurrentCulture,
                        L(nameof(CommonStrings.Uninstall_Progress_Failed_Format)),
                        exitCode)));
                }

                return false;
            }

            // The helper's own exe and the settings folder it logged into go once it has exited
            List<string> remaining = await WaitForRemovalAsync(removalTargets).ConfigureAwait(false);
            if (remaining.Count == 0)
            {
                if (tracker is { IsTerminal: false })
                {
                    ReportStage(
                        tracker,
                        TrayAppDotNETInstallProgress.CompletePercent,
                        nameof(CommonStrings.Uninstall_Progress_Complete));
                }

                return true;
            }

            Identity.WriteLog($"TrayAppDotNETInstallationService.MonitorUninstall: still present: {string.Join(", ", remaining)}");
            if (tracker is { IsTerminal: false })
            {
                tracker.Report(TrayAppDotNETInstallProgress.Failed(string.Format(
                    CultureInfo.CurrentCulture,
                    L(nameof(CommonStrings.Uninstall_Progress_ItemsRemain_Format)),
                    string.Join(", ", remaining))));
            }

            return false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            Identity.WriteLog($"TrayAppDotNETInstallationService.MonitorUninstall: {exception}");
            if (tracker is { IsTerminal: false }) tracker.Report(TrayAppDotNETInstallProgress.Failed(exception.Message));
            return false;
        }
        finally
        {
            observer.Dispose();
            pipeServer?.Dispose();
        }
    }

    private static async Task<List<string>> WaitForRemovalAsync(IReadOnlyList<DetachedCleanupTarget> targets)
    {
        long deadline = Environment.TickCount64 + TimeConstants.UninstallCleanupWaitMs;
        while (true)
        {
            List<string> remaining =
            [
                .. targets
                    .Where(target => target.Action != DetachedCleanupAction.RemoveDirectoryIfEmpty)
                    .Where(target => File.Exists(target.Path) || Directory.Exists(target.Path))
                    .Select(target => target.Path)
            ];
            if (remaining.Count == 0 || Environment.TickCount64 >= deadline) return remaining;

            await Task.Delay(TimeConstants.UninstallProcessRetryDelayMs).ConfigureAwait(false);
        }
    }

    /// <summary>What the detached removal deletes once the uninstall helper has exited.</summary>
    internal IReadOnlyList<DetachedCleanupTarget> RemovalTargets(InstallScope scope, bool deleteSettings)
    {
        bool allUsers = scope == InstallScope.ProgramFiles;
        string installDirectory = allUsers ? Layout.ProgramFilesInstallDirectory : Layout.LocalAppDataInstallDirectory;
        string installedExecutable = allUsers ? Layout.ProgramFilesInstallExecutable : Layout.LocalAppDataInstallExecutable;
        List<DetachedCleanupTarget> targets =
        [
            new(DetachedCleanupAction.DeleteFile, installedExecutable),
            new(DetachedCleanupAction.DeleteFile, TrayAppDotNETInstallLayout.PendingRemovalExecutablePath(installedExecutable))
        ];
        if (deleteSettings && !string.IsNullOrWhiteSpace(Identity.SettingsDirectory))
            targets.Add(new DetachedCleanupTarget(DetachedCleanupAction.DeleteDirectoryTree, Identity.SettingsDirectory));
        targets.Add(new DetachedCleanupTarget(DetachedCleanupAction.RemoveDirectoryIfEmpty, installDirectory));
        return targets;
    }

    /// <summary>
    /// The uninstall helper stage: reconciles shell state, stops this app's instances, removes the payload (the
    /// shared files only when this is the last app) and starts the detached removal of what this process still
    /// holds: its own exe and the settings folder its log lives in.
    /// </summary>
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
            Startup.ReconcileForUninstall(scope);

            ReportStage(progress, UninstallStartMenuPercent, nameof(CommonStrings.Uninstall_Progress_StartMenu));
            options.SyncStartMenu?.Invoke(scope, scope == InstallScope.ProgramFiles);

            ReportStage(progress, UninstallDesktopShortcutPercent, nameof(CommonStrings.Uninstall_Progress_DesktopShortcut));
            TrayAppDotNETInstallResult desktopResult = DesktopShortcut.SetEnabled(scope, enabled: false);
            if (!desktopResult.Success)
                Identity.WriteLog($"PrepareUninstall: {desktopResult.ErrorMessage}");

            ReportStage(progress, UninstallRegistryPercent, nameof(CommonStrings.Uninstall_Progress_Registry));
            bool registryRemoved = WindowsUninstallRegistry.Remove(scope, Identity);
            Startup.RemoveLegacyRunKey();

            ReportStage(progress, UninstallStoppingInstancesPercent, nameof(CommonStrings.Uninstall_Progress_StoppingInstances));
            StopInstalledProcesses(scope);

            if (deleteSettings)
            {
                ReportStage(progress, UninstallDeletingSettingsPercent, nameof(CommonStrings.Uninstall_Progress_DeletingSettings));
                DeleteSettingsDirectory();
            }

            ReportStage(progress, UninstallRemovingFilesPercent, nameof(CommonStrings.Uninstall_Progress_RemovingFiles));
            TrayAppDotNETInstallResult sharedResult = RemoveSharedFilesIfLast(scope);
            List<string> remainingFiles = RemoveAppFiles(scope);
            _ = options.ScheduleRemovalAfterExit(RemovalTargets(scope, deleteSettings), Identity.WriteLog);

            if (!desktopResult.Success) return Fail(progress, desktopResult.ErrorMessage);
            if (!registryRemoved) return Fail(progress, "The uninstall entry could not be removed.");
            if (!sharedResult.Success) return Fail(progress, sharedResult.ErrorMessage);
            if (remainingFiles.Count > 0)
            {
                return Fail(progress, string.Format(
                    CultureInfo.CurrentCulture,
                    L(nameof(CommonStrings.Uninstall_Progress_ItemsRemain_Format)),
                    string.Join(", ", remainingFiles)));
            }

            return new TrayAppDotNETInstallResult(true);
        }
        catch (Exception exception)
        {
            Identity.WriteLog($"TrayAppDotNETInstallationService.PrepareUninstall({scope}): {exception}");
            return Fail(progress, exception.Message);
        }
    }

    /// <summary>
    /// Deletes this app's own payload (everything not shared with the other apps) and anything an interrupted
    /// update of the app left. Returns the paths that could not be deleted.
    /// </summary>
    private List<string> RemoveAppFiles(InstallScope scope)
    {
        string installDirectory = scope == InstallScope.ProgramFiles
            ? Layout.ProgramFilesInstallDirectory
            : Layout.LocalAppDataInstallDirectory;
        List<string> remaining = [];
        if (!Directory.Exists(installDirectory)) return remaining;

        foreach (TrayAppDotNETInstallFile file in Payload.InstalledFiles(Layout.InstalledExecutableFileName))
        {
            if (file.RemoveOnlyWhenInstallRootHasNoExe
                || string.Equals(file.Name, Layout.InstalledExecutableFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            string path = Path.Combine(installDirectory, file.Name);
            TryDeletePath(path, directory: false);
            if (File.Exists(path)) remaining.Add(path);
        }

        foreach (TrayAppDotNETInstallDirectory directory in Payload.InstalledDirectories)
        {
            if (directory.RemoveOnlyWhenInstallRootHasNoExe) continue;

            string path = Path.Combine(installDirectory, directory.Name);
            TryDeletePath(path, directory: true);
            if (Directory.Exists(path)) remaining.Add(path);
        }

        UpdateFileTransaction.DeleteArtifacts(installDirectory, Layout.ApplicationName, Identity.WriteLog);
        return remaining;
    }

    private void TryDeletePath(string path, bool directory)
    {
        try
        {
            if (directory)
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Identity.WriteLog($"PrepareUninstall: could not delete {path}: {exception.Message}");
        }
    }

    /// <summary>
    /// Removes the files every app shares when this app is the last one in the install folder. The check and
    /// the deletes run under the install directory lock, so an install cannot land its exe in between. This
    /// app's exe is first renamed out of the "*.exe" pattern: a running image can be renamed but not deleted,
    /// and another app uninstalling at the same time then only counts exes that are staying. The detached
    /// removal deletes the renamed exe once its image is unmapped.
    /// </summary>
    private TrayAppDotNETInstallResult RemoveSharedFilesIfLast(InstallScope scope)
    {
        bool allUsers = scope == InstallScope.ProgramFiles;
        string installDirectory = allUsers ? Layout.ProgramFilesInstallDirectory : Layout.LocalAppDataInstallDirectory;
        string installedExecutable = allUsers ? Layout.ProgramFilesInstallExecutable : Layout.LocalAppDataInstallExecutable;
        if (!Directory.Exists(installDirectory)) return new TrayAppDotNETInstallResult(true);

        using TrayAppDotNETInstallDirectoryLock? directoryLock = TrayAppDotNETInstallDirectoryLock.Acquire(
            installDirectory,
            TimeSpan.FromMilliseconds(TimeConstants.InstallDirectoryLockTimeoutMs),
            waiting: null,
            Identity.WriteLog);
        // Keeping the shared files is the safe outcome whenever the folder cannot be examined under the lock
        if (directoryLock == null)
            return new TrayAppDotNETInstallResult(Success: false, $"Shared files were kept: {installDirectory} stayed busy");

        string pendingExecutable = TrayAppDotNETInstallLayout.PendingRemovalExecutablePath(installedExecutable);
        try
        {
            if (File.Exists(installedExecutable)) File.Move(installedExecutable, pendingExecutable, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Identity.WriteLog($"PrepareUninstall: could not rename {installedExecutable}; shared files were kept: {exception.Message}");
            return new TrayAppDotNETInstallResult(Success: false, exception.Message);
        }

        foreach (string executable in Directory.EnumerateFiles(installDirectory, searchPattern: "*.exe"))
        {
            // Only another TrayAppDotNET app needs the shared files; a stray exe in the folder does not
            if (!TrayAppDotNETInstalledProcesses.IsAppExecutableName(Path.GetFileName(executable))) continue;

            Identity.WriteLog($"PrepareUninstall: {Path.GetFileName(executable)} still uses the shared files; keeping them");
            return new TrayAppDotNETInstallResult(true);
        }

        List<string> remaining = [];
        for (int attempt = 1; attempt <= SharedFileDeleteAttempts; attempt++)
        {
            remaining = TryDeleteSharedFiles(installDirectory, pendingExecutable);
            if (remaining.Count == 0) return new TrayAppDotNETInstallResult(true);

            // Processes stopped a moment ago can still have the native libraries mapped
            if (attempt < SharedFileDeleteAttempts) Thread.Sleep(TimeConstants.UninstallFileRetryDelayMs);
        }

        string message = $"Could not remove shared files: {string.Join(separator: ", ", remaining)}";
        Identity.WriteLog($"PrepareUninstall: {message}");
        return new TrayAppDotNETInstallResult(Success: false, message);
    }

    /// <summary>Deletes the shared payload once, returning the paths that still exist.</summary>
    private List<string> TryDeleteSharedFiles(string installDirectory, string pendingExecutable)
    {
        List<string> files = [];
        foreach (TrayAppDotNETInstallFile file in Payload.InstalledFiles(Layout.InstalledExecutableFileName))
        {
            if (file.RemoveOnlyWhenInstallRootHasNoExe) files.Add(Path.Combine(installDirectory, file.Name));
        }

        if (Payload.CopySourceDirectoryRootFiles)
        {
            foreach (string rootFile in Directory.EnumerateFiles(installDirectory))
            {
                if (!string.Equals(rootFile, pendingExecutable, StringComparison.OrdinalIgnoreCase)) files.Add(rootFile);
            }
        }

        List<string> remaining = [];
        foreach (string file in files)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Identity.WriteLog($"PrepareUninstall: could not delete {file}: {exception.Message}");
            }

            if (File.Exists(file)) remaining.Add(file);
        }

        foreach (TrayAppDotNETInstallDirectory directory in Payload.InstalledDirectories)
        {
            if (!directory.RemoveOnlyWhenInstallRootHasNoExe) continue;

            string path = Path.Combine(installDirectory, directory.Name);
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Identity.WriteLog($"PrepareUninstall: could not delete {path}: {exception.Message}");
            }

            if (Directory.Exists(path)) remaining.Add(path);
        }

        return remaining;
    }

    /// <summary>Removes the settings directory; the detached removal repeats it once this process has exited.</summary>
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
                $"PrepareUninstall: settings removal deferred until this helper exits: {exception.Message}");
        }
    }

    private void StopInstalledProcesses(InstallScope scope)
    {
        string targetExecutable = scope == InstallScope.ProgramFiles
            ? Layout.ProgramFilesInstallExecutable
            : Layout.LocalAppDataInstallExecutable;
        TrayAppDotNETInstalledProcesses.Stop(
            TrayAppDotNETInstalledProcesses.ExecutablePaths(targetExecutable, includeSiblings: false),
            UninstallProcessStopAttempts,
            TimeConstants.UninstallProcessRetryDelayMs,
            Identity.WriteLog);
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
        catch (Win32Exception ex) when (TrayAppDotNETElevatedLaunch.IsUserCancelled(ex))
        {
            return new TrayAppDotNETInstallResult(Success: false, UserCancelled: true);
        }
        catch (Exception ex)
        {
            Identity.WriteLog($"TrayAppDotNETInstallationService.TryInvokeElevated: {ex}");
            return new TrayAppDotNETInstallResult(Success: false, ex.Message);
        }
    }

    /// <summary>
    /// Applies the install choices. An unset desktop choice leaves the desktop shortcut as it is. An unset Start
    /// Menu choice keeps the entries in step with the installation, except that a scope that was already installed
    /// and had its entries removed by the user does not get them back. A system install of an app that offers Always run
    /// as administrator applies a set choice for the installing account, and always turns on a required one; anything
    /// else leaves it.
    /// </summary>
    internal TrayAppDotNETInstallResult ApplyInstallOptions(
        InstallScope scope,
        bool allUsers,
        TrayAppDotNETInstallOptions? installOptions,
        bool scopeWasInstalled)
    {
        Identity.WriteLog(
            $"TrayAppDotNETInstallationService.ApplyInstallOptions: scope={scope}, "
            + $"desktopShortcut={FormatChoice(installOptions?.CreateDesktopShortcut)}, "
            + $"startMenuShortcut={FormatChoice(installOptions?.CreateStartMenuShortcut)}, "
            + $"runAsAdministrator={FormatChoice(installOptions?.RunAsAdministrator)}");

        bool startMenuWanted = installOptions?.CreateStartMenuShortcut
                               ?? (!scopeWasInstalled || TrayAppDotNETStartMenuShortcut.HasAnyShortcut(Identity.ApplicationName));
        options.SyncStartMenu?.Invoke(startMenuWanted ? null : scope, allUsers);

        List<string> failures = [];
        if (installOptions?.CreateDesktopShortcut is { } createDesktopShortcut)
        {
            TrayAppDotNETInstallResult desktopResult = DesktopShortcut.SetEnabled(scope, createDesktopShortcut);
            if (!desktopResult.Success)
                failures.Add("the desktop shortcut could not be updated: " + desktopResult.ErrorMessage);
        }

        if (scope == InstallScope.ProgramFiles
            && options.RunAsAdministrator is { } runAsAdministratorOption
            && runAsAdministratorOption.EffectiveChoice(installOptions?.RunAsAdministrator) is { } runAsAdministrator
            && !runAsAdministratorOption.SetEnabled(runAsAdministrator))
        {
            failures.Add(runAsAdministrator
                ? "Always run as administrator could not be turned on"
                : "Always run as administrator could not be turned off");
        }

        if (failures.Count == 0) return new TrayAppDotNETInstallResult(true);

        return new TrayAppDotNETInstallResult(
            Success: false,
            "Application files were installed, but " + string.Join("; ", failures) + ".");
    }

    private static string FormatChoice(bool? value) => value switch
    {
        true => "true",
        false => "false",
        null => "unchanged"
    };

    internal static string BuildElevatedInstallArguments(
        string sourceExecutable,
        int buildNumber,
        TrayAppDotNETInstallOptions? installOptions,
        string? progressPipeName = null)
    {
        string arguments =
            $"{TrayAppDotNETInstallContract.SystemInstallArgument} "
            + $"{TrayAppDotNETInstallContract.SourceExecutableArgument} \"{sourceExecutable}\" "
            + $"{TrayAppDotNETInstallContract.BuildNumberArgument} {buildNumber}";
        if (installOptions?.CreateDesktopShortcut is { } createDesktopShortcut)
        {
            arguments += $" {TrayAppDotNETInstallContract.DesktopShortcutArgument} "
                         + FormatBooleanArgument(createDesktopShortcut);
        }

        if (installOptions?.CreateStartMenuShortcut is { } createStartMenuShortcut)
        {
            arguments += $" {TrayAppDotNETInstallContract.StartMenuShortcutArgument} "
                         + FormatBooleanArgument(createStartMenuShortcut);
        }

        if (installOptions?.RunAsAdministrator is { } runAsAdministrator)
        {
            arguments += $" {TrayAppDotNETInstallContract.RunAsAdministratorArgument} "
                         + FormatBooleanArgument(runAsAdministrator);
        }

        if (!string.IsNullOrWhiteSpace(progressPipeName))
            arguments += $" {TrayAppDotNETInstallContract.ProgressPipeArgument} {progressPipeName}";

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

        // The app exe itself is added last by the copy plan
        if (string.Equals(fileName, installedExecutableFileName, StringComparison.OrdinalIgnoreCase)) return false;

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

    private static bool SamePath(string first, string second) =>
        string.Equals(
            PathNormalization.Normalize(first),
            PathNormalization.Normalize(second),
            StringComparison.OrdinalIgnoreCase);

    private static void CopyFileIfDifferent(string sourceFile, string destinationFile)
    {
        if (SamePath(sourceFile, destinationFile)) return;

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
    private readonly record struct InstallCopyItem(
        string Source,
        string Destination,
        bool IsDirectory,
        bool IsSharedNativeDll = false);
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
