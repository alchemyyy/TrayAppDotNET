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
    // A portable install only copies after extracting, which is the bulk of its time
    private const int PortableExtractPortionEndPercent = 80;
    private const string SearchAllPattern = "*";
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

    /// <summary>How one application's run ended: not started, failed (no executable) or installed.</summary>
    private sealed record ApplicationRunResult(bool Started, string? InstalledExecutable)
    {
        public static ApplicationRunResult NotStarted { get; } = new(Started: false, InstalledExecutable: null);
    }

    /// <summary>State shared by the applications of one run.</summary>
    private sealed class ParallelRun(
        InstallPlan plan,
        Func<EmbeddedPayload, Stream> openPayload,
        string temporaryRoot,
        InstallProgressAggregator aggregator,
        IProgress<InstallApplicationStatus> applicationProgress,
        int maximumConcurrentApplications,
        CancellationToken cancellationToken) : IDisposable
    {
        // Stands in for System.Threading.Lock, which the .NET Framework does not define
        private readonly object _gate = new();
        // Cancelled by the first failure or by the caller; running applications only follow the caller's token
        private readonly CancellationTokenSource _stopQueued = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        private string? _firstFailureMessage;

        public InstallPlan Plan { get; } = plan;
        public Func<EmbeddedPayload, Stream> OpenPayload { get; } = openPayload;
        public string TemporaryRoot { get; } = temporaryRoot;
        public InstallProgressAggregator Aggregator { get; } = aggregator;
        public IProgress<InstallApplicationStatus> ApplicationProgress { get; } = applicationProgress;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public SemaphoreSlim Slots { get; } = new(maximumConcurrentApplications, maximumConcurrentApplications);
        public SemaphoreSlim PortableCopyGate { get; } = new(initialCount: 1, maxCount: 1);
        public CancellationToken StopQueuedToken => _stopQueued.Token;

        /// <summary>The message of the failure that happened first, which the run reports as its outcome.</summary>
        public string? FirstFailureMessage
        {
            get
            {
                lock (_gate) return _firstFailureMessage;
            }
        }

        public void RecordFailure(string message)
        {
            lock (_gate) _firstFailureMessage ??= message;

            _stopQueued.Cancel();
        }

        public void Dispose()
        {
            _stopQueued.Dispose();
            Slots.Dispose();
            PortableCopyGate.Dispose();
        }
    }

    public static Task<InstallOutcome> RunAsync(
        InstallPlan plan,
        IProgress<InstallProgressLine> progress,
        IProgress<InstallApplicationStatus> applicationProgress,
        CancellationToken cancellationToken) =>
        RunAsync(
            plan,
            EmbeddedPayloadCatalog.Load().OpenPayload,
            progress,
            applicationProgress,
            DefaultMaximumConcurrentApplications,
            cancellationToken);

    /// <summary>
    /// Every selected application installs at once, up to one per processor: extraction decodes each
    /// application's LZMA stream on its own thread, and the rest is the app's own installer process.
    /// </summary>
    public static int DefaultMaximumConcurrentApplications => Math.Max(Environment.ProcessorCount, 1);

    /// <summary>
    /// Runs the plan with up to <paramref name="maximumConcurrentApplications"/> applications installing at once,
    /// reporting each application's Installing, Installed or Failed state to <paramref name="applicationProgress"/>.
    /// The first failure stops queued applications from starting, while the ones already running finish,
    /// because killing an app's installer mid-copy would leave that app half installed.
    /// <paramref name="openPayload"/> lets tests substitute on-disk zips.
    /// </summary>
    internal static async Task<InstallOutcome> RunAsync(
        InstallPlan plan,
        Func<EmbeddedPayload, Stream> openPayload,
        IProgress<InstallProgressLine> progress,
        IProgress<InstallApplicationStatus> applicationProgress,
        int maximumConcurrentApplications,
        CancellationToken cancellationToken)
    {
        FrameworkCompatibility.ThrowIfNull(plan, nameof(plan));
        FrameworkCompatibility.ThrowIfNull(openPayload, nameof(openPayload));
        FrameworkCompatibility.ThrowIfNull(progress, nameof(progress));
        FrameworkCompatibility.ThrowIfNull(applicationProgress, nameof(applicationProgress));
        if (maximumConcurrentApplications <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumConcurrentApplications), maximumConcurrentApplications, message: "At least one application must be allowed to run.");
        }

        List<string> installedExecutables = [];
        if (plan.Payloads.Count == 0)
        {
            return Fail(
                L(nameof(AppStrings.Installer_Progress_NoPayloads)),
                failedApplicationName: null,
                progress,
                applicationProgress,
                installedExecutables);
        }

        string? temporaryRoot = null;
        try
        {
            temporaryRoot = CreateTemporaryRoot();
            int applicationCount = plan.Payloads.Count;
            using ParallelRun run = new(
                plan,
                openPayload,
                temporaryRoot,
                new InstallProgressAggregator(applicationCount, progress),
                applicationProgress,
                Math.Min(maximumConcurrentApplications, applicationCount),
                cancellationToken);

            Task<ApplicationRunResult>[] applicationRuns = new Task<ApplicationRunResult>[applicationCount];
            for (int index = 0; index < applicationCount; index++)
            {
                // Slots are taken here rather than inside each run, so queued applications start in plan order
                try
                {
                    await run.Slots.WaitAsync(run.StopQueuedToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Stopped before it started, so it is not blamed and stays not installed
                    applicationRuns[index] = Task.FromResult(ApplicationRunResult.NotStarted);
                    continue;
                }

                int applicationIndex = index;
                // Extraction is synchronous, so each application starts on the thread pool rather than this thread
                applicationRuns[index] = Task.Run(() => RunApplicationAsync(run, applicationIndex), CancellationToken.None);
            }

            ApplicationRunResult[] results = await Task.WhenAll(applicationRuns).ConfigureAwait(false);
            bool anyNotStarted = false;
            foreach (ApplicationRunResult result in results)
            {
                if (result.InstalledExecutable != null) installedExecutables.Add(result.InstalledExecutable);
                anyNotStarted |= !result.Started;
            }

            // Only a failure or a cancellation keeps an application from starting
            string? failureMessage = run.FirstFailureMessage
                                     ?? (anyNotStarted ? L(nameof(AppStrings.Installer_Progress_Cancelled)) : null);
            if (failureMessage != null)
                return Fail(failureMessage, failedApplicationName: null, progress, applicationProgress, installedExecutables);

            progress.Report(InstallProgressLine.At(InstallProgressLine.CompletePercent, L(nameof(AppStrings.Installer_Progress_Complete))));
            return new InstallOutcome(Success: true, ErrorMessage: null, installedExecutables);
        }
        catch (Exception exception)
        {
            // Every application handles its own failures, so this is the shared setup failing before any started
            InstallerLog.Write("InstallEngine.RunAsync", exception);
            return Fail(exception.Message, failedApplicationName: null, progress, applicationProgress, installedExecutables);
        }
        finally
        {
            if (temporaryRoot != null)
                await DeleteTemporaryRootAsync(temporaryRoot).ConfigureAwait(false);
        }
    }

    /// <summary>Installs one application in the slot the caller took for it, settling its state before returning. Never throws.</summary>
    private static async Task<ApplicationRunResult> RunApplicationAsync(ParallelRun run, int index)
    {
        EmbeddedPayload payload = run.Plan.Payloads[index];
        try
        {
            // A failure can land between taking the slot and starting
            if (run.StopQueuedToken.IsCancellationRequested) return ApplicationRunResult.NotStarted;

            run.ApplicationProgress.Report(new InstallApplicationStatus(payload.ApplicationName, InstallApplicationState.Installing));
            IProgress<InstallProgressLine> progress = run.Aggregator.ForApplication(index);
            AppInstallResult result;
            switch (run.Plan.Mode)
            {
                case InstallMode.Portable:
                    result = await InstallPortableAsync(run, payload, progress).ConfigureAwait(false);
                    break;
                case InstallMode.Local:
                case InstallMode.System:
                    result = await InstallThroughAppAsync(
                            run.Plan, payload, run.OpenPayload, run.TemporaryRoot, ProgressSlice.Full, progress, run.CancellationToken)
                        .ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(run), run.Plan.Mode, message: "Unsupported install mode.");
            }

            if (result.Error != null) return FailApplication(run, payload, result.Error);

            string installedExecutable = result.InstalledExecutable
                                         ?? InstallDefaults.InstalledExecutablePath(
                                             run.Plan.Mode, run.Plan.TargetDirectory, payload.ApplicationName);
            run.ApplicationProgress.Report(new InstallApplicationStatus(payload.ApplicationName, InstallApplicationState.Installed));
            return new ApplicationRunResult(Started: true, installedExecutable);
        }
        catch (OperationCanceledException)
        {
            return FailApplication(run, payload, L(nameof(AppStrings.Installer_Progress_Cancelled)));
        }
        catch (Exception exception)
        {
            InstallerLog.Write($"InstallEngine: {payload.ApplicationName}", exception);
            return FailApplication(run, payload, exception.Message);
        }
        finally
        {
            run.Slots.Release();
        }
    }

    private static ApplicationRunResult FailApplication(ParallelRun run, EmbeddedPayload payload, string message)
    {
        InstallerLog.Write($"InstallEngine: {payload.ApplicationName} failed: {message}");
        run.RecordFailure(message);
        run.ApplicationProgress.Report(new InstallApplicationStatus(payload.ApplicationName, InstallApplicationState.Failed));
        return new ApplicationRunResult(Started: true, InstalledExecutable: null);
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

    /// <summary>
    /// Extracts into the staging folder, which runs alongside the other applications, then copies into the
    /// portable folder one application at a time, because every package carries the same shared files.
    /// </summary>
    private static async Task<AppInstallResult> InstallPortableAsync(
        ParallelRun run,
        EmbeddedPayload payload,
        IProgress<InstallProgressLine> progress)
    {
        string stagingDirectory = Path.Combine(run.TemporaryRoot, payload.ApplicationName);
        ProgressSlice extractSlice = ProgressSlice.Full.Portion(fromInnerPercent: 0, PortableExtractPortionEndPercent);
        using (Stream archive = run.OpenPayload(payload))
        {
            ExtractArchive(archive, stagingDirectory, payload.ApplicationName, extractSlice, progress, run.CancellationToken);
        }

        await run.PortableCopyGate.WaitAsync(run.CancellationToken).ConfigureAwait(false);
        try
        {
            progress.Report(InstallProgressLine.At(extractSlice.EndPercent, Format(nameof(AppStrings.Installer_Progress_Installing_Format), payload.ApplicationName)));
            CopyDirectory(stagingDirectory, run.Plan.TargetDirectory, run.CancellationToken);
        }
        finally
        {
            run.PortableCopyGate.Release();
        }

        progress.Report(InstallProgressLine.At(FullInnerPercent, Format(nameof(AppStrings.Installer_Progress_Installed_Format), payload.ApplicationName)));
        return new AppInstallResult(Error: null, InstalledExecutable: null);
    }

    /// <summary>Copies every directory and file under <paramref name="sourceDirectory"/> into <paramref name="destinationDirectory"/>, overwriting.</summary>
    private static void CopyDirectory(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
    {
        string sourceRoot = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar);
        Directory.CreateDirectory(destinationDirectory);
        // Path.GetRelativePath does not exist on the .NET Framework; every enumerated path starts with the source root
        foreach (string directory in Directory.EnumerateDirectories(sourceRoot, SearchAllPattern, SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destinationDirectory, directory[(sourceRoot.Length + 1)..]));

        foreach (string file in Directory.EnumerateFiles(sourceRoot, SearchAllPattern, SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(destinationDirectory, file[(sourceRoot.Length + 1)..]), overwrite: true);
        }
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

    /// <summary>
    /// Marks the application the run stopped on as failed, when there is one, then reports the terminal failure line.
    /// Example mode ends its simulated runs through this too, so both report failures identically.
    /// </summary>
    internal static InstallOutcome Fail(
        string message,
        string? failedApplicationName,
        IProgress<InstallProgressLine> progress,
        IProgress<InstallApplicationStatus> applicationProgress,
        IReadOnlyList<string> installedExecutables)
    {
        if (failedApplicationName != null)
            applicationProgress.Report(new InstallApplicationStatus(failedApplicationName, InstallApplicationState.Failed));

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
