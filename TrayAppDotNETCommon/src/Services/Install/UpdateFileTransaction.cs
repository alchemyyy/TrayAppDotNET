namespace TrayAppDotNETCommon.Services.Install;

internal enum UpdateFileTransactionStatus
{
    Succeeded,
    FailedRolledBack,
    FailedRollbackIncomplete
}

internal sealed record UpdateFileTransactionResult(
    UpdateFileTransactionStatus Status,
    string? ErrorMessage = null);

internal sealed record UpdateFilePlan(
    IReadOnlyList<UpdateFileOperation> Files,
    bool StopSiblingApps);

internal sealed class UpdateFileOperation(
    string relativePath,
    string sourcePath,
    string destinationPath,
    string temporaryPath,
    string backupPath)
{
    public string RelativePath { get; } = relativePath;
    public string SourcePath { get; } = sourcePath;
    public string DestinationPath { get; } = destinationPath;
    public string TemporaryPath { get; } = temporaryPath;
    public string BackupPath { get; } = backupPath;
    public bool DestinationExisted { get; set; }
    public FileAttributes DestinationAttributes { get; set; }
    public bool WasApplied { get; set; }
}

/// <summary>Stages an update beside its destination and rolls back any failed replacement.</summary>
internal static class UpdateFileTransaction
{
    private const int FileOperationAttempts = 40;
    private const int CopyBufferSize = 128 * 1024;

    private static readonly string[] SharedNativeDLLFileNames =
    [
        "av_libglesv2.dll",
        "libHarfBuzzSharp.dll",
        "libSkiaSharp.dll",
        "libMonoPosixHelper.dll",
        "MonoPosixHelper.dll"
    ];

    /// <summary>Enumerates the extracted payload and places the running app executable last.</summary>
    public static UpdateFilePlan BuildPlan(
        string sourceDirectory,
        string targetDirectory,
        string targetExecutable,
        Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetExecutable);
        ArgumentNullException.ThrowIfNull(log);

        string sourceRoot = Path.GetFullPath(sourceDirectory);
        string targetRoot = Path.GetFullPath(targetDirectory);
        string normalizedTargetExecutable = Path.GetFullPath(targetExecutable);
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"Extracted update directory not found: {sourceRoot}");
        if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The update source and destination cannot be the same directory.");

        string transactionToken = $"{Environment.ProcessId}-{Guid.NewGuid():N}";
        List<UpdateFileOperation> files = [];
        bool stopSiblingApps = false;
        int fileIndex = 0;

        string[] sourceFiles =
        [
            .. Directory.EnumerateFiles(sourceRoot, searchPattern: "*", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
        ];

        foreach (string sourcePath in sourceFiles)
        {
            string relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            string destinationPath = Path.Combine(targetRoot, relativePath);

            if (IsSharedNativeDLL(relativePath) && File.Exists(destinationPath))
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

            if (IsSharedNativeDLL(relativePath))
                stopSiblingApps = true;

            string artifactSuffix = $".tadn-update-{transactionToken}-{fileIndex++}";
            files.Add(new UpdateFileOperation(
                relativePath,
                sourcePath,
                destinationPath,
                destinationPath + artifactSuffix + ".tmp",
                destinationPath + artifactSuffix + ".bak"));
        }

        if (files.Count == 0)
            throw new InvalidOperationException("The extracted update contains no installable files.");
        if (!files.Any(file =>
                string.Equals(file.DestinationPath, normalizedTargetExecutable, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"The update package does not contain {Path.GetFileName(normalizedTargetExecutable)}.");
        }

        List<UpdateFileOperation> orderedFiles = [];
        orderedFiles.AddRange(files
            .OrderBy(file =>
                string.Equals(file.DestinationPath, normalizedTargetExecutable, StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0)
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase));
        return new UpdateFilePlan(orderedFiles, stopSiblingApps);
    }

    /// <summary>Applies every planned replacement or restores every destination already changed.</summary>
    public static UpdateFileTransactionResult Apply(UpdateFilePlan plan, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(log);

        try
        {
            StageFiles(plan.Files, log);
            CommitFiles(plan.Files, log);
            CleanupArtifacts(plan.Files, includeBackups: true, log);
            return new UpdateFileTransactionResult(UpdateFileTransactionStatus.Succeeded);
        }
        catch (Exception exception)
        {
            log($"Update: file replacement failed: {exception}");
            bool rollbackSucceeded = RollBack(plan.Files, log);
            CleanupArtifacts(plan.Files, rollbackSucceeded, log);
            return new UpdateFileTransactionResult(
                rollbackSucceeded
                    ? UpdateFileTransactionStatus.FailedRolledBack
                    : UpdateFileTransactionStatus.FailedRollbackIncomplete,
                exception.Message);
        }
    }

    private static void StageFiles(IReadOnlyList<UpdateFileOperation> files, Action<string> log)
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
            DeleteIfPresent(file.TemporaryPath, log);
            ExecuteWithRetry(
                () => File.Copy(file.SourcePath, file.TemporaryPath, overwrite: true),
                $"stage {file.RelativePath}",
                log);

            long sourceLength = new FileInfo(file.SourcePath).Length;
            long stagedLength = new FileInfo(file.TemporaryPath).Length;
            if (sourceLength != stagedLength)
                throw new IOException($"Staged file size differs for {file.RelativePath}.");

            log($"Update: staged {file.RelativePath} ({sourceLength} bytes)");
        }
    }

    private static void CommitFiles(IReadOnlyList<UpdateFileOperation> files, Action<string> log)
    {
        foreach (UpdateFileOperation file in files)
        {
            file.DestinationExisted = File.Exists(file.DestinationPath);
            if (file.DestinationExisted)
            {
                file.DestinationAttributes = File.GetAttributes(file.DestinationPath);
                DeleteIfPresent(file.BackupPath, log);
                ExecuteWithRetry(
                    () => File.Copy(file.DestinationPath, file.BackupPath, overwrite: true),
                    $"back up {file.RelativePath}",
                    log);

                long destinationLength = new FileInfo(file.DestinationPath).Length;
                long backupLength = new FileInfo(file.BackupPath).Length;
                if (destinationLength != backupLength)
                    throw new IOException($"Backup file size differs for {file.RelativePath}.");

                ClearReadOnly(file.DestinationPath);
            }

            ExecuteWithRetry(
                () => File.Move(file.TemporaryPath, file.DestinationPath, overwrite: true),
                $"replace {file.RelativePath}",
                log);
            file.WasApplied = true;
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
                ClearReadOnly(file.DestinationPath);
                if (file.DestinationExisted)
                {
                    if (!File.Exists(file.BackupPath))
                        throw new FileNotFoundException(message: "Update backup is missing.", file.BackupPath);

                    ExecuteWithRetry(
                        () => File.Copy(file.BackupPath, file.DestinationPath, overwrite: true),
                        $"restore {file.RelativePath}",
                        log);
                    File.SetAttributes(file.DestinationPath, file.DestinationAttributes);
                }
                else
                    DeleteIfPresent(file.DestinationPath, log);

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

    private static void CleanupArtifacts(
        IReadOnlyList<UpdateFileOperation> files,
        bool includeBackups,
        Action<string> log)
    {
        foreach (UpdateFileOperation file in files)
        {
            TryDelete(file.TemporaryPath, log);
            if (includeBackups)
                TryDelete(file.BackupPath, log);
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
            int firstRead = first.Read(firstBuffer);
            int secondRead = second.Read(secondBuffer);
            if (firstRead != secondRead) return false;
            if (firstRead == 0) return true;
            if (!firstBuffer.AsSpan(start: 0, firstRead).SequenceEqual(secondBuffer.AsSpan(start: 0, secondRead)))
                return false;
        }
    }

    private static bool IsSharedNativeDLL(string relativePath) =>
        string.IsNullOrEmpty(Path.GetDirectoryName(relativePath))
        && SharedNativeDLLFileNames.Contains(Path.GetFileName(relativePath), StringComparer.OrdinalIgnoreCase);

    private static void ClearReadOnly(string path)
    {
        if (!File.Exists(path)) return;
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }

    private static void DeleteIfPresent(string path, Action<string> log)
    {
        if (!File.Exists(path)) return;
        ExecuteWithRetry(
            () =>
            {
                ClearReadOnly(path);
                File.Delete(path);
            },
            $"delete {Path.GetFileName(path)}",
            log);
    }

    private static void TryDelete(string path, Action<string> log)
    {
        try
        {
            DeleteIfPresent(path, log);
        }
        catch (Exception exception)
        {
            log($"Update: could not remove artifact {path}: {exception.Message}");
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
