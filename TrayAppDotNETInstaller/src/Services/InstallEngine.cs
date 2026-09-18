using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using TrayAppDotNETInstaller.Localization;

namespace TrayAppDotNETInstaller.Services;

/// <summary>Extracts the selected packages and drives each app's own headless installer.</summary>
public static class InstallEngine
{
    // Inside one app's slice, extraction owns the first 40 percent and the headless install the rest
    private const int ExtractPortionEndPercent = 40;
    private const int FullInnerPercent = InstallProgressLine.CompletePercent;
    private const string HeadlessInstallArgument = "--install-headless";
    private const string DesktopShortcutArgument = "--desktop-shortcut";
    private const string StartMenuShortcutArgument = "--start-menu-shortcut";
    private const string LocalScope = "local";
    private const string SystemScope = "system";
    private const string TrueValue = "true";
    private const string FalseValue = "false";
    private const string InstalledToPrefix = "Installed to ";
    private const int OutputDrainTimeoutMs = 250;
    private const int TemporaryDeleteAttempts = 5;
    private const int TemporaryDeleteRetryDelayMs = 200;
    private const char ZipDirectorySeparator = '/';
    private const string InstallScriptFileName = "install.bat";

    private sealed record AppInstallResult(string? Error, string? InstalledExecutable);

    public static Task<InstallOutcome> RunAsync(
        InstallPlan plan,
        IProgress<InstallProgressLine> progress,
        CancellationToken cancellationToken) =>
        RunAsync(plan, EmbeddedPayloadCatalog.Load().OpenPayload, progress, cancellationToken);

    /// <summary>Runs the plan; <paramref name="openPayload"/> lets tests substitute on-disk zips.</summary>
    internal static async Task<InstallOutcome> RunAsync(
        InstallPlan plan,
        Func<EmbeddedPayload, Stream> openPayload,
        IProgress<InstallProgressLine> progress,
        CancellationToken cancellationToken)
    {
        FrameworkCompatibility.ThrowIfNull(plan, nameof(plan));
        FrameworkCompatibility.ThrowIfNull(openPayload, nameof(openPayload));
        FrameworkCompatibility.ThrowIfNull(progress, nameof(progress));

        List<string> installedExecutables = [];
        if (plan.Payloads.Count == 0) return Fail(L(nameof(AppStrings.Installer_Progress_NoPayloads)), progress, installedExecutables);

        string? temporaryRoot = null;
        try
        {
            for (int index = 0; index < plan.Payloads.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EmbeddedPayload payload = plan.Payloads[index];
                ProgressSlice slice = ProgressSlice.ForIndex(index, plan.Payloads.Count);
                AppInstallResult result;
                switch (plan.Mode)
                {
                    case InstallMode.Portable:
                        result = InstallPortable(plan, payload, openPayload, slice, progress, cancellationToken);
                        break;
                    case InstallMode.Local:
                    case InstallMode.System:
                        temporaryRoot ??= CreateTemporaryRoot();
                        result = await InstallThroughAppAsync(
                            plan, payload, openPayload, temporaryRoot, slice, progress, cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(plan), plan.Mode, message: "Unsupported install mode.");
                }

                if (result.Error != null) return Fail(result.Error, progress, installedExecutables);

                installedExecutables.Add(result.InstalledExecutable
                                         ?? InstallDefaults.InstalledExecutablePath(
                                             plan.Mode, plan.TargetDirectory, payload.ApplicationName));
            }

            progress.Report(InstallProgressLine.At(InstallProgressLine.CompletePercent, L(nameof(AppStrings.Installer_Progress_Complete))));
            return new InstallOutcome(Success: true, ErrorMessage: null, installedExecutables);
        }
        catch (OperationCanceledException)
        {
            return Fail(L(nameof(AppStrings.Installer_Progress_Cancelled)), progress, installedExecutables);
        }
        catch (Exception exception)
        {
            InstallerLog.Write("InstallEngine.RunAsync", exception);
            return Fail(exception.Message, progress, installedExecutables);
        }
        finally
        {
            if (temporaryRoot != null)
                await DeleteTemporaryRootAsync(temporaryRoot).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Extracts every entry under <paramref name="destinationDirectory"/>, reporting one line per entry inside
    /// <paramref name="slice"/>. Entries that resolve outside the destination abort with InvalidDataException.
    /// Returns the number of files written.
    ///
    /// A stamped installer carries solid LZMA payloads; a development payload directory holds plain release
    /// zips. The two are told apart by the leading bytes rather than by the file name.
    /// </summary>
    internal static int ExtractArchive(
        Stream archiveStream,
        string destinationDirectory,
        string applicationName,
        ProgressSlice slice,
        IProgress<InstallProgressLine> progress,
        CancellationToken cancellationToken)
    {
        FrameworkCompatibility.ThrowIfNull(archiveStream, nameof(archiveStream));

        byte[] leadingBytes = new byte[SolidPayloadArchive.MagicLength];
        long start = archiveStream.Position;
        int read = archiveStream.Read(leadingBytes, offset: 0, leadingBytes.Length);
        archiveStream.Position = start;
        return SolidPayloadArchive.StartsWithMagic(leadingBytes, read)
            ? ExtractSolidPayload(archiveStream, destinationDirectory, applicationName, slice, progress, cancellationToken)
            : ExtractZipArchive(archiveStream, destinationDirectory, applicationName, slice, progress, cancellationToken);
    }

    /// <summary>Extracts a solid LZMA payload, reporting one line per entry as the stream decodes.</summary>
    private static int ExtractSolidPayload(
        Stream archiveStream,
        string destinationDirectory,
        string applicationName,
        ProgressSlice slice,
        IProgress<InstallProgressLine> progress,
        CancellationToken cancellationToken)
    {
        SolidPayloadHeader header = SolidPayloadArchive.Read(archiveStream);
        int entryCount = Math.Max(header.Entries.Count, 1);
        int extractedFiles = SolidPayloadArchive.Extract(
            archiveStream,
            header,
            destinationDirectory,
            (entryIndex, entry) => progress.Report(InstallProgressLine.At(
                slice.Map(entryIndex * FullInnerPercent / entryCount),
                Format(nameof(AppStrings.Installer_Progress_Extracting_Format), applicationName, entry.Name))),
            cancellationToken);

        progress.Report(InstallProgressLine.At(slice.EndPercent, Format(nameof(AppStrings.Installer_Progress_Extracted_Format), applicationName)));
        return extractedFiles;
    }

    private static int ExtractZipArchive(
        Stream archiveStream,
        string destinationDirectory,
        string applicationName,
        ProgressSlice slice,
        IProgress<InstallProgressLine> progress,
        CancellationToken cancellationToken)
    {
        string destinationRoot = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar);
        string destinationPrefix = destinationRoot + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(destinationRoot);

        using ZipArchive archive = new(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        int entryCount = archive.Entries.Count;
        int extractedFiles = 0;
        for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = archive.Entries[entryIndex];
            string entryPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            bool isDestinationRoot = string.Equals(entryPath, destinationRoot, StringComparison.OrdinalIgnoreCase);
            if (!isDestinationRoot && !entryPath.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The {applicationName} package contains an entry outside its destination: {entry.FullName}");
            }

            // Release packages ship a batch installer for people who download the zip directly. It is
            // redundant inside an installer, so it never reaches a temporary or a portable folder.
            if (IsRootInstallScript(entry)) continue;

            progress.Report(InstallProgressLine.At(
                slice.Map(entryIndex * FullInnerPercent / entryCount),
                Format(nameof(AppStrings.Installer_Progress_Extracting_Format), applicationName, entry.FullName)));

            if (isDestinationRoot || IsDirectoryEntry(entry))
            {
                Directory.CreateDirectory(entryPath);
                continue;
            }

            string? parentDirectory = Path.GetDirectoryName(entryPath);
            if (!string.IsNullOrEmpty(parentDirectory)) Directory.CreateDirectory(parentDirectory);
            entry.ExtractToFile(entryPath, overwrite: true);
            extractedFiles++;
        }

        progress.Report(InstallProgressLine.At(slice.EndPercent, Format(nameof(AppStrings.Installer_Progress_Extracted_Format), applicationName)));
        return extractedFiles;
    }

    /// <summary>True for the batch installer a release package carries at its root.</summary>
    internal static bool IsRootInstallScript(ZipArchiveEntry entry) => IsRootInstallScript(entry.FullName);

    /// <summary>
    /// True for the batch installer a release package carries at its root. A solid payload drops it while
    /// packing, so only a development payload zip ever reaches the extraction time check.
    /// </summary>
    internal static bool IsRootInstallScript(string entryName) =>
        string.Equals(entryName, InstallScriptFileName, StringComparison.OrdinalIgnoreCase);

    private static AppInstallResult InstallPortable(
        InstallPlan plan,
        EmbeddedPayload payload,
        Func<EmbeddedPayload, Stream> openPayload,
        ProgressSlice slice,
        IProgress<InstallProgressLine> progress,
        CancellationToken cancellationToken)
    {
        using Stream archive = openPayload(payload);
        ExtractArchive(archive, plan.TargetDirectory, payload.ApplicationName, slice, progress, cancellationToken);
        return new AppInstallResult(Error: null, InstalledExecutable: null);
    }

    private static async Task<AppInstallResult> InstallThroughAppAsync(
        InstallPlan plan,
        EmbeddedPayload payload,
        Func<EmbeddedPayload, Stream> openPayload,
        string temporaryRoot,
        ProgressSlice slice,
        IProgress<InstallProgressLine> progress,
        CancellationToken cancellationToken)
    {
        string applicationDirectory = Path.Combine(temporaryRoot, payload.ApplicationName);
        ProgressSlice extractSlice = slice.Portion(fromInnerPercent: 0, ExtractPortionEndPercent);
        ProgressSlice installSlice = slice.Portion(ExtractPortionEndPercent, FullInnerPercent);
        using (Stream archive = openPayload(payload))
        {
            ExtractArchive(archive, applicationDirectory, payload.ApplicationName, extractSlice, progress, cancellationToken);
        }

        string executableName = payload.ApplicationName + InstallDefaults.ExecutableExtension;
        string executablePath = Path.Combine(applicationDirectory, executableName);
        if (!File.Exists(executablePath))
        {
            return new AppInstallResult(
                Format(nameof(AppStrings.Installer_Error_PackageMissingExecutable_Format), payload.ApplicationName, executableName),
                InstalledExecutable: null);
        }

        progress.Report(InstallProgressLine.At(installSlice.StartPercent, Format(nameof(AppStrings.Installer_Progress_Installing_Format), payload.ApplicationName)));
        return await RunHeadlessInstallAsync(
            executablePath, applicationDirectory, plan, payload.ApplicationName, installSlice, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<AppInstallResult> RunHeadlessInstallAsync(
        string executablePath,
        string workingDirectory,
        InstallPlan plan,
        string applicationName,
        ProgressSlice slice,
        IProgress<InstallProgressLine> progress,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        // AddArgument stands in for ProcessStartInfo.ArgumentList, which the .NET Framework lacks
        startInfo.AddArgument(HeadlessInstallArgument);
        startInfo.AddArgument(plan.Mode == InstallMode.System ? SystemScope : LocalScope);
        startInfo.AddArgument(DesktopShortcutArgument);
        startInfo.AddArgument(plan.CreateDesktopShortcut ? TrueValue : FalseValue);
        startInfo.AddArgument(StartMenuShortcutArgument);
        startInfo.AddArgument(plan.CreateStartMenuShortcut ? TrueValue : FalseValue);
        InstallerLog.Write($"InstallEngine: starting {executablePath} {startInfo.Arguments}");

        using Process process = new();
        process.StartInfo = startInfo;
        if (!process.Start())
            return new AppInstallResult(Format(nameof(AppStrings.Installer_Error_AppStartFailed_Format), executablePath), InstalledExecutable: null);

        HeadlessOutput output = new(slice, progress);
        StreamReader standardOutput = process.StandardOutput;
        StreamReader standardError = process.StandardError;
        // Grandchildren may inherit the pipes, so the readers are never awaited to EOF; each gets its own
        // thread because a blocked pipe read would otherwise pin a thread pool thread indefinitely
        Task standardOutputTask = Task.Factory.StartNew(
            () => output.ReadStandardOutput(standardOutput),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Task standardErrorTask = Task.Factory.StartNew(
            () => output.ReadStandardError(standardError),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        await Task.WhenAny(
                Task.WhenAll(standardOutputTask, standardErrorTask),
                Task.Delay(OutputDrainTimeoutMs, CancellationToken.None))
            .ConfigureAwait(false);

        int exitCode = process.ExitCode;
        string diagnostics = output.Diagnostics;
        InstallerLog.Write($"InstallEngine: {applicationName} exited with {exitCode}; output: {diagnostics}");
        string? failureMessage = output.FailureMessage;
        if (failureMessage != null)
            return new AppInstallResult(WithDiagnostics(failureMessage, diagnostics), InstalledExecutable: null);
        if (exitCode != 0)
        {
            return new AppInstallResult(
                WithDiagnostics(Format(nameof(AppStrings.Installer_Error_AppInstallerExitCode_Format), applicationName, exitCode), diagnostics),
                InstalledExecutable: null);
        }

        return new AppInstallResult(Error: null, output.InstalledExecutable);
    }

    private static InstallOutcome Fail(
        string message,
        IProgress<InstallProgressLine> progress,
        IReadOnlyList<string> installedExecutables)
    {
        progress.Report(InstallProgressLine.Failed(message));
        return new InstallOutcome(Success: false, message, installedExecutables);
    }

    private static string WithDiagnostics(string headline, string diagnostics) =>
        diagnostics.Length == 0 ? headline : $"{headline}{Environment.NewLine}{diagnostics}";

    private static bool IsDirectoryEntry(ZipArchiveEntry entry) =>
        entry.FullName.Length > 0 && entry.FullName[^1] == ZipDirectorySeparator;

    private static string CreateTemporaryRoot()
    {
        string temporaryRoot = InstallerTempPaths.NewPayloadDirectory();
        Directory.CreateDirectory(temporaryRoot);
        return temporaryRoot;
    }

    /// <summary>
    /// Best-effort in-process deletion. A helper spawned by the app installer can still hold the staging
    /// folder as its working directory, so the last resort is a detached script that retries after it exits.
    /// </summary>
    private static async Task DeleteTemporaryRootAsync(string temporaryRoot)
    {
        for (int attempt = 1; attempt <= TemporaryDeleteAttempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(temporaryRoot)) return;

                Directory.Delete(temporaryRoot, recursive: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == TemporaryDeleteAttempts)
                {
                    InstallerLog.Write($"InstallEngine: could not delete {temporaryRoot}; deferring to a detached cleanup: {exception.Message}");
                    DetachedDirectoryCleanup.Schedule(temporaryRoot);
                    return;
                }

                await Task.Delay(TemporaryDeleteRetryDelayMs).ConfigureAwait(false);
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            // KillProcessTree stands in for Process.Kill(entireProcessTree: true), which the .NET Framework lacks
            if (!process.HasExited) FrameworkCompatibility.KillProcessTree(process);
        }
        // KillProcessTree starts taskkill, so the filter also covers what starting and waiting on a process
        // can throw. TryKill runs from a cancellation handler, and an escaping exception would replace the
        // cancellation the caller is reporting
        catch (Exception exception) when (exception is InvalidOperationException
                                             or Win32Exception
                                             or NotSupportedException
                                             or FileNotFoundException
                                             or ObjectDisposedException
                                             or IOException)
        {
            InstallerLog.Write("InstallEngine.TryKill", exception);
        }
    }

    /// <summary>Collects the child's output: progress lines are rescaled and forwarded, everything else is kept as diagnostics.</summary>
    private sealed class HeadlessOutput(ProgressSlice slice, IProgress<InstallProgressLine> progress)
    {
        // Stands in for System.Threading.Lock, which the .NET Framework does not define
        private readonly object _gate = new();
        private readonly StringBuilder _diagnostics = new();
        private string? _failureMessage;
        private string? _installedExecutable;

        public string? FailureMessage
        {
            get
            {
                lock (_gate) return _failureMessage;
            }
        }

        public string? InstalledExecutable
        {
            get
            {
                lock (_gate) return _installedExecutable;
            }
        }

        public string Diagnostics
        {
            get
            {
                lock (_gate) return _diagnostics.ToString().Trim();
            }
        }

        public void ReadStandardOutput(StreamReader reader)
        {
            try
            {
                string? line;
                while ((line = reader.ReadLine()) != null) HandleStandardOutputLine(line);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                InstallerLog.Write("InstallEngine: standard output reader stopped", exception);
            }
        }

        public void ReadStandardError(StreamReader reader)
        {
            try
            {
                string? line;
                while ((line = reader.ReadLine()) != null) AppendDiagnostic(line);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                InstallerLog.Write("InstallEngine: standard error reader stopped", exception);
            }
        }

        private void HandleStandardOutputLine(string line)
        {
            if (InstallProgressLine.TryParseLine(line, out InstallProgressLine? parsed))
            {
                if (parsed.IsFailure)
                {
                    lock (_gate) _failureMessage = parsed.Message;
                    return;
                }

                progress.Report(InstallProgressLine.At(slice.Map(parsed.Percent), parsed.Message));
                return;
            }

            if (line.StartsWith(InstalledToPrefix, StringComparison.Ordinal))
            {
                string installedPath = line[InstalledToPrefix.Length..].Trim();
                lock (_gate) _installedExecutable = installedPath.Length == 0 ? null : installedPath;
            }

            AppendDiagnostic(line);
        }

        private void AppendDiagnostic(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            lock (_gate) _diagnostics.AppendLine(line.Trim());
        }
    }
    private static string L(string key) => LocalizationManager.Instance[key];

    private static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, L(key), arguments);
}
