using System.Globalization;
using System.Text;

namespace TrayAppDotNETCommon.Services.Install;

internal enum UpdateFileTransactionStatus
{
    Succeeded,
    FailedRolledBack,
    FailedRollbackIncomplete
}

/// <summary>
/// How an apply ended. <see cref="LeftoverPaths"/> lists transaction files that could not be deleted yet, such as
/// the backup of the worker's own running image; the caller removes them once it has exited.
/// </summary>
internal sealed record UpdateFileTransactionResult(
    UpdateFileTransactionStatus Status,
    string? ErrorMessage = null,
    IReadOnlyList<string>? LeftoverPaths = null);

/// <summary>
/// The files one update replaces. <see cref="StagingDirectory"/> holds the verified payload inside the install
/// folder, so every replacement is a rename on the same volume.
/// </summary>
internal sealed record UpdateFilePlan(
    IReadOnlyList<UpdateFileOperation> Files,
    bool StopSiblingApps,
    string TargetDirectory,
    string ApplicationName,
    string StagingDirectory);

internal sealed class UpdateFileOperation(
    string relativePath,
    string sourcePath,
    string destinationPath,
    string backupPath)
{
    public string RelativePath { get; } = relativePath;
    public string SourcePath { get; } = sourcePath;
    public string DestinationPath { get; } = destinationPath;
    public string BackupPath { get; } = backupPath;
    public bool DestinationExisted { get; set; }
    public bool WasApplied { get; set; }
}

/// <summary>
/// Replaces an installed app's payload with a staged one as a journaled transaction. Each existing file is renamed
/// to a backup beside it (a rename works even for the running image of the worker), then the staged file is renamed
/// into place. A journal in the install folder lists every step before the first change, so an apply that is
/// killed midway is rolled back by <see cref="RecoverInterrupted"/> the next time an update or install runs.
/// </summary>
internal static class UpdateFileTransaction
{
    private const int FileOperationAttempts = 40;
    private const int CopyBufferSize = 128 * 1024;
    private const string ArtifactMarker = ".tadn-update-";
    private const string JournalHeader = "TrayAppDotNET update journal 2";
    // Appended once every file was replaced; a journal that ends with it is never rolled back
    private const string JournalCommittedMarker = "committed";
    private const char JournalSeparator = '\t';

    /// <summary>The journal of an apply in progress for <paramref name="applicationName"/>.</summary>
    public static string JournalPath(string targetDirectory, string applicationName) =>
        Path.Combine(targetDirectory, ArtifactMarker + applicationName + ".journal");

    /// <summary>The lock file one app's update worker holds from validation until it exits.</summary>
    public static string LockPath(string targetDirectory, string applicationName) =>
        Path.Combine(targetDirectory, ArtifactMarker + applicationName + ".lock");

    /// <summary>A fresh staging folder name inside the install folder for one update of the app.</summary>
    public static string NewStagingDirectory(string targetDirectory, string applicationName) =>
        Path.Combine(targetDirectory, ArtifactMarker + applicationName + "-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Plans the replacement of the app's installed payload, and nothing else of the package, from
    /// <paramref name="payloadDirectory"/>. Unchanged shared files are skipped; shared native DLLs come first and
    /// the app exe last, so a failure never leaves a new exe beside old native code.
    /// </summary>
    public static UpdateFilePlan BuildPlan(
        string payloadDirectory,
        string targetDirectory,
        string targetExecutable,
        TrayAppDotNETInstallPayload payload,
        string stagingDirectory,
        Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetExecutable);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(log);

        string sourceRoot = Path.GetFullPath(payloadDirectory);
        string targetRoot = Path.GetFullPath(targetDirectory);
        string normalizedTargetExecutable = Path.GetFullPath(targetExecutable);
        string executableFileName = Path.GetFileName(normalizedTargetExecutable);
        string applicationName = Path.GetFileNameWithoutExtension(executableFileName);
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"Extracted update directory not found: {sourceRoot}");
        if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The update source and destination cannot be the same directory.");

        string transactionToken = $"{applicationName}-{Guid.NewGuid():N}";
        List<(string RelativePath, bool Shared)> candidates = [];
        foreach (TrayAppDotNETInstallFile file in payload.InstalledFiles(executableFileName))
        {
            if (File.Exists(Path.Combine(sourceRoot, file.Name)))
            {
                candidates.Add((file.Name, file.RemoveOnlyWhenInstallRootHasNoExe));
                continue;
            }

            bool required = string.Equals(file.Name, executableFileName, StringComparison.OrdinalIgnoreCase)
                            || payload.RequiredFiles.Contains(file);
            if (required)
                throw new InvalidOperationException($"The update package does not contain {file.Name}.");
        }

        foreach (TrayAppDotNETInstallDirectory directory in payload.InstalledDirectories)
        {
            string sourceDirectory = Path.Combine(sourceRoot, directory.Name);
            if (!Directory.Exists(sourceDirectory))
            {
                if (payload.RequiredDirectories.Contains(directory))
                    throw new InvalidOperationException($"The update package does not contain {directory.Name}\\.");
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(sourceDirectory, searchPattern: "*", SearchOption.AllDirectories))
                candidates.Add((Path.GetRelativePath(sourceRoot, file), directory.RemoveOnlyWhenInstallRootHasNoExe));
        }

        List<UpdateFileOperation> files = [];
        bool stopSiblingApps = false;
        int fileIndex = 0;
        foreach ((string relativePath, bool shared) in candidates)
        {
            string sourcePath = Path.Combine(sourceRoot, relativePath);
            string destinationPath = Path.Combine(targetRoot, relativePath);
            if (shared && File.Exists(destinationPath))
            {
                try
                {
                    if (FilesEqual(sourcePath, destinationPath))
                    {
                        log($"Update: shared file is unchanged; skipping {relativePath}");
                        continue;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    log($"Update: could not compare {relativePath}; it will be replaced: {exception.Message}");
                }
            }

            if (TrayAppDotNETInstalledProcesses.IsSharedNativeDll(relativePath)) stopSiblingApps = true;

            files.Add(new UpdateFileOperation(
                relativePath,
                sourcePath,
                destinationPath,
                destinationPath + ArtifactMarker + transactionToken + "-" + (fileIndex++).ToString(CultureInfo.InvariantCulture) + ".bak"));
        }

        List<UpdateFileOperation> orderedFiles =
        [
            .. files
                .OrderBy(file => OrderKey(file, normalizedTargetExecutable))
                .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
        ];
        return new UpdateFilePlan(orderedFiles, stopSiblingApps, targetRoot, applicationName, Path.GetFullPath(stagingDirectory));
    }

    /// <summary>Applies every planned replacement, or restores every destination already changed.</summary>
    public static UpdateFileTransactionResult Apply(UpdateFilePlan plan, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(log);

        string journalPath = JournalPath(plan.TargetDirectory, plan.ApplicationName);
        try
        {
            WriteJournal(journalPath, plan);
            CommitFiles(plan.Files, log);
            SealJournal(journalPath, log);
            List<string> leftovers = DeleteBackups(plan.Files, log);
            return new UpdateFileTransactionResult(UpdateFileTransactionStatus.Succeeded, LeftoverPaths: leftovers);
        }
        catch (Exception exception)
        {
            log($"Update: file replacement failed: {exception}");
            bool rollbackSucceeded = RollBack(plan.Files, log);
            List<string> leftovers = [];
            if (rollbackSucceeded)
            {
                TryDelete(journalPath, log);
                leftovers = DeleteBackups(plan.Files, log);
            }

            return new UpdateFileTransactionResult(
                rollbackSucceeded
                    ? UpdateFileTransactionStatus.FailedRolledBack
                    : UpdateFileTransactionStatus.FailedRollbackIncomplete,
                exception.Message,
                leftovers);
        }
    }

    /// <summary>
    /// Rolls back an apply of <paramref name="applicationName"/> that never finished (its worker was killed or
    /// crashed) and deletes the transaction files and staging folders it left. Call it only while holding the
    /// app's update lock, so no worker of this app is running. Returns false when something could not be restored;
    /// the journal is kept then so a later run can retry.
    /// </summary>
    public static bool RecoverInterrupted(string targetDirectory, string applicationName, Action<string> log)
    {
        if (!Directory.Exists(targetDirectory)) return true;

        string journalPath = JournalPath(targetDirectory, applicationName);
        bool restored = true;
        if (File.Exists(journalPath) && IsCommitted(journalPath))
        {
            // The update completed and only its journal outlived it; nothing is undone
            log($"Update: the last update of {applicationName} completed; removing its journal and backups");
            TryDelete(journalPath, log);
        }
        else if (File.Exists(journalPath))
        {
            List<UpdateFileOperation> operations = ReadJournal(journalPath);
            log($"Update: rolling back an interrupted update of {applicationName} ({operations.Count} files)");
            for (int index = operations.Count - 1; index >= 0; index--)
            {
                UpdateFileOperation operation = operations[index];
                try
                {
                    if (File.Exists(operation.BackupPath))
                    {
                        // The original was moved aside; whatever sits at the destination now is the new file
                        if (File.Exists(operation.DestinationPath)) DeleteWithRetry(operation.DestinationPath, log);
                        MoveWithRetry(operation.BackupPath, operation.DestinationPath, $"restore {operation.RelativePath}", log);
                    }
                    // A file the update added: its staged source is gone because it was moved into place
                    else if (!operation.DestinationExisted && !File.Exists(operation.SourcePath)
                             && File.Exists(operation.DestinationPath))
                    {
                        DeleteWithRetry(operation.DestinationPath, log);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                      or InvalidOperationException)
                {
                    restored = false;
                    log($"Update: FAILED to restore {operation.RelativePath}: {exception.Message}");
                }
            }

            if (!restored) return false;
            TryDelete(journalPath, log);
        }

        SweepArtifacts(targetDirectory, applicationName, log);
        return restored;
    }

    private static int OrderKey(UpdateFileOperation file, string targetExecutable)
    {
        if (string.Equals(file.DestinationPath, targetExecutable, StringComparison.OrdinalIgnoreCase)) return 2;
        return TrayAppDotNETInstalledProcesses.IsSharedNativeDll(file.RelativePath) ? 0 : 1;
    }

    private static void WriteJournal(string journalPath, UpdateFilePlan plan)
    {
        StringBuilder builder = new();
        builder.AppendLine(JournalHeader);
        foreach (UpdateFileOperation file in plan.Files)
        {
            bool existed = File.Exists(file.DestinationPath);
            builder.Append(file.RelativePath).Append(JournalSeparator)
                .Append(file.SourcePath).Append(JournalSeparator)
                .Append(file.DestinationPath).Append(JournalSeparator)
                .Append(file.BackupPath).Append(JournalSeparator)
                .Append(existed ? '1' : '0')
                .AppendLine();
        }

        string temporaryPath = journalPath + ".tmp";
        File.WriteAllText(temporaryPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, journalPath, overwrite: true);
    }

    /// <summary>True when the journal ends with the committed marker, so the apply it describes completed.</summary>
    private static bool IsCommitted(string journalPath) =>
        File.ReadAllLines(journalPath, Encoding.UTF8).LastOrDefault(line => line.Length > 0) == JournalCommittedMarker;

    private static List<UpdateFileOperation> ReadJournal(string journalPath)
    {
        string[] lines = File.ReadAllLines(journalPath, Encoding.UTF8);
        if (lines.Length == 0 || lines[0] != JournalHeader)
            throw new InvalidDataException($"Unrecognized update journal: {journalPath}");

        List<UpdateFileOperation> operations = [];
        foreach (string line in lines.Skip(1))
        {
            if (line.Length == 0 || line == JournalCommittedMarker) continue;
            string[] fields = line.Split(JournalSeparator);
            if (fields.Length != 5) throw new InvalidDataException($"Malformed update journal line: {line}");
            operations.Add(new UpdateFileOperation(fields[0], fields[1], fields[2], fields[3])
            {
                DestinationExisted = fields[4] == "1"
            });
        }

        return operations;
    }

    /// <summary>
    /// Records that every file was replaced: appends the committed marker, then deletes the journal. Either one
    /// keeps a later recovery from undoing the update. Throws when neither can be done, so the caller rolls back
    /// while it still knows every step.
    /// </summary>
    private static void SealJournal(string journalPath, Action<string> log)
    {
        bool marked = false;
        try
        {
            File.AppendAllText(journalPath, JournalCommittedMarker + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            marked = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log($"Update: could not mark the journal committed: {exception.Message}");
        }

        if (TryDeleteJournal(journalPath, log) || marked) return;

        throw new IOException($"The update journal {journalPath} could neither be marked committed nor removed.");
    }

    private static bool TryDeleteJournal(string journalPath, Action<string> log)
    {
        TryDelete(journalPath, log);
        return !File.Exists(journalPath);
    }

    private static void CommitFiles(IReadOnlyList<UpdateFileOperation> files, Action<string> log)
    {
        foreach (UpdateFileOperation file in files)
        {
            string? destinationDirectory = Path.GetDirectoryName(file.DestinationPath);
            if (string.IsNullOrWhiteSpace(destinationDirectory))
                throw new InvalidOperationException($"No destination directory for {file.RelativePath}");

            ExecuteWithRetry(
                () => Directory.CreateDirectory(destinationDirectory),
                $"create directory for {file.RelativePath}",
                log);

            file.DestinationExisted = File.Exists(file.DestinationPath);
            if (file.DestinationExisted)
                MoveWithRetry(file.DestinationPath, file.BackupPath, $"back up {file.RelativePath}", log);

            // Counted as applied from here on: the destination is moved aside and must come back on failure
            file.WasApplied = true;
            MoveWithRetry(file.SourcePath, file.DestinationPath, $"replace {file.RelativePath}", log);
            log($"Update: replaced {file.RelativePath}");
        }
    }

    private static bool RollBack(IReadOnlyList<UpdateFileOperation> files, Action<string> log)
    {
        bool succeeded = true;
        for (int fileIndex = files.Count - 1; fileIndex >= 0; fileIndex--)
        {
            UpdateFileOperation file = files[fileIndex];
            if (!file.WasApplied) continue;

            try
            {
                if (file.DestinationExisted)
                {
                    if (!File.Exists(file.BackupPath))
                        throw new FileNotFoundException(message: "Update backup is missing.", file.BackupPath);

                    if (File.Exists(file.DestinationPath)) DeleteWithRetry(file.DestinationPath, log);
                    MoveWithRetry(file.BackupPath, file.DestinationPath, $"restore {file.RelativePath}", log);
                }
                else if (File.Exists(file.DestinationPath))
                    DeleteWithRetry(file.DestinationPath, log);

                file.WasApplied = false;
                log($"Update: restored {file.RelativePath}");
            }
            catch (Exception exception)
            {
                succeeded = false;
                log($"Update: FAILED to restore {file.RelativePath}: {exception}");
            }
        }

        return succeeded;
    }

    /// <summary>Deletes the backups once they are no longer needed and returns the ones still in use.</summary>
    private static List<string> DeleteBackups(IReadOnlyList<UpdateFileOperation> files, Action<string> log)
    {
        List<string> leftovers = [];
        foreach (UpdateFileOperation file in files)
        {
            if (!File.Exists(file.BackupPath)) continue;

            try
            {
                ClearReadOnly(file.BackupPath);
                File.Delete(file.BackupPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The worker's own image, renamed to its backup, stays mapped until the worker exits
                log($"Update: {file.BackupPath} is still in use; it is removed after exit");
            }

            if (File.Exists(file.BackupPath)) leftovers.Add(file.BackupPath);
        }

        return leftovers;
    }

    /// <summary>
    /// Deletes everything updates of <paramref name="applicationName"/> keep in the install folder (journal, lock,
    /// staging folders, backups). Uninstall calls it, since nothing of the app is restored afterwards.
    /// </summary>
    public static void DeleteArtifacts(string targetDirectory, string applicationName, Action<string> log)
    {
        if (!Directory.Exists(targetDirectory)) return;

        TryDelete(JournalPath(targetDirectory, applicationName), log);
        TryDelete(LockPath(targetDirectory, applicationName), log);
        SweepArtifacts(targetDirectory, applicationName, log);
    }

    /// <summary>Deletes this app's staging folders and backup files left by an earlier, interrupted update.</summary>
    private static void SweepArtifacts(string targetDirectory, string applicationName, Action<string> log)
    {
        string prefix = ArtifactMarker + applicationName + "-";
        foreach (string directory in Directory.EnumerateDirectories(targetDirectory, ArtifactMarker + "*"))
        {
            if (!Path.GetFileName(directory).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                Directory.Delete(directory, recursive: true);
                log($"Update: removed leftover staging folder {directory}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                log($"Update: could not remove leftover staging folder {directory}: {exception.Message}");
            }
        }

        foreach (string file in Directory.EnumerateFiles(targetDirectory, "*" + prefix + "*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
            TryDelete(file, log);
        }
    }

    private static bool FilesEqual(string firstPath, string secondPath)
    {
        FileInfo firstInfo = new(firstPath);
        FileInfo secondInfo = new(secondPath);
        if (firstInfo.Length != secondInfo.Length) return false;

        using FileStream first = new(
            firstPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            CopyBufferSize,
            FileOptions.SequentialScan);
        using FileStream second = new(
            secondPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            CopyBufferSize,
            FileOptions.SequentialScan);
        byte[] firstBuffer = new byte[CopyBufferSize];
        byte[] secondBuffer = new byte[CopyBufferSize];

        while (true)
        {
            int firstRead = first.ReadAtLeast(firstBuffer, firstBuffer.Length, throwOnEndOfStream: false);
            int secondRead = second.ReadAtLeast(secondBuffer, secondBuffer.Length, throwOnEndOfStream: false);
            if (firstRead != secondRead) return false;
            if (firstRead == 0) return true;
            if (!firstBuffer.AsSpan(start: 0, firstRead).SequenceEqual(secondBuffer.AsSpan(start: 0, secondRead)))
                return false;
        }
    }

    private static void ClearReadOnly(string path)
    {
        if (!File.Exists(path)) return;
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }

    private static void MoveWithRetry(string source, string destination, string description, Action<string> log) =>
        ExecuteWithRetry(() => File.Move(source, destination, overwrite: false), description, log);

    private static void DeleteWithRetry(string path, Action<string> log) =>
        ExecuteWithRetry(
            () =>
            {
                ClearReadOnly(path);
                File.Delete(path);
            },
            $"delete {Path.GetFileName(path)}",
            log);

    private static void TryDelete(string path, Action<string> log)
    {
        try
        {
            if (File.Exists(path)) DeleteWithRetry(path, log);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidOperationException)
        {
            log($"Update: could not remove {path}: {exception.Message}");
        }
    }

    private static void ExecuteWithRetry(Action operation, string description, Action<string> log)
    {
        for (int attempt = 1; attempt <= FileOperationAttempts; attempt++)
        {
            try
            {
                operation();
                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException
                && attempt < FileOperationAttempts)
            {
                log(
                    $"Update: {description} failed ({attempt}/{FileOperationAttempts}): "
                    + $"{exception.Message}; retrying");
                Thread.Sleep(TimeConstants.UpdateFileRetryDelayMs);
            }
        }

        throw new InvalidOperationException($"Update: retry loop ended unexpectedly while trying to {description}.");
    }
}
