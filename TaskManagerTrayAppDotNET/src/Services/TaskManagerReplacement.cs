using System.ComponentModel;
using System.Diagnostics;
using TrayAppDotNETCommon.Services.Ipc;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>The outcome of an elevated request to enable or disable Task Manager replacement.</summary>
internal enum TaskManagerReplacementResult
{
    Enabled,
    Disabled,
    Declined,
    Failed
}

/// <summary>Reflects what the machine-wide taskmgr.exe redirect currently points at.</summary>
internal readonly record struct TaskManagerReplacementState(
    bool IsEnabled,
    bool PointsElsewhere,
    string? RegisteredExecutable);

/// <summary>
/// Policy for replacing Windows Task Manager with tmtadn through the Image File Execution Options
/// "Debugger" value on taskmgr.exe. Unlike the in-process Ctrl+Shift+Esc hook, this redirect is
/// machine-wide and works even when tmtadn is not already running: every route that starts Task
/// Manager through CreateProcess (Ctrl+Shift+Esc, the Ctrl+Alt+Del screen, the taskbar menu, Win+X,
/// or running taskmgr from Run) launches tmtadn instead.
///
/// Because the value applies to every account, it may only ever point at the Program Files system
/// install: a per-user or portable copy lives in one user's writable profile and disappears with a
/// per-user uninstall, which would leave taskmgr.exe broken for everyone. Ownership is decided by the
/// full executable path, never the file name, so removing one copy never deletes a redirect that
/// another copy (or another tool) owns.
///
/// Writing and removing the value needs administrator rights (HKLM), so the settings toggle relaunches
/// this executable elevated with <see cref="ConfigureModeArgument"/>; that relaunch performs the
/// registry change and exits without starting the UI. Redirected taskmgr launches arrive with the
/// original image path as the first argument and are handed off to any running instance.
/// </summary>
internal static class TaskManagerReplacement
{
    // The Windows image whose launches we intercept
    private const string TaskManagerImageName = "taskmgr.exe";

    // Elevated self-relaunch mode that performs the HKLM write or removal, then exits
    public const string ConfigureModeArgument = "--set-taskmgr-replacement";
    private const string EnableToken = "on";
    private const string DisableToken = "off";

    private const int ConfigureSuccessExitCode = 0;
    private const int ConfigureFailureExitCode = 2;
    private const int ActivationTimeoutMilliseconds = 1_500;

    /// <summary>Reads the current redirect state from the registry.</summary>
    public static TaskManagerReplacementState GetState()
    {
        string? registered = IfeoRegistry.GetDebuggerExecutable(TaskManagerImageName, TADNLog.Log);
        if (string.IsNullOrWhiteSpace(registered))
            return new TaskManagerReplacementState(IsEnabled: false, PointsElsewhere: false, RegisteredExecutable: null);

        bool pointsAtThisApp = IsOwnedBy(registered, OwnedExecutables());
        return new TaskManagerReplacementState(pointsAtThisApp, PointsElsewhere: !pointsAtThisApp, registered);
    }

    /// <summary>
    /// Returns true when the replacement can be turned on: only a Program Files system install may be registered
    /// machine-wide.
    /// </summary>
    public static bool IsAvailable => File.Exists(SystemInstallExecutable);

    /// <summary>
    /// Returns whether the settings toggle may be used: turning the redirect on needs a system install, while a
    /// redirect this copy owns can always be turned off.
    /// </summary>
    internal static bool CanToggle(bool systemInstallExists, TaskManagerReplacementState state) =>
        systemInstallExists || state.IsEnabled;

    /// <summary>Returns true when the arguments describe an IFEO-redirected taskmgr.exe launch.</summary>
    public static bool IsRedirectedLaunch(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length == 0) return false;

        string firstArgument = arguments[0];
        if (string.IsNullOrWhiteSpace(firstArgument)) return false;

        // The redirect passes the original image path ("C:\Windows\System32\Taskmgr.exe") as our first argument
        return string.Equals(
            Path.GetFileName(firstArgument.Trim('"')),
            TaskManagerImageName,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// For a redirected launch, hands off to a running instance so it comes forward. Returns true when
    /// an instance answered and this process should exit; false means no instance is running and the
    /// caller should cold-start (the redirect arguments are harmless and ignored by normal startup).
    /// </summary>
    public static bool TryHandOffRedirectedLaunch(string[] arguments)
    {
        if (!IsRedirectedLaunch(arguments)) return false;
        return TaskManagerActivationServer.TryActivateRunningInstance(ActivationTimeoutMilliseconds, TADNLog.Log);
    }

    /// <summary>Returns true when this launch is the elevated taskmgr.exe redirect change.</summary>
    public static bool IsElevatedConfigureLaunch(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return FindConfigureModeIndex(arguments) >= 0;
    }

    private static int FindConfigureModeIndex(string[] arguments) =>
        Array.FindIndex(
            arguments,
            argument => string.Equals(argument, ConfigureModeArgument, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Performs the elevated registry change when launched with <see cref="ConfigureModeArgument"/>.
    /// Runs before the UI and single-instance machinery so it neither shows a window nor takes over a
    /// running instance. Returns true when the launch was a configure request; <paramref name="exitCode"/>
    /// is the process exit code to return.
    /// </summary>
    public static bool TryHandleElevatedConfigure(string[] arguments, out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        exitCode = ConfigureSuccessExitCode;

        int modeIndex = FindConfigureModeIndex(arguments);
        if (modeIndex < 0) return false;

        string? mode = modeIndex + 1 < arguments.Length ? arguments[modeIndex + 1] : null;
        bool enable = string.Equals(mode, EnableToken, StringComparison.OrdinalIgnoreCase);
        bool disable = string.Equals(mode, DisableToken, StringComparison.OrdinalIgnoreCase);
        if (!enable && !disable)
        {
            TADNLog.Log($"TaskManagerReplacement: '{ConfigureModeArgument}' requires '{EnableToken}' or '{DisableToken}'.");
            exitCode = ConfigureFailureExitCode;
            return true;
        }

        bool succeeded = enable ? EnableRedirection() : DisableRedirection();
        exitCode = succeeded ? ConfigureSuccessExitCode : ConfigureFailureExitCode;
        return true;
    }

    /// <summary>Relaunches this executable elevated to apply the change, returning the observed outcome.</summary>
    public static async Task<TaskManagerReplacementResult> SetEnabledElevatedAsync(bool enable)
    {
        string? sourceExecutable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(sourceExecutable))
        {
            TADNLog.Log("TaskManagerReplacement.SetEnabledElevatedAsync: the process path is unavailable.");
            return TaskManagerReplacementResult.Failed;
        }

        string arguments = $"{ConfigureModeArgument} {(enable ? EnableToken : DisableToken)}";
        return await Task.Run(() =>
        {
            try
            {
                ProcessStartInfo startInfo = new()
                {
                    FileName = sourceExecutable,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using Process? process = Process.Start(startInfo);
                if (process == null) return TaskManagerReplacementResult.Failed;

                process.WaitForExit();
                if (process.ExitCode != ConfigureSuccessExitCode)
                {
                    // The elevated configure process writes no log file; its exit code is the whole report
                    TADNLog.Log(
                        $"TaskManagerReplacement.SetEnabledElevatedAsync: the elevated change exited with code {process.ExitCode}.");
                    return TaskManagerReplacementResult.Failed;
                }

                return enable ? TaskManagerReplacementResult.Enabled : TaskManagerReplacementResult.Disabled;
            }
            catch (Win32Exception exception) when (TrayAppDotNETElevatedLaunch.IsUserCancelled(exception))
            {
                // The user declined the UAC prompt
                return TaskManagerReplacementResult.Declined;
            }
            catch (Exception exception)
            {
                TADNLog.Log($"TaskManagerReplacement.SetEnabledElevatedAsync: {exception}");
                return TaskManagerReplacementResult.Failed;
            }
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Removes the redirect during uninstall so Ctrl+Shift+Esc never points at a deleted executable. Only a
    /// redirect whose full path is the executable of the copy being removed is deleted, so uninstalling one copy
    /// never removes a redirect that another copy or another tool owns. HKLM needs elevation: a system uninstall
    /// has it; a per-user copy is never registered by current builds, and a stale registration left by an older
    /// build is logged because an unelevated uninstall cannot remove it. Returns false only when a matching
    /// redirect could not be removed.
    /// </summary>
    public static bool RemoveForUninstall(InstallScope removingScope)
    {
        string? registered = IfeoRegistry.GetDebuggerExecutable(TaskManagerImageName, TADNLog.Log);
        string removedExecutable = removingScope switch
        {
            InstallScope.ProgramFiles => SystemInstallExecutable,
            InstallScope.LocalAppData => AppServices.Install.Layout.LocalAppDataInstallExecutable,
            _ => Environment.ProcessPath ?? string.Empty
        };
        if (!IsOwnedBy(registered, [removedExecutable])) return true;

        if (IfeoRegistry.RemoveDebugger(TaskManagerImageName, TADNLog.Log)) return true;
        TADNLog.Log(
            $"TaskManagerReplacement.RemoveForUninstall: taskmgr.exe still redirects to '{registered}', which this " +
            "uninstall removes; an administrator must turn the replacement off or delete the IFEO Debugger value.");
        return false;
    }

    /// <summary>The only executable the machine-wide redirect may point at.</summary>
    internal static string SystemInstallExecutable => AppServices.Install.Layout.ProgramFilesInstallExecutable;

    /// <summary>
    /// Returns true when the registered Debugger executable is exactly one of the candidate executables (full,
    /// normalized, case-insensitive path comparison; a file with the same name in another folder never matches).
    /// </summary>
    internal static bool IsOwnedBy(string? registeredExecutable, IReadOnlyList<string?> candidateExecutables)
    {
        if (string.IsNullOrWhiteSpace(registeredExecutable)) return false;
        string registered = registeredExecutable.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(registered)) return false;
        string normalizedRegistered = PathNormalization.Normalize(registered);
        for (int index = 0; index < candidateExecutables.Count; index++)
        {
            string? candidate = candidateExecutables[index];
            if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate)) continue;
            if (string.Equals(
                    normalizedRegistered,
                    PathNormalization.Normalize(candidate),
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The executables whose redirect this copy may report as its own and turn off: the system install, plus this
    /// process's own path so a stale registration made by an older per-user build can still be removed.
    /// </summary>
    private static string?[] OwnedExecutables() => [SystemInstallExecutable, Environment.ProcessPath];

    private static bool EnableRedirection()
    {
        string target = SystemInstallExecutable;
        if (!File.Exists(target))
        {
            TADNLog.Log(
                $"TaskManagerReplacement.EnableRedirection: no system install at '{target}'; only a Program Files " +
                "install can replace Task Manager.");
            return false;
        }

        return IfeoRegistry.WriteDebugger(TaskManagerImageName, target, TADNLog.Log);
    }

    private static bool DisableRedirection()
    {
        // Never clobber a redirect that some other tool (for example Process Explorer) or another copy installed
        if (!IsOwnedBy(IfeoRegistry.GetDebuggerExecutable(TaskManagerImageName, TADNLog.Log), OwnedExecutables()))
            return true;
        return IfeoRegistry.RemoveDebugger(TaskManagerImageName, TADNLog.Log);
    }
}
