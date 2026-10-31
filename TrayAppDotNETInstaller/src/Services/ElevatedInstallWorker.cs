using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using TrayAppDotNETInstaller.Localization;

namespace TrayAppDotNETInstaller.Services;

/// <summary>
/// The plan an elevated worker process receives on its command line. Only System installs need one. A null
/// shortcut choice leaves that shortcut as it is.
/// </summary>
public sealed record WorkerArguments(
    string PipeName,
    bool? CreateDesktopShortcut,
    bool? CreateStartMenuShortcut,
    IReadOnlyList<string> ApplicationNames);

/// <summary>
/// System installs run the engine in a second, elevated copy of this executable.
/// The UI hosts a named pipe, starts the worker with the runas verb, and relays the progress lines, application
/// status lines and log lines the worker writes to the pipe. The UI writes one byte back to cancel the run.
/// </summary>
public static class ElevatedInstallWorker
{
    public const string WorkerArgument = "--worker";
    internal const string LogLinePrefix = "[LOG] ";
    private const string PipeArgument = "--pipe";
    private const string AppsArgument = "--apps";
    private const string TrueValue = "true";
    private const string FalseValue = "false";
    private const char AppsSeparator = ',';
    // The .NET Framework string.Join has no char overload, so the same separator is also kept as text
    private const string AppsSeparatorText = ",";
    private const string PipeNamePrefix = "TrayAppDotNETInstaller.";
    private const string RunAsVerb = "runas";
    private const string LocalServerName = ".";
    private const byte CancelMessage = 1;
    private const int WindowsErrorCancelled = 1223;
    private const int ConnectTimeoutMs = 10_000;
    private const int ConnectionWaitTimeoutMs = 30_000;
    private const int SuccessExitCode = 0;
    private const int FailureExitCode = 1;
    private const int UsageExitCode = 2;
    // The named pipe buffer sizes the system picks when the caller asks for none
    private const int DefaultPipeBufferSize = 0;

    public static bool IsWorkerInvocation(string[] args)
    {
        FrameworkCompatibility.ThrowIfNull(args, nameof(args));

        foreach (string argument in args)
        {
            if (string.Equals(argument, WorkerArgument, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    public static string[] BuildWorkerArguments(WorkerArguments arguments)
    {
        FrameworkCompatibility.ThrowIfNull(arguments, nameof(arguments));

        List<string> result = [WorkerArgument, PipeArgument, arguments.PipeName];
        if (arguments.CreateDesktopShortcut is { } desktop)
        {
            result.Add(TrayAppDotNETInstallContract.DesktopShortcutArgument);
            result.Add(desktop ? TrueValue : FalseValue);
        }

        if (arguments.CreateStartMenuShortcut is { } startMenu)
        {
            result.Add(TrayAppDotNETInstallContract.StartMenuShortcutArgument);
            result.Add(startMenu ? TrueValue : FalseValue);
        }

        result.Add(AppsArgument);
        result.Add(string.Join(AppsSeparatorText, arguments.ApplicationNames));
        return [.. result];
    }

    public static bool TryParseWorkerArguments(
        string[] args,
        [NotNullWhen(true)] out WorkerArguments? arguments,
        [NotNullWhen(false)] out string? error)
    {
        FrameworkCompatibility.ThrowIfNull(args, nameof(args));

        arguments = null;
        error = null;
        string? pipeName = TryGetArgumentValue(args, PipeArgument);
        // The FrameworkCompatibility guard restores the nullable flow the unannotated framework method loses
        if (FrameworkCompatibility.IsNullOrWhiteSpace(pipeName))
        {
            error = $"{PipeArgument} <name> is required.";
            return false;
        }

        if (!TryParseOptionalBoolean(args, TrayAppDotNETInstallContract.DesktopShortcutArgument, out bool? createDesktopShortcut, out error)
            || !TryParseOptionalBoolean(args, TrayAppDotNETInstallContract.StartMenuShortcutArgument, out bool? createStartMenuShortcut, out error))
            return false;

        string? appsValue = TryGetArgumentValue(args, AppsArgument);
        // SplitTrimmed stands in for Split(char, RemoveEmptyEntries | TrimEntries); the .NET Framework has
        // neither the char overload nor TrimEntries
        List<string> applicationNames = appsValue == null
            ? []
            : FrameworkCompatibility.SplitTrimmed(appsValue, AppsSeparator);

        if (applicationNames.Count == 0)
        {
            error = $"{AppsArgument} must list at least one application.";
            return false;
        }

        arguments = new WorkerArguments(pipeName, createDesktopShortcut, createStartMenuShortcut, applicationNames);
        return true;
    }

    /// <summary>Worker process entry point: connects to the UI's pipe and runs the engine.</summary>
    public static int Run(string[] args)
    {
        // The log folder is the user's; the lines travel to the unelevated window once the pipe is up
        InstallerLog.BeginBuffering();
        if (!TryParseWorkerArguments(args, out WorkerArguments? arguments, out string? error))
        {
            InstallerLog.Write($"ElevatedInstallWorker.Run: {error}");
            return UsageExitCode;
        }

        InstallerLog.Write($"ElevatedInstallWorker.Run: apps {string.Join(AppsSeparatorText, arguments.ApplicationNames)}");
        try
        {
            return RunWorkerAsync(
                    arguments,
                    (progress, applicationProgress, cancellationToken) =>
                        RunInstallAsync(arguments, progress, applicationProgress, cancellationToken),
                    redirectLog: true)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
        {
            InstallerLog.Write("ElevatedInstallWorker.Run", exception);
            return FailureExitCode;
        }
    }

    /// <summary>
    /// Pipe transport of the worker; <paramref name="runInstall"/> is injected so tests can drive it without payloads.
    /// It receives one writer for progress lines, application statuses and log lines, so they keep their relative
    /// order on the pipe, and a token the UI cancels by writing a byte or by going away.
    /// </summary>
    internal static async Task<int> RunWorkerAsync(
        WorkerArguments arguments,
        Func<IProgress<TrayAppDotNETInstallProgress>, IProgress<InstallApplicationStatus>, CancellationToken, Task<InstallOutcome>> runInstall,
        bool redirectLog = false)
    {
        // The .NET Framework has no IAsyncDisposable, so these are plain using declarations; disposal order
        // is unchanged, the writer closing before the pipe.
        // Anonymous impersonation stands in for the half of PipeOptions.CurrentUserOnly that protects the
        // client: without it the framework lets the pipe's owner impersonate this worker, which runs elevated.
        using NamedPipeClientStream pipe = new(
            LocalServerName,
            arguments.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Anonymous);
        await pipe.ConnectAsync(ConnectTimeoutMs).ConfigureAwait(false);
        VerifyPipeOwnerIsCurrentUser(pipe, arguments.PipeName);

        using StreamWriter writer = new(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.AutoFlush = true;
        PipeProgress progress = new(writer);
        if (redirectLog) InstallerLog.RedirectTo(progress.WriteLog);
        using CancellationTokenSource cancellation = new();
        _ = WatchForCancelAsync(pipe, cancellation);
        InstallOutcome outcome = await runInstall(progress, progress, cancellation.Token).ConfigureAwait(false);
        progress.EnsureTerminalLine(outcome);
        return outcome.Success ? SuccessExitCode : FailureExitCode;
    }

    /// <summary>Cancels the run once the UI writes a byte or closes its end of the pipe.</summary>
    private static async Task WatchForCancelAsync(Stream pipe, CancellationTokenSource cancellation)
    {
        try
        {
            byte[] buffer = new byte[1];
            _ = await pipe.ReadAsync(buffer, offset: 0, count: 1).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // A broken pipe means the window is gone, which cancels the run as well
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run already finished
        }
    }

    /// <summary>
    /// UI side: starts the elevated worker and relays its progress, application statuses and log lines.
    /// Runs in-process when already elevated. Cancelling <paramref name="cancellationToken"/> asks the worker to
    /// stop, and the call still waits for the worker to report how far it got.
    /// </summary>
    public static async Task<InstallOutcome> RunElevatedAsync(
        InstallPlan plan,
        IProgress<TrayAppDotNETInstallProgress> progress,
        IProgress<InstallApplicationStatus> applicationProgress,
        CancellationToken cancellationToken)
    {
        FrameworkCompatibility.ThrowIfNull(plan, nameof(plan));
        FrameworkCompatibility.ThrowIfNull(progress, nameof(progress));
        FrameworkCompatibility.ThrowIfNull(applicationProgress, nameof(applicationProgress));

        if (SystemProbes.IsElevated())
            return await InstallEngine.RunAsync(plan, progress, applicationProgress, cancellationToken).ConfigureAwait(false);

        // The one over-the-shoulder policy shared with the apps: elevated work runs only as the signed-in user
        if (TrayAppDotNETElevationPolicy.ElevationWouldUseAnotherAccount(InstallerLog.Write))
            return Fail(L(nameof(AppStrings.Installer_Error_DifferentAdministratorAccount)), progress);

        string? executablePath = FrameworkCompatibility.ProcessPath;
        if (string.IsNullOrEmpty(executablePath))
            return Fail(L(nameof(AppStrings.Installer_Error_WorkerPathUnknown)), progress);

        List<string> applicationNames = [];
        foreach (EmbeddedPayload payload in plan.Payloads) applicationNames.Add(payload.ApplicationName);

        string pipeName = $"{PipeNamePrefix}{FrameworkCompatibility.ProcessID}.{Guid.NewGuid():N}";
        string[] workerArguments = BuildWorkerArguments(new WorkerArguments(
            pipeName,
            plan.CreateDesktopShortcut,
            plan.CreateStartMenuShortcut,
            applicationNames));

        try
        {
            using NamedPipeServerStream server = CreateProgressPipeServer(pipeName);

            ProcessStartInfo startInfo = new()
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
                UseShellExecute = true,
                Verb = RunAsVerb,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            // AddArgument stands in for ProcessStartInfo.ArgumentList, which the .NET Framework lacks
            foreach (string argument in workerArguments) startInfo.AddArgument(argument);

            Process? worker;
            try
            {
                worker = Process.Start(startInfo);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == WindowsErrorCancelled)
            {
                InstallerLog.Write("ElevatedInstallWorker: UAC prompt declined");
                return Fail(L(nameof(AppStrings.Installer_Progress_UacDeclined)), progress);
            }

            if (worker == null) return Fail(L(nameof(AppStrings.Installer_Error_WorkerStartFailed)), progress);

            using (worker)
            {
                InstallerLog.Write($"ElevatedInstallWorker: started worker PID {worker.Id}");
                return await RelayWorkerAsync(server, worker, plan, progress, applicationProgress, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            InstallerLog.Write("ElevatedInstallWorker.RunElevatedAsync", exception);
            return Fail(exception.Message, progress);
        }
    }

    private static async Task<InstallOutcome> RelayWorkerAsync(
        NamedPipeServerStream server,
        Process worker,
        InstallPlan plan,
        IProgress<TrayAppDotNETInstallProgress> progress,
        IProgress<InstallApplicationStatus> applicationProgress,
        CancellationToken cancellationToken)
    {
        Task workerExitTask = worker.WaitForExitAsync(CancellationToken.None);
        Task connectTask = server.WaitForConnectionAsync(CancellationToken.None);
        Task connectionTimeoutTask = Task.Delay(ConnectionWaitTimeoutMs, CancellationToken.None);
        Task firstTask = await Task.WhenAny(connectTask, workerExitTask, connectionTimeoutTask).ConfigureAwait(false);
        if (ReferenceEquals(firstTask, workerExitTask))
        {
            await workerExitTask.ConfigureAwait(false);
            return Fail(Format(nameof(AppStrings.Installer_Error_WorkerExitedEarly_Format), worker.ExitCode), progress);
        }

        if (ReferenceEquals(firstTask, connectionTimeoutTask))
        {
            TryKill(worker);
            return Fail(L(nameof(AppStrings.Installer_Error_WorkerNotConnected)), progress);
        }

        await connectTask.ConfigureAwait(false);

        // The worker cannot be killed from here (it runs elevated), so cancelling asks it to stop and the relay
        // keeps reading until it has reported where the run ended
        using CancellationTokenRegistration cancelRegistration = cancellationToken.Register(() => RequestCancel(server));
        string? failureMessage = null;
        HashSet<string> installedApplications = new(StringComparer.OrdinalIgnoreCase);
        using (StreamReader reader = new(server, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
        {
            string? line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                TrayAppDotNETInstallProgress? relayed = RelayWorkerLine(line, progress, applicationProgress, installedApplications);
                if (relayed?.IsFailure == true) failureMessage = relayed.Message;
            }
        }

        await workerExitTask.ConfigureAwait(false);
        int exitCode = worker.ExitCode;
        InstallerLog.Write($"ElevatedInstallWorker: worker exited with {exitCode}");

        // The apps that finished are relaunched by the window whether or not the run as a whole succeeded
        List<string> installedExecutables = [];
        foreach (EmbeddedPayload payload in plan.Payloads)
        {
            if (!installedApplications.Contains(payload.ApplicationName)) continue;
            installedExecutables.Add(InstallDefaults.InstalledExecutablePath(
                plan.Mode, plan.TargetDirectory, payload.ApplicationName));
        }

        // The worker already sent the [FAIL] line, so it is not reported a second time
        if (failureMessage != null) return new InstallOutcome(Success: false, failureMessage, installedExecutables);
        if (exitCode != SuccessExitCode)
        {
            string message = Format(nameof(AppStrings.Installer_Error_WorkerExitCode_Format), exitCode);
            progress.Report(TrayAppDotNETInstallProgress.Failed(message));
            return new InstallOutcome(Success: false, message, installedExecutables);
        }

        return new InstallOutcome(Success: true, ErrorMessage: null, installedExecutables);
    }

    private static void RequestCancel(NamedPipeServerStream server)
    {
        try
        {
            server.WriteByte(CancelMessage);
            server.Flush();
            InstallerLog.Write("ElevatedInstallWorker: asked the worker to cancel");
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            InstallerLog.Write("ElevatedInstallWorker: could not ask the worker to cancel", exception);
        }
    }

    /// <summary>
    /// Forwards one line the worker wrote: a log line to the log, an application status to
    /// <paramref name="applicationProgress"/> (remembering installed applications in
    /// <paramref name="installedApplications"/>), and a progress line to <paramref name="progress"/>.
    /// Returns the progress line so the caller can see a failure, or null for anything else.
    /// </summary>
    internal static TrayAppDotNETInstallProgress? RelayWorkerLine(
        string line,
        IProgress<TrayAppDotNETInstallProgress> progress,
        IProgress<InstallApplicationStatus> applicationProgress,
        ISet<string>? installedApplications = null)
    {
        if (line.StartsWith(LogLinePrefix, StringComparison.Ordinal))
        {
            InstallerLog.Write($"worker: {line.Substring(LogLinePrefix.Length)}");
            return null;
        }

        if (InstallApplicationStatus.TryParseLine(line, out InstallApplicationStatus? status))
        {
            if (status.State == InstallApplicationState.Installed) installedApplications?.Add(status.ApplicationName);
            applicationProgress.Report(status);
            return null;
        }

        if (TrayAppDotNETInstallProgress.TryParseLine(line, out TrayAppDotNETInstallProgress? parsed))
        {
            progress.Report(parsed);
            return parsed;
        }

        InstallerLog.Write($"ElevatedInstallWorker: unrecognized worker line: {line}");
        return null;
    }

    private static Task<InstallOutcome> RunInstallAsync(
        WorkerArguments arguments,
        IProgress<TrayAppDotNETInstallProgress> progress,
        IProgress<InstallApplicationStatus> applicationProgress,
        CancellationToken cancellationToken)
    {
        EmbeddedPayloadCatalog catalog = EmbeddedPayloadCatalog.Load();
        List<EmbeddedPayload> payloads = [];
        foreach (string applicationName in arguments.ApplicationNames)
        {
            EmbeddedPayload? payload = catalog.Find(applicationName);
            if (payload == null)
            {
                // Nothing has been installed yet, and this application is the one that stopped the run
                string message = Format(nameof(AppStrings.Installer_Error_PayloadMissing_Format), applicationName);
                applicationProgress.Report(new InstallApplicationStatus(applicationName, InstallApplicationState.Failed));
                progress.Report(TrayAppDotNETInstallProgress.Failed(message));
                return Task.FromResult(new InstallOutcome(Success: false, message, []));
            }

            payloads.Add(payload);
        }

        InstallPlan plan = new(
            InstallMode.System,
            InstallDefaults.DefaultDirectory(InstallMode.System, catalog),
            payloads,
            arguments.CreateDesktopShortcut,
            arguments.CreateStartMenuShortcut);
        return InstallEngine.RunAsync(plan, progress, applicationProgress, cancellationToken);
    }

    /// <summary>
    /// Verifies that the pipe this worker just connected to belongs to the user who started it. This is the
    /// client half of PipeOptions.CurrentUserOnly, which the .NET Framework does not define: without it an
    /// elevated worker would write its progress to whatever process happened to own the name.
    /// </summary>
    private static void VerifyPipeOwnerIsCurrentUser(NamedPipeClientStream pipe, string pipeName)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier? currentUser = identity.User;
        IdentityReference? owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        if (currentUser != null && currentUser.Equals(owner)) return;

        throw new UnauthorizedAccessException(
            $"The progress pipe {pipeName} is owned by {owner?.Value ?? "an unknown account"} rather than the current user.");
    }

    /// <summary>
    /// Stands in for the server half of PipeOptions.CurrentUserOnly, which the .NET Framework does not
    /// define. The pipe carries elevated install progress, so the descriptor starts from an empty DACL,
    /// which grants nobody anything, and then allows exactly one account: the user that created it. An
    /// explicit deny entry is deliberately avoided because a deny entry for Everyone would also match the
    /// owner and shut the pipe down entirely. Under Admin Approval Mode the elevated worker carries the same
    /// user SID and still passes; over-the-shoulder elevation with a different account does not, which is
    /// the same behaviour PipeOptions.CurrentUserOnly has.
    /// </summary>
    private static PipeSecurity CreateCurrentUserOnlyPipeSecurity()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier currentUser = identity.User
                                         ?? throw new InvalidOperationException("The current Windows identity carries no user SID.");
        PipeSecurity security = new();
        security.SetOwner(currentUser);
        security.AddAccessRule(new PipeAccessRule(currentUser, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    /// <summary>
    /// Creates the UI's end of the progress pipe with the current-user-only descriptor and the current user as
    /// its explicit owner. Without the explicit owner an elevated UI would create a pipe owned by
    /// BUILTIN\Administrators, which the worker's owner check rightly refuses; with it the check holds whether
    /// or not the UI runs elevated.
    /// </summary>
    internal static NamedPipeServerStream CreateProgressPipeServer(string pipeName) =>
        new(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            DefaultPipeBufferSize,
            DefaultPipeBufferSize,
            CreateCurrentUserOnlyPipeSecurity());

    private static InstallOutcome Fail(string message, IProgress<TrayAppDotNETInstallProgress> progress)
    {
        progress.Report(TrayAppDotNETInstallProgress.Failed(message));
        return new InstallOutcome(Success: false, message, []);
    }

    private static bool TryParseOptionalBoolean(
        string[] args,
        string name,
        out bool? value,
        [NotNullWhen(false)] out string? error)
    {
        value = null;
        error = null;
        if (!args.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase))) return true;

        switch (TryGetArgumentValue(args, name)?.ToLowerInvariant())
        {
            case TrueValue:
                value = true;
                return true;
            case FalseValue:
                value = false;
                return true;
            default:
                error = $"{name} must be {TrueValue} or {FalseValue}.";
                return false;
        }
    }

    private static string? TryGetArgumentValue(string[] args, string name)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill();
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            InstallerLog.Write("ElevatedInstallWorker.TryKill", exception);
        }
    }

    /// <summary>
    /// Writes progress lines, application status lines and log lines to the pipe.
    /// The engine reports from several threads, and one gate keeps every kind in the order it was reported.
    /// </summary>
    private sealed class PipeProgress(TextWriter writer) : IProgress<TrayAppDotNETInstallProgress>, IProgress<InstallApplicationStatus>
    {
        // Stands in for System.Threading.Lock, which the .NET Framework does not define
        private readonly object _gate = new();
        private bool _terminalLineWritten;

        public void Report(TrayAppDotNETInstallProgress value)
        {
            FrameworkCompatibility.ThrowIfNull(value, nameof(value));

            WriteLine(value.ToLine(), isTerminal: value.IsFailure || value.IsComplete);
        }

        public void Report(InstallApplicationStatus value)
        {
            FrameworkCompatibility.ThrowIfNull(value, nameof(value));

            WriteLine(value.ToLine(), isTerminal: false);
        }

        public void WriteLog(string line) =>
            WriteLine(LogLinePrefix + TrayAppDotNETInstallProgress.SingleLine(line), isTerminal: false);

        /// <summary>Guarantees the UI sees a terminal line even when the engine returned without one.</summary>
        public void EnsureTerminalLine(InstallOutcome outcome)
        {
            lock (_gate)
            {
                if (_terminalLineWritten) return;
            }

            Report(outcome.Success
                ? TrayAppDotNETInstallProgress.At(TrayAppDotNETInstallProgress.CompletePercent, L(nameof(AppStrings.Installer_Progress_Complete)))
                : TrayAppDotNETInstallProgress.Failed(outcome.ErrorMessage ?? L(nameof(AppStrings.Installer_Status_Failed))));
        }

        private void WriteLine(string line, bool isTerminal)
        {
            lock (_gate)
            {
                try
                {
                    writer.WriteLine(line);
                    if (isTerminal) _terminalLineWritten = true;
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                    // The log is redirected here, so a failed write only reaches the debugger
                    Debug.WriteLine($"ElevatedInstallWorker: pipe write failed: {exception.Message}");
                }
            }
        }
    }

    private static string L(string key) => LocalizationManager.Instance[key];

    private static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, L(key), arguments);
}
