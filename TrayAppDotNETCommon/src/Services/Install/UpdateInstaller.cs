using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Ipc;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.Services.Install;

/// <summary>How handing a verified package to the update worker ended.</summary>
internal enum UpdateLaunchResult
{
    HandedOff,
    Failed,
    AnotherUpdateRunning,
    AdministratorAccountRequired
}

/// <summary>
/// Runs the helper processes that replace and restart an installed app.
/// <para>
/// The worker is the installed exe itself (<c>--update-apply</c>), started elevated when the install folder needs it.
/// It never runs code from the user's download folder: it copies the downloaded package into a staging folder inside
/// the install folder, which only those allowed to change the install can write, checks the copy's SHA-256 against
/// the published one, extracts it there and moves the app's payload files into place as a journaled transaction.
/// The restarter (<c>--update-restart</c>) runs unelevated from the extracted download, waits for the worker and
/// starts the apps it stopped again.
/// </para>
/// </summary>
internal static class UpdateInstaller
{
    internal const string ApplyArgument = "--update-apply";
    internal const string RestartArgument = "--update-restart";

    private const int SuccessExitCode = 0;
    private const int RolledBackExitCode = 20;
    private const int UnsafeFailureExitCode = 21;
    private const int CancelledExitCode = 22;
    private const int ValidationFailureExitCode = 23;
    private const int AnotherUpdateRunningExitCode = 24;
    private const int InstallFolderBusyExitCode = 25;
    private const int ProcessStopAttempts = 20;
    private const int TargetLockAttempts = 40;
    private const int LogWriteAttempts = 20;
    private const byte ReadyMessage = 1;
    private const byte CommitMessage = 2;
    private const byte CancelMessage = 3;
    private const string PackageFileName = "package.zip";
    private const string PayloadDirectoryName = "payload";
    private const string RestartListFileName = ".update-restart-list";

    /// <summary>Returns true when the current process is an internal update helper.</summary>
    public static bool IsUpdateMode(string[] args) =>
        HasArgument(args, ApplyArgument) || HasArgument(args, RestartArgument);

    /// <summary>Dispatches an internal update helper before normal app startup.</summary>
    public static bool TryRun(
        string[] args,
        TrayAppDotNETProgramOptions options,
        out int exitCode)
    {
        if (HasArgument(args, ApplyArgument))
        {
            exitCode = RunApplyAsync(args, options).GetAwaiter().GetResult();
            return true;
        }

        if (HasArgument(args, RestartArgument))
        {
            exitCode = RunRestart(args, options);
            return true;
        }

        exitCode = 0;
        return false;
    }

    /// <summary>
    /// Starts and validates the worker, then starts the unelevated restarter before committing. The worker is
    /// <paramref name="targetExecutable"/>; <paramref name="stagedExecutable"/>, the same exe extracted from the
    /// download, only runs the unelevated restarter.
    /// </summary>
    public static async Task<UpdateLaunchResult> LaunchAsync(
        string stagedExecutable,
        string targetExecutable,
        string downloadedArchive,
        string archiveSha256,
        int buildNumber,
        string installerLogPath,
        Action<string> log,
        CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadedArchive);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(installerLogPath);
        ArgumentNullException.ThrowIfNull(log);

        string stagedPath = Path.GetFullPath(stagedExecutable);
        string targetPath = Path.GetFullPath(targetExecutable);
        if (!File.Exists(stagedPath))
            throw new FileNotFoundException(message: "Staged update executable not found.", stagedPath);
        if (!File.Exists(targetPath))
            throw new FileNotFoundException(message: "Installed executable not found.", targetPath);
        if (IsWindowsAppsPath(targetPath))
        {
            log("Update: Windows Store installations must be updated through the Store.");
            return UpdateLaunchResult.Failed;
        }

        bool elevate = NeedsElevation(targetPath, log);
        if (elevate && TrayAppDotNETElevationPolicy.ElevationWouldUseAnotherAccount(log))
        {
            log("Update: elevation would run under another account; the update needs an administrator account.");
            return UpdateLaunchResult.AdministratorAccountRequired;
        }

        // Created here, unelevated, so the worker appends to an ordinary file of the user's
        File.WriteAllText(installerLogPath, string.Empty);

        string pipeName = TrayAppDotNETPipeSecurity.CreatePipeName("TrayAppDotNET.Update");
        await using NamedPipeServerStream pipe = TrayAppDotNETPipeSecurity.CreateServer(pipeName, PipeDirection.InOut);

        using Process currentProcess = Process.GetCurrentProcess();
        long parentStartTimeTicks = currentProcess.StartTime.ToUniversalTime().Ticks;
        ProcessStartInfo workerStartInfo = BuildWorkerStartInfo(
            targetPath,
            downloadedArchive,
            archiveSha256,
            buildNumber,
            installerLogPath,
            pipeName,
            Environment.ProcessId,
            parentStartTimeTicks,
            elevate);

        Process? worker = null;
        bool committed = false;
        try
        {
            try
            {
                worker = Process.Start(workerStartInfo);
            }
            catch (Win32Exception exception) when (TrayAppDotNETElevatedLaunch.IsUserCancelled(exception))
            {
                log("Update: UAC prompt was declined; the running app was left untouched.");
                return UpdateLaunchResult.Failed;
            }

            if (worker == null)
            {
                log("Update: Process.Start returned null for the update worker.");
                return UpdateLaunchResult.Failed;
            }

            long workerStartTimeTicks = worker.StartTime.ToUniversalTime().Ticks;
            log(
                $"Update: started {(elevate ? "elevated " : string.Empty)}worker PID {worker.Id}; "
                + $"log: {installerLogPath}");

            Task connectTask = pipe.WaitForConnectionAsync(token);
            Task workerExitTask = worker.WaitForExitAsync(CancellationToken.None);
            Task firstTask = await Task.WhenAny(connectTask, workerExitTask).ConfigureAwait(false);
            if (ReferenceEquals(firstTask, workerExitTask))
            {
                await workerExitTask.ConfigureAwait(false);
                log($"Update: worker exited before becoming ready with code {worker.ExitCode}.");
                return worker.ExitCode == AnotherUpdateRunningExitCode
                    ? UpdateLaunchResult.AnotherUpdateRunning
                    : UpdateLaunchResult.Failed;
            }

            await connectTask.ConfigureAwait(false);
            if (!TrayAppDotNETPipeSecurity.IsClient(pipe, worker.Id))
            {
                log($"Update: the update pipe was opened by another process than worker PID {worker.Id}.");
                return UpdateLaunchResult.Failed;
            }

            byte message = await ReadMessageAsync(pipe, token).ConfigureAwait(false);
            if (message != ReadyMessage)
            {
                log("Update: worker disconnected before confirming readiness.");
                return UpdateLaunchResult.Failed;
            }

            bool stopSiblingApps = await ReadMessageAsync(pipe, token).ConfigureAwait(false) != 0;
            string stagedDirectory = Path.GetDirectoryName(stagedPath)
                                     ?? throw new InvalidOperationException("Cannot resolve the staged directory.");
            List<string> restartExecutables = CollectRestartExecutables(targetPath, stopSiblingApps);
            await File.WriteAllLinesAsync(
                    Path.Combine(stagedDirectory, RestartListFileName),
                    restartExecutables,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    token)
                .ConfigureAwait(false);

            ProcessStartInfo restarterStartInfo = BuildRestarterStartInfo(
                stagedPath,
                targetPath,
                downloadedArchive,
                installerLogPath,
                worker.Id,
                workerStartTimeTicks);
            using Process? restarter = Process.Start(restarterStartInfo);
            if (restarter == null)
            {
                await WriteMessageAsync(pipe, CancelMessage, CancellationToken.None).ConfigureAwait(false);
                log("Update: Process.Start returned null for the update restarter.");
                return UpdateLaunchResult.Failed;
            }

            await WriteMessageAsync(pipe, CommitMessage, token).ConfigureAwait(false);
            committed = true;
            log($"Update: restarter PID {restarter.Id} is ready; shutting down for replacement.");
            return UpdateLaunchResult.HandedOff;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            log("Update: handoff was cancelled; the running app was left untouched.");
            return UpdateLaunchResult.Failed;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
                                              or UnauthorizedAccessException or Win32Exception)
        {
            log($"Update: failed to hand off to the worker: {exception}");
            return UpdateLaunchResult.Failed;
        }
        finally
        {
            if (!committed && pipe.IsConnected)
            {
                try
                {
                    await WriteMessageAsync(pipe, CancelMessage, CancellationToken.None).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // The worker already went away
                }
            }

            worker?.Dispose();
        }
    }

    private static async Task<int> RunApplyAsync(string[] args, TrayAppDotNETProgramOptions options)
    {
        string logPath = RequiredArgument(args, name: "--log");
        // An older parent starts the worker without --archive and --sha256 and does not create the log first
        bool legacyInvocation = IsLegacyApplyInvocation(args);
        Action<string> log = message => AppendLog(logPath, $"worker: {message}", createIfMissing: legacyInvocation);
        bool commitReceived = false;
        string? stagingDirectory = null;
        string? targetDirectory = null;
        string? lockPath = null;
        List<DetachedCleanupTarget> afterExit = [];

        try
        {
            int parentPID = RequiredIntArgument(args, name: "--parent-pid");
            long parentStartTimeTicks = RequiredLongArgument(args, name: "--parent-start");
            int buildNumber = legacyInvocation ? 0 : RequiredIntArgument(args, name: "--build");
            string pipeName = RequiredArgument(args, name: "--pipe");
            string? archivePath = legacyInvocation ? null : Path.GetFullPath(RequiredArgument(args, name: "--archive"));
            string expectedSha256 = legacyInvocation
                ? string.Empty
                : UpdateCheckService.NormalizeSha256(RequiredArgument(args, name: "--sha256"));
            string targetExecutable = Path.GetFullPath(RequiredArgument(args, name: "--target"));
            targetDirectory = Path.GetDirectoryName(targetExecutable)
                              ?? throw new InvalidOperationException("Cannot resolve install directory.");
            if (!legacyInvocation && expectedSha256.Length == 0)
                throw new ArgumentException("The update has no valid SHA-256.");

            // The worker is the installed exe; it never runs from the download folder. Only the transitional
            // invocation of an older parent runs this exe from the folder that parent extracted: that parent has
            // already chosen to run it, elevated if needed, so refusing would protect nothing and would strand the
            // install on the old build
            string workerExecutable = Environment.ProcessPath
                                      ?? throw new InvalidOperationException("Cannot resolve the worker path.");
            if (!legacyInvocation
                && !string.Equals(Path.GetFullPath(workerExecutable), targetExecutable, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The update worker must run from the installed executable.");

            TrayAppDotNETInstallationService installation = options.Installation();
            string applicationName = installation.Layout.ApplicationName;
            if (legacyInvocation)
            {
                buildNumber = installation.CurrentBuildNumber;
                log($"Transitional update from an older build to build {buildNumber}.");
            }

            using Process parent = RequireProcess(parentPID, parentStartTimeTicks, description: "update parent");
            string? parentExecutable = TryGetProcessExecutable(parent);
            if (parentExecutable != null
                && !string.Equals(
                    Path.GetFullPath(parentExecutable),
                    targetExecutable,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The update parent does not match the target executable.");

            lockPath = UpdateFileTransaction.LockPath(targetDirectory, applicationName);
            await using FileStream? targetLock = AcquireTargetLock(lockPath, log);
            if (targetLock == null)
            {
                lockPath = null;
                log("Another update of this app is being installed; this one was not started.");
                return AnotherUpdateRunningExitCode;
            }

            if (!UpdateFileTransaction.RecoverInterrupted(targetDirectory, applicationName, log))
                throw new InvalidOperationException("An earlier interrupted update could not be rolled back.");

            stagingDirectory = UpdateFileTransaction.NewStagingDirectory(targetDirectory, applicationName);
            Directory.CreateDirectory(stagingDirectory);
            string payloadDirectory = PrepareStagedPayload(
                legacyInvocation,
                Path.GetDirectoryName(workerExecutable)!,
                archivePath,
                expectedSha256,
                stagingDirectory);

            UpdateFilePlan plan = UpdateFileTransaction.BuildPlan(
                payloadDirectory,
                targetDirectory,
                targetExecutable,
                installation.Payload,
                stagingDirectory,
                log);
            await using NamedPipeClientStream pipe = TrayAppDotNETPipeSecurity.CreateClient(pipeName, PipeDirection.InOut);

            Task connectTask = pipe.ConnectAsync();
            Task parentExitTask = parent.WaitForExitAsync();
            Task firstTask = await Task.WhenAny(connectTask, parentExitTask).ConfigureAwait(false);
            if (ReferenceEquals(firstTask, parentExitTask))
            {
                log("Parent exited before the update handoff completed.");
                return CancelledExitCode;
            }

            await connectTask.ConfigureAwait(false);
            // The server must be the update parent in every case; only its owner rule depends on the handshake
            if (!IsAcceptedServerOwner(pipe, legacyInvocation) || !TrayAppDotNETPipeSecurity.IsServer(pipe, parentPID))
                throw new UnauthorizedAccessException("The update pipe is not served by the update parent.");

            await WriteMessageAsync(pipe, ReadyMessage, CancellationToken.None).ConfigureAwait(false);
            // An old parent reads the ready byte only; the sibling flag is part of the current handshake
            if (!legacyInvocation)
            {
                await WriteMessageAsync(pipe, plan.StopSiblingApps ? (byte)1 : (byte)0, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            log($"Verified the package and planned {plan.Files.Count} files under the update lock.");

            byte command = await ReadMessageAsync(pipe, CancellationToken.None).ConfigureAwait(false);
            if (command != CommitMessage)
            {
                log("Parent cancelled the update before shutdown.");
                return CancelledExitCode;
            }

            commitReceived = true;
            if (legacyInvocation)
            {
                // The old parent leaves the restart list to the worker, beside the restarter it started
                WriteLegacyRestartList(
                    Path.GetDirectoryName(workerExecutable)!,
                    CollectRestartExecutables(targetExecutable, plan.StopSiblingApps),
                    log);
            }

            log("Commit received; waiting for the parent to exit.");
            await parentExitTask.ConfigureAwait(false);
            TrayAppDotNETInstalledProcesses.Stop(
                TrayAppDotNETInstalledProcesses.ExecutablePaths(targetExecutable, plan.StopSiblingApps),
                ProcessStopAttempts,
                TimeConstants.UpdateProcessRetryDelayMs,
                log);

            // Other apps' installs and updates write the same shared files into this folder. Mutex ownership
            // is per thread, so the lock covers only this synchronous stretch after the last await
            using TrayAppDotNETInstallDirectoryLock? directoryLock = TrayAppDotNETInstallDirectoryLock.Acquire(
                targetDirectory,
                TimeSpan.FromMilliseconds(TimeConstants.InstallDirectoryLockTimeoutMs),
                waiting: null,
                log);
            if (directoryLock == null)
            {
                // Nothing was replaced yet, so the restarter brings the previous version back up
                log("Another install or update kept the install folder busy; the update was not applied.");
                return InstallFolderBusyExitCode;
            }

            UpdateFileTransactionResult result = UpdateFileTransaction.Apply(plan, log);
            foreach (string leftover in result.LeftoverPaths ?? [])
                afterExit.Add(new DetachedCleanupTarget(DetachedCleanupAction.DeleteFile, leftover));
            switch (result.Status)
            {
                case UpdateFileTransactionStatus.Succeeded:
                    RefreshUninstallEntry(installation, targetExecutable, buildNumber, log);
                    log("Update completed successfully.");
                    return SuccessExitCode;
                case UpdateFileTransactionStatus.FailedRolledBack:
                    log($"Update failed and was rolled back: {result.ErrorMessage}");
                    return RolledBackExitCode;
                case UpdateFileTransactionStatus.FailedRollbackIncomplete:
                    // The journal and the backups stay for the next update or install to finish the rollback
                    stagingDirectory = null;
                    log($"Update failed and rollback was incomplete: {result.ErrorMessage}");
                    return UnsafeFailureExitCode;
                default:
                    throw new UnreachableException();
            }
        }
        catch (Exception exception)
        {
            log($"Update worker failed: {exception}");
            return commitReceived ? RolledBackExitCode : ValidationFailureExitCode;
        }
        finally
        {
            if (stagingDirectory != null)
                afterExit.Add(new DetachedCleanupTarget(DetachedCleanupAction.DeleteDirectoryTree, stagingDirectory));
            if (lockPath != null)
                afterExit.Add(new DetachedCleanupTarget(DetachedCleanupAction.DeleteFile, lockPath));
            // Runs once this process, whose image may now be one of the backups, has exited
            DetachedCleanup.Schedule(afterExit, log);
            options.FlushLog?.Invoke();
        }
    }

    private static int RunRestart(string[] args, TrayAppDotNETProgramOptions options)
    {
        string logPath = RequiredArgument(args, name: "--log");
        Action<string> log = message => AppendLog(logPath, $"restarter: {message}");
        string sourceExecutable = Environment.ProcessPath
                                  ?? throw new InvalidOperationException("Cannot resolve restarter path.");
        string sourceDirectory = Path.GetDirectoryName(sourceExecutable)
                                 ?? throw new InvalidOperationException("Cannot resolve restarter directory.");
        string archivePath = Path.GetFullPath(RequiredArgument(args, name: "--archive"));
        string targetExecutable = Path.GetFullPath(RequiredArgument(args, name: "--target"));

        try
        {
            int workerPID = RequiredIntArgument(args, name: "--worker-pid");
            long workerStartTimeTicks = RequiredLongArgument(args, name: "--worker-start");
            using ProcessExitHandle worker = OpenWorker(workerPID, workerStartTimeTicks);
            log($"Waiting for worker PID {workerPID}.");
            int workerExitCode = worker.WaitForExit();
            log($"Worker exited with code {workerExitCode}.");

            List<string> restartExecutables = ReadRestartExecutables(
                Path.Combine(sourceDirectory, RestartListFileName),
                targetExecutable,
                log);

            switch (workerExitCode)
            {
                case SuccessExitCode:
                    StartApplications(restartExecutables, targetExecutable, log);
                    ScheduleCleanup(sourceDirectory, archivePath, log);
                    return SuccessExitCode;
                case RolledBackExitCode:
                    StartApplications(restartExecutables, targetExecutable, log);
                    ShowUpdateError(
                        options.ApplicationName,
                        Format(CommonStrings.UpdateError_RolledBack_Format, logPath));
                    ScheduleCleanup(sourceDirectory, archivePath, log);
                    return RolledBackExitCode;
                case InstallFolderBusyExitCode:
                    StartApplications(restartExecutables, targetExecutable, log);
                    ShowUpdateError(
                        options.ApplicationName,
                        Format(CommonStrings.UpdateError_FolderBusy_Format, logPath));
                    ScheduleCleanup(sourceDirectory, archivePath, log);
                    return InstallFolderBusyExitCode;
                case CancelledExitCode:
                    ScheduleCleanup(sourceDirectory, archivePath, log);
                    return CancelledExitCode;
                case UnsafeFailureExitCode:
                    ShowUpdateError(
                        options.ApplicationName,
                        Format(CommonStrings.UpdateError_Unsafe_Format, logPath));
                    ScheduleCleanup(sourceDirectory, archivePath, log);
                    return UnsafeFailureExitCode;
                default:
                    return RecoverAfterUnknownExit(
                        workerExitCode,
                        restartExecutables,
                        targetExecutable,
                        sourceDirectory,
                        archivePath,
                        logPath,
                        options,
                        log);
            }
        }
        catch (Exception exception)
        {
            log($"Restarter failed: {exception}");
            ShowUpdateError(
                options.ApplicationName,
                Format(CommonStrings.UpdateError_HelperFailed_Format, logPath));
            return ValidationFailureExitCode;
        }
        finally
        {
            options.FlushLog?.Invoke();
        }
    }

    /// <summary>
    /// A worker that ended with an unknown code (killed, crashed) may have stopped halfway through the replacement.
    /// The apps restart only once the journal proves the folder is whole again.
    /// </summary>
    private static int RecoverAfterUnknownExit(
        int workerExitCode,
        List<string> restartExecutables,
        string targetExecutable,
        string sourceDirectory,
        string archivePath,
        string logPath,
        TrayAppDotNETProgramOptions options,
        Action<string> log)
    {
        string targetDirectory = Path.GetDirectoryName(targetExecutable)
                                 ?? throw new InvalidOperationException("Cannot resolve install directory.");
        string applicationName = Path.GetFileNameWithoutExtension(targetExecutable);
        bool recovered;
        try
        {
            using FileStream? targetLock = AcquireTargetLock(UpdateFileTransaction.LockPath(targetDirectory, applicationName), log);
            recovered = targetLock != null
                        && UpdateFileTransaction.RecoverInterrupted(targetDirectory, applicationName, log);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidOperationException or InvalidDataException)
        {
            log($"Could not check the install folder after the worker ended: {exception.Message}");
            recovered = false;
        }

        ScheduleCleanup(sourceDirectory, archivePath, log);
        if (!recovered)
        {
            ShowUpdateError(
                options.ApplicationName,
                Format(CommonStrings.UpdateError_WorkerStoppedNotRestarted_Format, workerExitCode, logPath));
            return UnsafeFailureExitCode;
        }

        StartApplications(restartExecutables, targetExecutable, log);
        ShowUpdateError(
            options.ApplicationName,
            Format(CommonStrings.UpdateError_WorkerStoppedRestored_Format, workerExitCode, logPath));
        return RolledBackExitCode;
    }

    /// <summary>
    /// Copies the downloaded package into the protected staging folder while hashing the copy, so the bytes that
    /// are extracted are exactly the bytes that were verified.
    /// </summary>
    private static void CopyAndVerifyPackage(string sourcePath, string destinationPath, string expectedSha256)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (FileStream destination = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            byte[] buffer = new byte[81920];
            int read;
            while ((read = source.Read(buffer, offset: 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, offset: 0, read);
                destination.Write(buffer, offset: 0, read);
            }
        }

        string actualSha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The update package's SHA-256 is {actualSha256}, but the release publishes {expectedSha256}.");
        }
    }

    /// <summary>Records the installed build in the Apps &amp; Features entry of the scope that was updated.</summary>
    private static void RefreshUninstallEntry(
        TrayAppDotNETInstallationService installation,
        string targetExecutable,
        int buildNumber,
        Action<string> log)
    {
        InstallScope? scope = installation.ScopeOfExecutable(targetExecutable);
        if (scope == null) return;

        if (!WindowsUninstallRegistry.UpdateVersion(scope.Value, installation.Identity, buildNumber, targetExecutable))
            log($"Could not record build {buildNumber} in the uninstall entry.");
    }

    private static ProcessStartInfo BuildWorkerStartInfo(
        string targetExecutable,
        string archivePath,
        string archiveSha256,
        int buildNumber,
        string logPath,
        string pipeName,
        int parentPID,
        long parentStartTimeTicks,
        bool elevate)
    {
        ProcessStartInfo startInfo = BuildHelperStartInfo(targetExecutable, elevate);
        AddArgument(startInfo, ApplyArgument);
        AddArgument(startInfo, argument: "--parent-pid", parentPID.ToString(CultureInfo.InvariantCulture));
        AddArgument(
            startInfo,
            argument: "--parent-start",
            parentStartTimeTicks.ToString(CultureInfo.InvariantCulture));
        AddArgument(startInfo, argument: "--pipe", pipeName);
        AddArgument(startInfo, argument: "--target", targetExecutable);
        AddArgument(startInfo, argument: "--archive", archivePath);
        AddArgument(startInfo, argument: "--sha256", archiveSha256);
        AddArgument(startInfo, argument: "--build", buildNumber.ToString(CultureInfo.InvariantCulture));
        AddArgument(startInfo, argument: "--log", logPath);
        return startInfo;
    }

    private static ProcessStartInfo BuildRestarterStartInfo(
        string stagedExecutable,
        string targetExecutable,
        string archivePath,
        string logPath,
        int workerPID,
        long workerStartTimeTicks)
    {
        ProcessStartInfo startInfo = BuildHelperStartInfo(stagedExecutable, elevate: false);
        AddArgument(startInfo, RestartArgument);
        AddArgument(startInfo, argument: "--worker-pid", workerPID.ToString(CultureInfo.InvariantCulture));
        AddArgument(
            startInfo,
            argument: "--worker-start",
            workerStartTimeTicks.ToString(CultureInfo.InvariantCulture));
        AddArgument(startInfo, argument: "--target", targetExecutable);
        AddArgument(startInfo, argument: "--archive", archivePath);
        AddArgument(startInfo, argument: "--log", logPath);
        // The restarter applies the override to itself and passes it on when it restarts the target
        if (TrayAppDotNETProgram.SettingsDirectoryOverride is { } settingsDirectory)
            AddArgument(startInfo, TrayAppDotNETProgram.SettingsDirectoryArgument, settingsDirectory);
        return startInfo;
    }

    private static ProcessStartInfo BuildHelperStartInfo(string executable, bool elevate)
    {
        string? workingDirectory = Path.GetDirectoryName(executable);
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? "." : workingDirectory,
            UseShellExecute = elevate,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        if (elevate)
            startInfo.Verb = "runas";
        else
            startInfo.CreateNoWindow = true;
        return startInfo;
    }

    private static void AddArgument(ProcessStartInfo startInfo, string argument, string? value = null)
    {
        startInfo.ArgumentList.Add(argument);
        if (value != null)
            startInfo.ArgumentList.Add(value);
    }

    private static async Task<byte> ReadMessageAsync(Stream stream, CancellationToken token)
    {
        byte[] buffer = new byte[1];
        int bytesRead = await stream.ReadAsync(buffer.AsMemory(start: 0, length: 1), token).ConfigureAwait(false);
        return bytesRead == 1 ? buffer[0] : (byte)0;
    }

    private static async Task WriteMessageAsync(Stream stream, byte message, CancellationToken token)
    {
        byte[] buffer = [message];
        await stream.WriteAsync(buffer.AsMemory(start: 0, length: 1), token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes the app's own update lock, retrying briefly. Returns null while another worker of the same app holds
    /// it; other apps updating in the same folder use their own lock.
    /// </summary>
    private static FileStream? AcquireTargetLock(string path, Action<string> log)
    {
        for (int attempt = 1; attempt <= TargetLockAttempts; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when (attempt < TargetLockAttempts)
            {
                log($"Update lock is busy ({attempt}/{TargetLockAttempts}): {exception.Message}; retrying");
                Thread.Sleep(TimeConstants.UpdateFileRetryDelayMs);
            }
            catch (IOException exception)
            {
                log($"Update lock stayed busy: {exception.Message}");
                return null;
            }
        }

        return null;
    }

    private static List<string> CollectRestartExecutables(string targetExecutable, bool includeSiblings)
    {
        HashSet<string> restartExecutables = new(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(targetExecutable) };
        restartExecutables.UnionWith(TrayAppDotNETInstalledProcesses.RunningExecutables(
            TrayAppDotNETInstalledProcesses.ExecutablePaths(targetExecutable, includeSiblings)));
        return [.. restartExecutables.Order(StringComparer.OrdinalIgnoreCase)];
    }

    private static List<string> ReadRestartExecutables(
        string restartListPath,
        string targetExecutable,
        Action<string> log)
    {
        HashSet<string> executables = new(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(restartListPath))
        {
            try
            {
                foreach (string line in File.ReadAllLines(restartListPath))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        executables.Add(Path.GetFullPath(line.Trim()));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                log($"Could not read restart list: {exception.Message}");
            }
        }

        executables.Add(Path.GetFullPath(targetExecutable));
        return [.. executables.Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Restarts the stopped apps. Only <paramref name="targetExecutable"/> gets the settings folder override, since
    /// the sibling apps' command lines are not known.
    /// </summary>
    private static void StartApplications(
        IReadOnlyList<string> executables,
        string targetExecutable,
        Action<string> log)
    {
        string? settingsDirectory = TrayAppDotNETProgram.SettingsDirectoryOverride;
        _ = TrayAppDotNETInstalledProcesses.Start(
            executables,
            executable => settingsDirectory != null
                          && string.Equals(executable, targetExecutable, StringComparison.OrdinalIgnoreCase)
                ? [TrayAppDotNETProgram.SettingsDirectoryArgument, settingsDirectory]
                : [],
            log);
    }

    /// <summary>Removes the extracted download, which holds this restarter's own image, once it has exited.</summary>
    private static void ScheduleCleanup(string sourceDirectory, string archivePath, Action<string> log) =>
        DetachedCleanup.Schedule(
            [
                new DetachedCleanupTarget(DetachedCleanupAction.DeleteDirectoryTree, sourceDirectory),
                new DetachedCleanupTarget(DetachedCleanupAction.DeleteFile, archivePath)
            ],
            log);

    private static bool NeedsElevation(string targetExecutable, Action<string> log)
    {
        if (TrayAppDotNETInstallationService.IsElevated(log)) return false;

        string targetDirectory = Path.GetDirectoryName(targetExecutable)
                                 ?? throw new InvalidOperationException("Cannot resolve update target directory.");
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (IsPathWithin(targetDirectory, programFiles) || IsPathWithin(targetDirectory, programFilesX86))
            return true;

        string probePath = Path.Combine(targetDirectory, $".tadn-write-{Guid.NewGuid():N}.tmp");
        try
        {
            using FileStream probe = new(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            return false;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            log($"Update: target directory is not directly writable ({exception.Message}); requesting elevation.");
            return true;
        }
    }

    private static bool IsPathWithin(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return false;
        string normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowsAppsPath(string path)
    {
        string windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            path2: "WindowsApps");
        return IsPathWithin(path, windowsApps);
    }

    private static Process RequireProcess(int processID, long startTimeTicks, string description)
    {
        Process process = Process.GetProcessById(processID);
        try
        {
            long actualStartTimeTicks = process.StartTime.ToUniversalTime().Ticks;
            if (actualStartTimeTicks != startTimeTicks)
                throw new InvalidOperationException($"The {description} PID was reused.");
            return process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens the update worker and refuses a PID that now names a process started at another time. The worker is
    /// elevated whenever the install folder needs it, so this unelevated restarter waits on it through a
    /// <see cref="ProcessExitHandle"/>.
    /// </summary>
    internal static ProcessExitHandle OpenWorker(int processID, long startTimeTicks)
    {
        ProcessExitHandle worker = ProcessExitHandle.Open(processID);
        try
        {
            if (!worker.TryReadStartFileTime(out long startFileTime))
                throw new InvalidOperationException("The start time of the update worker cannot be read.");
            if (DateTime.FromFileTimeUtc(startFileTime).Ticks != startTimeTicks)
                throw new InvalidOperationException("The update worker PID was reused.");
            return worker;
        }
        catch
        {
            worker.Dispose();
            throw;
        }
    }

    private static string? TryGetProcessExecutable(Process process)
    {
        try
        {
            return ProcessImagePath.TryGet(process.Id);
        }
        catch (InvalidOperationException)
        {
            // A Process whose ID was never resolved
            return null;
        }
    }

    private static void AppendLog(string logPath, string message, bool createIfMissing = false)
    {
        string line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
        for (int attempt = 1; attempt <= LogWriteAttempts; attempt++)
        {
            try
            {
                // Appends only to the file the unelevated parent created, except for an older parent, which
                // leaves creating it to the worker
                using FileStream stream = new(
                    logPath,
                    createIfMissing ? FileMode.OpenOrCreate : FileMode.Open,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(0, SeekOrigin.End);
                using StreamWriter writer = new(stream, new UTF8Encoding(false));
                writer.Write(line);
                return;
            }
            catch (IOException) when (attempt < LogWriteAttempts)
            {
                Thread.Sleep(TimeConstants.UpdateLogRetryDelayMs);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    private static void ShowUpdateError(string applicationName, string message) =>
        _ = User32.MessageBox(
            IntPtr.Zero,
            message,
            Format(CommonStrings.UpdateError_Title_Format, applicationName),
            User32.MB_ICONERROR);

    private static string Format(string format, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, format, arguments);

    private static string RequiredArgument(string[] args, string name)
    {
        string? value = TrayAppDotNETProgram.TryGetArgValue(args, name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Missing required update argument: {name}")
            : value;
    }

    private static int RequiredIntArgument(string[] args, string name)
    {
        string value = RequiredArgument(args, name);
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new ArgumentException($"Invalid integer update argument: {name}");
    }

    private static long RequiredLongArgument(string[] args, string name)
    {
        string value = RequiredArgument(args, name);
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : throw new ArgumentException($"Invalid integer update argument: {name}");
    }

    private static bool HasArgument(string[] args, string name) =>
        args.Any(argument => argument.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True for the <c>--update-apply</c> command line of a build from before the verified-package handshake: no
    /// <c>--archive</c> and no <c>--sha256</c>. Such a parent runs the downloaded exe as the worker and cannot be
    /// updated any other way, so this one step applies the update the old way.
    /// </summary>
    internal static bool IsLegacyApplyInvocation(string[] args) =>
        HasArgument(args, ApplyArgument) && !HasArgument(args, "--archive") && !HasArgument(args, "--sha256");

    private static bool IsAcceptedServerOwner(NamedPipeClientStream pipe, bool legacyInvocation)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return IsAcceptedServerOwner(
            pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier,
            TrayAppDotNETPipeSecurity.CurrentUser,
            identity.Owner,
            legacyInvocation);
    }

    /// <summary>
    /// The owner rule for the update parent's pipe. The current handshake creates it with the user SID as explicit
    /// owner, which holds elevated or not. An older parent created it with <see cref="PipeOptions.CurrentUserOnly"/>,
    /// which leaves the token's default owner in place: <c>BUILTIN\Administrators</c> for an elevated administrator,
    /// the common case with UAC turned off. Only for that transitional handshake is the default owner of this
    /// process's token, the same account's, accepted too; the caller still requires the server to be the parent.
    /// </summary>
    internal static bool IsAcceptedServerOwner(
        SecurityIdentifier? owner,
        SecurityIdentifier currentUser,
        SecurityIdentifier? tokenDefaultOwner,
        bool legacyInvocation)
    {
        if (owner == null) return false;
        if (owner.Equals(currentUser)) return true;
        return legacyInvocation && tokenDefaultOwner != null && owner.Equals(tokenDefaultOwner);
    }

    /// <summary>Writes the restart list an older parent expects the worker to leave beside the restarter.</summary>
    private static void WriteLegacyRestartList(string sourceDirectory, List<string> executables, Action<string> log)
    {
        string path = Path.Combine(sourceDirectory, RestartListFileName);
        try
        {
            // CreateNew refuses a name planted in advance, link or file, instead of writing through it
            using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            foreach (string executable in executables) writer.WriteLine(executable);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The restarter still restarts the updated app itself; only siblings would stay stopped
            log($"Could not write the restart list {path}: {exception.Message}");
        }
    }

    /// <summary>
    /// Fills <c>payload\</c> inside <paramref name="stagingDirectory"/> and returns it. The current handshake copies
    /// the downloaded package in while hashing that copy, refuses a hash other than <paramref name="expectedSha256"/>,
    /// and extracts the verified copy. The transitional handshake of an older parent copies the folder that parent
    /// extracted and started this exe from, so the installed files still come from the protected staging folder.
    /// </summary>
    internal static string PrepareStagedPayload(
        bool legacyInvocation,
        string workerDirectory,
        string? archivePath,
        string expectedSha256,
        string stagingDirectory)
    {
        string payloadDirectory = Path.Combine(stagingDirectory, PayloadDirectoryName);
        if (legacyInvocation)
        {
            CopyDirectory(workerDirectory, payloadDirectory);
            return payloadDirectory;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        string packagePath = Path.Combine(stagingDirectory, PackageFileName);
        CopyAndVerifyPackage(archivePath, packagePath, expectedSha256);
        UpdateCheckService.ExtractZip(packagePath, payloadDirectory);
        return payloadDirectory;
    }

    /// <summary>Copies every file under <paramref name="source"/> into <paramref name="destination"/>.</summary>
    private static void CopyDirectory(string source, string destination)
    {
        string sourceRoot = Path.GetFullPath(source);
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(sourceRoot, directory)));
        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(sourceRoot, file)), overwrite: false);
    }
}
