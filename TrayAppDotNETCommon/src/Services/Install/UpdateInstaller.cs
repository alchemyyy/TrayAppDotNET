using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.Services.Install;

/// <summary>Runs the staged helper processes used to replace and restart an installed app.</summary>
internal static class UpdateInstaller
{
    internal const string ApplyArgument = "--update-apply";
    internal const string RestartArgument = "--update-restart";

    private const int SuccessExitCode = 0;
    private const int RolledBackExitCode = 20;
    private const int UnsafeFailureExitCode = 21;
    private const int CancelledExitCode = 22;
    private const int ValidationFailureExitCode = 23;
    private const int WindowsErrorCancelled = 1223;
    private const int ProcessStopAttempts = 20;
    private const int TargetLockAttempts = 40;
    private const int LogWriteAttempts = 20;
    private const byte ReadyMessage = 1;
    private const byte CommitMessage = 2;
    private const byte CancelMessage = 3;

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
    /// Starts and validates the elevated worker, then starts the medium-integrity restarter before committing.
    /// </summary>
    public static async Task<bool> LaunchAsync(
        string stagedExecutable,
        string targetExecutable,
        string downloadedArchive,
        string installerLogPath,
        Action<string> log,
        CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadedArchive);
        ArgumentException.ThrowIfNullOrWhiteSpace(installerLogPath);
        ArgumentNullException.ThrowIfNull(log);

        string stagedPath = Path.GetFullPath(stagedExecutable);
        string targetPath = Path.GetFullPath(targetExecutable);
        if (!File.Exists(stagedPath))
            throw new FileNotFoundException("Staged update executable not found.", stagedPath);
        if (!File.Exists(targetPath))
            throw new FileNotFoundException("Installed executable not found.", targetPath);
        if (IsWindowsAppsPath(targetPath))
        {
            log("Update: Windows Store installations must be updated through the Store.");
            return false;
        }

        string pipeName = $"TrayAppDotNET.Update.{Environment.ProcessId}.{Guid.NewGuid():N}";
        using NamedPipeServerStream pipe = new(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        using Process currentProcess = Process.GetCurrentProcess();
        long parentStartTimeTicks = currentProcess.StartTime.ToUniversalTime().Ticks;
        bool elevate = NeedsElevation(targetPath, log);
        ProcessStartInfo workerStartInfo = BuildWorkerStartInfo(
            stagedPath,
            targetPath,
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
            catch (Win32Exception exception) when (exception.NativeErrorCode == WindowsErrorCancelled)
            {
                log("Update: UAC prompt was declined; the running app was left untouched.");
                return false;
            }

            if (worker == null)
            {
                log("Update: Process.Start returned null for the update worker.");
                return false;
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
                return false;
            }

            await connectTask.ConfigureAwait(false);
            byte message = await ReadMessageAsync(pipe, token).ConfigureAwait(false);
            if (message != ReadyMessage)
            {
                log("Update: worker disconnected before confirming readiness.");
                return false;
            }

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
                return false;
            }

            await WriteMessageAsync(pipe, CommitMessage, token).ConfigureAwait(false);
            committed = true;
            log($"Update: restarter PID {restarter.Id} is ready; shutting down for replacement.");
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            log("Update: handoff was cancelled; the running app was left untouched.");
            return false;
        }
        catch (Exception exception)
        {
            log($"Update: failed to hand off to the worker: {exception}");
            return false;
        }
        finally
        {
            if (!committed && pipe.IsConnected)
            {
                try
                {
                    await WriteMessageAsync(pipe, CancelMessage, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            worker?.Dispose();
        }
    }

    private static async Task<int> RunApplyAsync(string[] args, TrayAppDotNETProgramOptions options)
    {
        string logPath = RequiredArgument(args, "--log");
        Action<string> log = message => AppendLog(logPath, $"worker: {message}");
        bool commitReceived = false;
        string? lockPath = null;

        try
        {
            int parentPID = RequiredIntArgument(args, "--parent-pid");
            long parentStartTimeTicks = RequiredLongArgument(args, "--parent-start");
            string pipeName = RequiredArgument(args, "--pipe");
            string targetExecutable = Path.GetFullPath(RequiredArgument(args, "--target"));
            string sourceExecutable = Environment.ProcessPath
                                      ?? throw new InvalidOperationException("Cannot resolve staged executable path.");
            string sourceDirectory = Path.GetDirectoryName(sourceExecutable)
                                     ?? throw new InvalidOperationException("Cannot resolve staged update directory.");
            string targetDirectory = Path.GetDirectoryName(targetExecutable)
                                     ?? throw new InvalidOperationException("Cannot resolve install directory.");

            if (!File.Exists(targetExecutable))
                throw new FileNotFoundException("Installed executable not found.", targetExecutable);
            if (!File.Exists(Path.Combine(sourceDirectory, Path.GetFileName(targetExecutable))))
            {
                throw new FileNotFoundException(
                    "The staged update does not contain the installed executable.",
                    Path.Combine(sourceDirectory, Path.GetFileName(targetExecutable)));
            }

            using Process parent = RequireProcess(parentPID, parentStartTimeTicks, "update parent");
            string? parentExecutable = TryGetProcessExecutable(parent);
            if (parentExecutable != null
                && !string.Equals(
                    Path.GetFullPath(parentExecutable),
                    targetExecutable,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The update parent does not match the target executable.");
            }

            UpdateFilePlan plan = UpdateFileTransaction.BuildPlan(
                sourceDirectory,
                targetDirectory,
                targetExecutable,
                log);
            lockPath = Path.Combine(targetDirectory, ".tadn-update.lock");
            using FileStream targetLock = AcquireTargetLock(lockPath, log);
            using NamedPipeClientStream pipe = new(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            Task connectTask = pipe.ConnectAsync();
            Task parentExitTask = parent.WaitForExitAsync();
            Task firstTask = await Task.WhenAny(connectTask, parentExitTask).ConfigureAwait(false);
            if (ReferenceEquals(firstTask, parentExitTask))
            {
                log("Parent exited before the update handoff completed.");
                return CancelledExitCode;
            }

            await connectTask.ConfigureAwait(false);
            await WriteMessageAsync(pipe, ReadyMessage, CancellationToken.None).ConfigureAwait(false);
            log($"Validated {plan.Files.Count} files and acquired the install lock.");

            byte command = await ReadMessageAsync(pipe, CancellationToken.None).ConfigureAwait(false);
            if (command != CommitMessage)
            {
                log("Parent cancelled the update before shutdown.");
                return CancelledExitCode;
            }

            commitReceived = true;
            List<string> restartExecutables = CollectRestartExecutables(
                targetExecutable,
                plan.StopSiblingApps,
                log);
            string restartListPath = RestartListPath(sourceDirectory);
            File.WriteAllLines(restartListPath, restartExecutables, new UTF8Encoding(false));

            log("Commit received; waiting for the parent to exit.");
            await parentExitTask.ConfigureAwait(false);
            StopInstalledProcesses(targetExecutable, plan.StopSiblingApps, log);

            UpdateFileTransactionResult result = UpdateFileTransaction.Apply(plan, log);
            switch (result.Status)
            {
                case UpdateFileTransactionStatus.Succeeded:
                    log("Update completed successfully.");
                    return SuccessExitCode;
                case UpdateFileTransactionStatus.FailedRolledBack:
                    log($"Update failed and was rolled back: {result.ErrorMessage}");
                    return RolledBackExitCode;
                case UpdateFileTransactionStatus.FailedRollbackIncomplete:
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
            if (!string.IsNullOrWhiteSpace(lockPath))
                TryDeleteFile(lockPath, log);
            options.FlushLog?.Invoke();
        }
    }

    private static int RunRestart(string[] args, TrayAppDotNETProgramOptions options)
    {
        string logPath = RequiredArgument(args, "--log");
        Action<string> log = message => AppendLog(logPath, $"restarter: {message}");
        string sourceExecutable = Environment.ProcessPath
                                  ?? throw new InvalidOperationException("Cannot resolve restarter path.");
        string sourceDirectory = Path.GetDirectoryName(sourceExecutable)
                                 ?? throw new InvalidOperationException("Cannot resolve restarter directory.");
        string archivePath = Path.GetFullPath(RequiredArgument(args, "--archive"));
        string targetExecutable = Path.GetFullPath(RequiredArgument(args, "--target"));

        try
        {
            int workerPID = RequiredIntArgument(args, "--worker-pid");
            long workerStartTimeTicks = RequiredLongArgument(args, "--worker-start");
            using Process worker = RequireProcess(workerPID, workerStartTimeTicks, "update worker");
            // Retain the handle so ExitCode remains available after an externally opened process exits
            _ = worker.SafeHandle;
            log($"Waiting for worker PID {workerPID}.");
            worker.WaitForExit();
            int workerExitCode = worker.ExitCode;
            log($"Worker exited with code {workerExitCode}.");

            List<string> restartExecutables = ReadRestartExecutables(
                RestartListPath(sourceDirectory),
                targetExecutable,
                log);

            switch (workerExitCode)
            {
                case SuccessExitCode:
                    StartApplications(restartExecutables, log);
                    ScheduleCleanup(sourceDirectory, archivePath, log);
                    return SuccessExitCode;
                case RolledBackExitCode:
                    StartApplications(restartExecutables, log);
                    ShowUpdateError(
                        options.ApplicationName,
                        "The update could not be installed. The previous version was restored and restarted.\n\n"
                        + $"Details: {logPath}");
                    ScheduleCleanup(sourceDirectory, archivePath, log);
                    return RolledBackExitCode;
                case CancelledExitCode:
                    ScheduleCleanup(sourceDirectory, archivePath, log);
                    return CancelledExitCode;
                case UnsafeFailureExitCode:
                    ShowUpdateError(
                        options.ApplicationName,
                        "The update failed and could not be completely rolled back. The application was not "
                        + "restarted to avoid loading a mixed installation.\n\n"
                        + $"Recovery files and details were preserved at: {sourceDirectory}\n{logPath}");
                    return UnsafeFailureExitCode;
                default:
                    StartApplications(restartExecutables, log);
                    ShowUpdateError(
                        options.ApplicationName,
                        "The update worker failed before completing the installation.\n\n"
                        + $"Details: {logPath}");
                    return workerExitCode;
            }
        }
        catch (Exception exception)
        {
            log($"Restarter failed: {exception}");
            ShowUpdateError(
                options.ApplicationName,
                "The update helper failed while waiting to restart the application.\n\n"
                + $"Details: {logPath}");
            return ValidationFailureExitCode;
        }
        finally
        {
            options.FlushLog?.Invoke();
        }
    }

    private static ProcessStartInfo BuildWorkerStartInfo(
        string stagedExecutable,
        string targetExecutable,
        string logPath,
        string pipeName,
        int parentPID,
        long parentStartTimeTicks,
        bool elevate)
    {
        ProcessStartInfo startInfo = BuildHelperStartInfo(stagedExecutable, elevate);
        AddArgument(startInfo, ApplyArgument);
        AddArgument(startInfo, "--parent-pid", parentPID.ToString(CultureInfo.InvariantCulture));
        AddArgument(
            startInfo,
            "--parent-start",
            parentStartTimeTicks.ToString(CultureInfo.InvariantCulture));
        AddArgument(startInfo, "--pipe", pipeName);
        AddArgument(startInfo, "--target", targetExecutable);
        AddArgument(startInfo, "--log", logPath);
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
        AddArgument(startInfo, "--worker-pid", workerPID.ToString(CultureInfo.InvariantCulture));
        AddArgument(
            startInfo,
            "--worker-start",
            workerStartTimeTicks.ToString(CultureInfo.InvariantCulture));
        AddArgument(startInfo, "--target", targetExecutable);
        AddArgument(startInfo, "--archive", archivePath);
        AddArgument(startInfo, "--log", logPath);
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
        int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 1), token).ConfigureAwait(false);
        return bytesRead == 1 ? buffer[0] : (byte)0;
    }

    private static async Task WriteMessageAsync(Stream stream, byte message, CancellationToken token)
    {
        byte[] buffer = [message];
        await stream.WriteAsync(buffer.AsMemory(0, 1), token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private static FileStream AcquireTargetLock(string path, Action<string> log)
    {
        for (int attempt = 1; attempt <= TargetLockAttempts; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException
                && attempt < TargetLockAttempts)
            {
                log(
                    $"Install lock is busy ({attempt}/{TargetLockAttempts}): {exception.Message}; retrying");
                Thread.Sleep(TimeConstants.UpdateFileRetryDelayMs);
            }
        }

        throw new InvalidOperationException("Update lock retry loop ended unexpectedly.");
    }

    private static List<string> CollectRestartExecutables(
        string targetExecutable,
        bool includeSiblings,
        Action<string> log)
    {
        HashSet<string> allowedExecutables = InstalledExecutablePaths(targetExecutable, includeSiblings);
        HashSet<string> restartExecutables = new(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(targetExecutable)
        };

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                string? executable = TryGetProcessExecutable(process);
                if (executable != null && allowedExecutables.Contains(Path.GetFullPath(executable)))
                    restartExecutables.Add(Path.GetFullPath(executable));
            }
            catch (Exception exception)
            {
                log($"Could not inspect PID {SafeProcessID(process)}: {exception.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        return [.. restartExecutables.Order(StringComparer.OrdinalIgnoreCase)];
    }

    private static void StopInstalledProcesses(
        string targetExecutable,
        bool includeSiblings,
        Action<string> log)
    {
        HashSet<string> allowedExecutables = InstalledExecutablePaths(targetExecutable, includeSiblings);

        for (int attempt = 1; attempt <= ProcessStopAttempts; attempt++)
        {
            List<Process> processes = FindProcesses(allowedExecutables);
            if (processes.Count == 0) return;

            foreach (Process process in processes)
            {
                try
                {
                    if (process.HasExited) continue;
                    log($"Stopping PID {process.Id}: {TryGetProcessExecutable(process)}");
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(TimeConstants.UpdateProcessRetryDelayMs);
                }
                catch (Exception exception)
                {
                    log($"Could not stop PID {SafeProcessID(process)}: {exception.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (attempt < ProcessStopAttempts)
                Thread.Sleep(TimeConstants.UpdateProcessRetryDelayMs);
        }

        List<Process> remaining = FindProcesses(allowedExecutables);
        try
        {
            if (remaining.Count > 0)
            {
                string processIDs = string.Join(", ", remaining.Select(SafeProcessID));
                throw new IOException($"Could not stop installed process IDs: {processIDs}");
            }
        }
        finally
        {
            foreach (Process process in remaining)
                process.Dispose();
        }
    }

    private static List<Process> FindProcesses(HashSet<string> allowedExecutables)
    {
        List<Process> matches = [];
        foreach (Process process in Process.GetProcesses())
        {
            bool keep = false;
            try
            {
                if (process.Id == Environment.ProcessId || process.HasExited) continue;
                string? executable = TryGetProcessExecutable(process);
                keep = executable != null && allowedExecutables.Contains(Path.GetFullPath(executable));
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

    private static HashSet<string> InstalledExecutablePaths(string targetExecutable, bool includeSiblings)
    {
        string normalizedTarget = Path.GetFullPath(targetExecutable);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase) { normalizedTarget };
        if (!includeSiblings) return paths;

        string? targetDirectory = Path.GetDirectoryName(normalizedTarget);
        if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
            return paths;

        foreach (string executable in Directory.EnumerateFiles(targetDirectory, "*.exe"))
        {
            if (Path.GetFileName(executable).EndsWith("TrayAppDotNET.exe", StringComparison.OrdinalIgnoreCase))
                paths.Add(Path.GetFullPath(executable));
        }

        return paths;
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
            catch (Exception exception)
            {
                log($"Could not read restart list: {exception.Message}");
            }
        }

        executables.Add(Path.GetFullPath(targetExecutable));
        return [.. executables.Order(StringComparer.OrdinalIgnoreCase)];
    }

    private static void StartApplications(IReadOnlyList<string> executables, Action<string> log)
    {
        foreach (string executable in executables)
        {
            try
            {
                if (!File.Exists(executable))
                {
                    log($"Cannot restart missing executable: {executable}");
                    continue;
                }

                ProcessStartInfo startInfo = new()
                {
                    FileName = executable,
                    WorkingDirectory = Path.GetDirectoryName(executable) ?? ".",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using Process? process = Process.Start(startInfo);
                log(process == null
                    ? $"Process.Start returned null for {executable}"
                    : $"Restarted {executable} as PID {process.Id}");
            }
            catch (Exception exception)
            {
                log($"Could not restart {executable}: {exception}");
            }
        }
    }

    private static void ScheduleCleanup(string sourceDirectory, string archivePath, Action<string> log)
    {
        string scriptPath = sourceDirectory.TrimEnd(Path.DirectorySeparatorChar) + ".cleanup.bat";
        string delaySeconds = Math.Max(1, TimeConstants.UpdateCleanupRetryDelayMs / 1000)
            .ToString(CultureInfo.InvariantCulture);
        string escapedSource = EscapeBatchPath(sourceDirectory);
        string escapedArchive = EscapeBatchPath(archivePath);
        string script = $$"""
            @echo off
            setlocal
            :retry
            rmdir /s /q "{{escapedSource}}" >nul 2>&1
            del /f /q "{{escapedArchive}}" >nul 2>&1
            if exist "{{escapedSource}}" goto wait
            if exist "{{escapedArchive}}" goto wait
            goto done
            :wait
            timeout /t {{delaySeconds}} /nobreak >nul 2>&1
            goto retry
            :done
            del /f /q "%~f0" >nul 2>&1
            """;

        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
        ProcessStartInfo startInfo = new()
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(scriptPath);
        using Process? cleanup = Process.Start(startInfo);
        log(cleanup == null
            ? $"Could not start cleanup script: {scriptPath}"
            : $"Scheduled staging cleanup as PID {cleanup.Id}");
    }

    private static string EscapeBatchPath(string path) => path.Replace("%", "%%", StringComparison.Ordinal);

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
        finally
        {
            try
            {
                if (File.Exists(probePath)) File.Delete(probePath);
            }
            catch
            {
            }
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
            "WindowsApps");
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

    private static string? TryGetProcessExecutable(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static int SafeProcessID(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return 0;
        }
    }

    private static string RestartListPath(string sourceDirectory) =>
        Path.Combine(sourceDirectory, ".update-restart-list");

    private static void AppendLog(string logPath, string message)
    {
        string line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
        for (int attempt = 1; attempt <= LogWriteAttempts; attempt++)
        {
            try
            {
                string? directory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                using FileStream stream = new(
                    logPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                using StreamWriter writer = new(stream, new UTF8Encoding(false));
                writer.Write(line);
                return;
            }
            catch when (attempt < LogWriteAttempts)
            {
                Thread.Sleep(TimeConstants.UpdateLogRetryDelayMs);
            }
            catch
            {
                return;
            }
        }
    }

    private static void TryDeleteFile(string path, Action<string> log)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception)
        {
            log($"Could not delete {path}: {exception.Message}");
        }
    }

    private static void ShowUpdateError(string applicationName, string message)
    {
        try
        {
            _ = User32.MessageBox(IntPtr.Zero, message, $"{applicationName} Update", User32.MB_ICONERROR);
        }
        catch
        {
        }
    }

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
}
