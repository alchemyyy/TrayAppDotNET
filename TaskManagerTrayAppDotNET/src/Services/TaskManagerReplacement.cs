using System.ComponentModel;
using System.Diagnostics;

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
    private const int ErrorCancelled = 1223;
    private const int ActivationTimeoutMilliseconds = 1_500;

    /// <summary>Reads the current redirect state from the registry.</summary>
    public static TaskManagerReplacementState GetState()
    {
        string? registered = IfeoRegistry.GetDebuggerExecutable(TaskManagerImageName, TADNLog.Log);
        if (string.IsNullOrWhiteSpace(registered))
            return new TaskManagerReplacementState(IsEnabled: false, PointsElsewhere: false, RegisteredExecutable: null);

        bool pointsAtThisApp = PointsAtThisApp(registered);
        return new TaskManagerReplacementState(pointsAtThisApp, PointsElsewhere: !pointsAtThisApp, registered);
    }

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

        int modeIndex = Array.FindIndex(
            arguments,
            argument => string.Equals(argument, ConfigureModeArgument, StringComparison.OrdinalIgnoreCase));
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
                if (process.ExitCode != ConfigureSuccessExitCode) return TaskManagerReplacementResult.Failed;
                return enable ? TaskManagerReplacementResult.Enabled : TaskManagerReplacementResult.Disabled;
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
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
    /// Removes the redirect during uninstall so Ctrl+Shift+Esc never points at a deleted executable.
    /// Only removes a redirect that targets this application, and only succeeds when the caller is
    /// elevated (a system uninstall); a per-user uninstall logs and moves on.
    /// </summary>
    public static void RemoveForUninstall()
    {
        if (!RedirectPointsAtThisApp()) return;
        _ = IfeoRegistry.RemoveDebugger(TaskManagerImageName, TADNLog.Log);
    }

    /// <summary>Resolves the executable to register: the stable system install when present, else this process.</summary>
    internal static string ResolveTargetExecutable()
    {
        string programFilesExecutable = AppServices.InstallLayout.ProgramFilesInstallExecutable;
        if (File.Exists(programFilesExecutable)) return programFilesExecutable;
        return Environment.ProcessPath ?? programFilesExecutable;
    }

    /// <summary>Returns true when the resolved target is the machine-wide system install path.</summary>
    internal static bool TargetIsSystemInstall() =>
        string.Equals(
            Path.GetFullPath(ResolveTargetExecutable()),
            Path.GetFullPath(AppServices.InstallLayout.ProgramFilesInstallExecutable),
            StringComparison.OrdinalIgnoreCase)
        && File.Exists(AppServices.InstallLayout.ProgramFilesInstallExecutable);

    internal static bool PointsAtThisApp(string? registeredExecutable)
    {
        if (string.IsNullOrWhiteSpace(registeredExecutable)) return false;
        return string.Equals(
            Path.GetFileName(registeredExecutable.Trim()),
            ExpectedExecutableFileName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool EnableRedirection()
    {
        string target = ResolveTargetExecutable();
        if (string.IsNullOrEmpty(target) || !File.Exists(target))
        {
            TADNLog.Log($"TaskManagerReplacement.EnableRedirection: target '{target}' does not exist.");
            return false;
        }

        return IfeoRegistry.WriteDebugger(TaskManagerImageName, target, TADNLog.Log);
    }

    private static bool DisableRedirection()
    {
        // Never clobber a redirect that some other tool (for example Process Explorer) installed
        if (!RedirectPointsAtThisApp()) return true;
        return IfeoRegistry.RemoveDebugger(TaskManagerImageName, TADNLog.Log);
    }

    private static bool RedirectPointsAtThisApp() =>
        PointsAtThisApp(IfeoRegistry.GetDebuggerExecutable(TaskManagerImageName, TADNLog.Log));

    private static string ExpectedExecutableFileName => Constants.ApplicationName + ".exe";
}
