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

/// <summary>The plan an elevated worker process receives on its command line.</summary>
public sealed record WorkerArguments(
    string PipeName,
    InstallMode Mode,
    bool CreateDesktopShortcut,
    bool CreateStartMenuShortcut,
    IReadOnlyList<string> ApplicationNames);

/// <summary>
/// System installs run the engine in a second, elevated copy of this executable. The UI hosts a named pipe,
/// starts the worker with the runas verb, and relays the progress lines the worker writes to the pipe.
/// </summary>
public static class ElevatedInstallWorker
{
    public const string WorkerArgument = "--worker";
    private const string PipeArgument = "--pipe";
    private const string ModeArgument = "--mode";
    private const string DesktopShortcutArgument = "--desktop-shortcut";
    private const string StartMenuShortcutArgument = "--start-menu-shortcut";
    private const string AppsArgument = "--apps";
    private const string LocalModeValue = "local";
    private const string SystemModeValue = "system";
    private const string PortableModeValue = "portable";
    private const string TrueValue = "true";
    private const string FalseValue = "false";
    private const char AppsSeparator = ',';
    // The .NET Framework string.Join has no char overload, so the same separator is also kept as text
    private const string AppsSeparatorText = ",";
    private const string PipeNamePrefix = "TrayAppDotNETInstaller.";
    private const string RunAsVerb = "runas";
    private const string LocalServerName = ".";
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

        return
        [
            WorkerArgument,
            PipeArgument, arguments.PipeName,
            ModeArgument, ModeToValue(arguments.Mode),
            DesktopShortcutArgument, arguments.CreateDesktopShortcut ? TrueValue : FalseValue,
            StartMenuShortcutArgument, arguments.CreateStartMenuShortcut ? TrueValue : FalseValue,
            AppsArgument, string.Join(AppsSeparatorText, arguments.ApplicationNames)
        ];
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

        string? modeValue = TryGetArgumentValue(args, ModeArgument);
        if (!TryParseMode(modeValue, out InstallMode mode))
        {
            error = $"{ModeArgument} must be {LocalModeValue}, {SystemModeValue}, or {PortableModeValue}.";
            return false;
        }

        if (!TryParseBoolean(TryGetArgumentValue(args, DesktopShortcutArgument), out bool createDesktopShortcut))
        {
            error = $"{DesktopShortcutArgument} must be {TrueValue} or {FalseValue}.";
            return false;
        }

        if (!TryParseBoolean(TryGetArgumentValue(args, StartMenuShortcutArgument), out bool createStartMenuShortcut))
        {
            error = $"{StartMenuShortcutArgument} must be {TrueValue} or {FalseValue}.";
            return false;
        }

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

        arguments = new WorkerArguments(pipeName, mode, createDesktopShortcut, createStartMenuShortcut, applicationNames);
        return true;
    }

    /// <summary>Worker process entry point: connects to the UI's pipe and runs the engine.</summary>
    public static int Run(string[] args)
    {
        if (!TryParseWorkerArguments(args, out WorkerArguments? arguments, out string? error))
        {
            InstallerLog.Write($"ElevatedInstallWorker.Run: {error}");
            return UsageExitCode;
        }

        InstallerLog.Write($"ElevatedInstallWorker.Run: mode {arguments.Mode}, apps {string.Join(AppsSeparatorText, arguments.ApplicationNames)}");
        try
        {
            return RunWorkerAsync(arguments, progress => RunInstallAsync(arguments, progress))
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
        {
            InstallerLog.Write("ElevatedInstallWorker.Run", exception);
            return FailureExitCode;
        }
    }

    /// <summary>Pipe transport of the worker; <paramref name="runInstall"/> is injected so tests can drive it without payloads.</summary>
    internal static async Task<int> RunWorkerAsync(
        WorkerArguments arguments,
        Func<IProgress<InstallProgressLine>, Task<InstallOutcome>> runInstall)
    {
        // The .NET Framework has no IAsyncDisposable, so these are plain using declarations; disposal order
        // is unchanged, the writer closing before the pipe.
        // Anonymous impersonation stands in for the half of PipeOptions.CurrentUserOnly that protects the
        // client: without it the framework lets the pipe's owner impersonate this worker, which runs elevated.
        using NamedPipeClientStream pipe = new(
            LocalServerName,
            arguments.PipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Anonymous);
        await pipe.ConnectAsync(ConnectTimeoutMs).ConfigureAwait(false);
        VerifyPipeOwnerIsCurrentUser(pipe, arguments.PipeName);

        using StreamWriter writer = new(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.AutoFlush = true;
        PipeProgress progress = new(writer);
        InstallOutcome outcome = await runInstall(progress).ConfigureAwait(false);
        progress.EnsureTerminalLine(outcome);
        return outcome.Success ? SuccessExitCode : FailureExitCode;
    }

    /// <summary>UI side: starts the elevated worker and relays its progress. Runs in-process when already elevated.</summary>
    public static async Task<InstallOutcome> RunElevatedAsync(
        InstallPlan plan,
        IProgress<InstallProgressLine> progress,
        CancellationToken cancellationToken)
    {
        FrameworkCompatibility.ThrowIfNull(plan, nameof(plan));
        FrameworkCompatibility.ThrowIfNull(progress, nameof(progress));

        if (SystemProbes.IsElevated())
            return await InstallEngine.RunAsync(plan, progress, cancellationToken).ConfigureAwait(false);

        string? executablePath = FrameworkCompatibility.ProcessPath;
        if (string.IsNullOrEmpty(executablePath))
            return Fail(L(nameof(AppStrings.Installer_Error_WorkerPathUnknown)), progress);

        List<string> applicationNames = [];
        foreach (EmbeddedPayload payload in plan.Payloads) applicationNames.Add(payload.ApplicationName);

        string pipeName = $"{PipeNamePrefix}{FrameworkCompatibility.ProcessID}.{Guid.NewGuid():N}";
        string[] workerArguments = BuildWorkerArguments(new WorkerArguments(
            pipeName,
            plan.Mode,
            plan.CreateDesktopShortcut,
            plan.CreateStartMenuShortcut,
            applicationNames));

        try
        {
            // PipeOptions.CurrentUserOnly does not exist on the .NET Framework, so the same guarantee comes
            // from an explicit descriptor; see CreateCurrentUserOnlyPipeSecurity
            using NamedPipeServerStream server = new(
                pipeName,
                PipeDirection.In,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                DefaultPipeBufferSize,
                DefaultPipeBufferSize,
                CreateCurrentUserOnlyPipeSecurity());

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
                return await RelayWorkerAsync(server, worker, plan, progress, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return Fail(L(nameof(AppStrings.Installer_Progress_Cancelled)), progress);
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
        IProgress<InstallProgressLine> progress,
        CancellationToken cancellationToken)
    {
        Task workerExitTask = worker.WaitForExitAsync(CancellationToken.None);
        Task connectTask = server.WaitForConnectionAsync(cancellationToken);
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

        string? failureMessage = null;
        using (StreamReader reader = new(server, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            string? line;
            while ((line = await ReadLineAsync(reader, cancellationToken).ConfigureAwait(false)) != null)
            {
                if (!InstallProgressLine.TryParseLine(line, out InstallProgressLine? parsed))
                {
                    InstallerLog.Write($"ElevatedInstallWorker: unrecognized worker line: {line}");
                    continue;
                }

                if (parsed.IsFailure) failureMessage = parsed.Message;
                progress.Report(parsed);
            }
        }

        await workerExitTask.ConfigureAwait(false);
        int exitCode = worker.ExitCode;
        InstallerLog.Write($"ElevatedInstallWorker: worker exited with {exitCode}");
        // The worker already sent the [FAIL] line, so it is not reported a second time
        if (failureMessage != null) return new InstallOutcome(Success: false, failureMessage, []);
        if (exitCode != SuccessExitCode) return Fail(Format(nameof(AppStrings.Installer_Error_WorkerExitCode_Format), exitCode), progress);

        List<string> installedExecutables = [];
        foreach (EmbeddedPayload payload in plan.Payloads)
        {
            installedExecutables.Add(InstallDefaults.InstalledExecutablePath(
                plan.Mode, plan.TargetDirectory, payload.ApplicationName));
        }

        return new InstallOutcome(Success: true, ErrorMessage: null, installedExecutables);
    }

    /// <summary>
    /// Reads one line with cancellation. The .NET Framework only offers the parameterless
    /// StreamReader.ReadLineAsync, so the read races a task the token completes. The abandoned read ends
    /// when the caller disposes the reader and the pipe on its way out.
    /// </summary>
    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        Task<string> readTask = reader.ReadLineAsync();
        if (!cancellationToken.CanBeCanceled) return await readTask.ConfigureAwait(false);

        TaskCompletionSource<bool> cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
        {
            Task firstTask = await Task.WhenAny(readTask, cancelled.Task).ConfigureAwait(false);
            if (ReferenceEquals(firstTask, readTask)) return await readTask.ConfigureAwait(false);
        }

        // Disposing the pipe faults the abandoned read, so its exception is observed here instead of being
        // left to the unobserved task exception path
        _ = readTask.ContinueWith(
            static task => InstallerLog.Write("ElevatedInstallWorker: the cancelled pipe read ended", task.Exception!),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        throw new OperationCanceledException(cancellationToken);
    }

    private static Task<InstallOutcome> RunInstallAsync(WorkerArguments arguments, IProgress<InstallProgressLine> progress)
    {
        EmbeddedPayloadCatalog catalog = EmbeddedPayloadCatalog.Load();
        List<EmbeddedPayload> payloads = [];
        foreach (string applicationName in arguments.ApplicationNames)
        {
            EmbeddedPayload? payload = catalog.Find(applicationName);
            if (payload == null)
            {
                string message = Format(nameof(AppStrings.Installer_Error_PayloadMissing_Format), applicationName);
                progress.Report(InstallProgressLine.Failed(message));
                return Task.FromResult(new InstallOutcome(Success: false, message, []));
            }

            payloads.Add(payload);
        }

        InstallPlan plan = new(
            arguments.Mode,
            InstallDefaults.DefaultDirectory(arguments.Mode, catalog),
            payloads,
            arguments.CreateDesktopShortcut,
            arguments.CreateStartMenuShortcut);
        return InstallEngine.RunAsync(plan, progress, CancellationToken.None);
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

    private static InstallOutcome Fail(string message, IProgress<InstallProgressLine> progress)
    {
        progress.Report(InstallProgressLine.Failed(message));
        return new InstallOutcome(Success: false, message, []);
    }

    private static string ModeToValue(InstallMode mode) => mode switch
    {
        InstallMode.Local => LocalModeValue,
        InstallMode.System => SystemModeValue,
        InstallMode.Portable => PortableModeValue,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, message: "Unsupported install mode.")
    };

    private static bool TryParseMode(string? value, out InstallMode mode)
    {
        switch (value?.ToLowerInvariant())
        {
            case LocalModeValue:
                mode = InstallMode.Local;
                return true;
            case SystemModeValue:
                mode = InstallMode.System;
                return true;
            case PortableModeValue:
                mode = InstallMode.Portable;
                return true;
            default:
                mode = InstallMode.Local;
                return false;
        }
    }

    private static bool TryParseBoolean(string? value, out bool result)
    {
        switch (value?.ToLowerInvariant())
        {
            case TrueValue:
                result = true;
                return true;
            case FalseValue:
                result = false;
                return true;
            default:
                result = false;
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

    /// <summary>Writes progress lines to the pipe; the engine reports from several threads.</summary>
    private sealed class PipeProgress(TextWriter writer) : IProgress<InstallProgressLine>
    {
        // Stands in for System.Threading.Lock, which the .NET Framework does not define
        private readonly object _gate = new();
        private bool _terminalLineWritten;

        public void Report(InstallProgressLine value)
        {
            FrameworkCompatibility.ThrowIfNull(value, nameof(value));

            lock (_gate)
            {
                try
                {
                    writer.WriteLine(value.ToLine());
                    if (value.IsFailure || value.IsComplete) _terminalLineWritten = true;
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                    InstallerLog.Write("ElevatedInstallWorker: pipe write failed", exception);
                }
            }
        }

        /// <summary>Guarantees the UI sees a terminal line even when the engine returned without one.</summary>
        public void EnsureTerminalLine(InstallOutcome outcome)
        {
            lock (_gate)
            {
                if (_terminalLineWritten) return;
            }

            Report(outcome.Success
                ? InstallProgressLine.At(InstallProgressLine.CompletePercent, L(nameof(AppStrings.Installer_Progress_Complete)))
                : InstallProgressLine.Failed(outcome.ErrorMessage ?? L(nameof(AppStrings.Installer_Status_Failed))));
        }
    }
    private static string L(string key) => LocalizationManager.Instance[key];

    private static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, L(key), arguments);
}
