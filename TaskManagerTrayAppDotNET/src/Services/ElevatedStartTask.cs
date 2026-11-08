using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml;
using TaskManagerTrayAppDotNET.Interop;
using TrayAppDotNETCommon.Contracts;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>The current account's Always run as administrator task, as the settings card shows it.</summary>
/// <param name="IsRegistered">The account's task exists.</param>
/// <param name="IsUsable">The task still starts the system install elevated: nothing outside the app changed or disabled it.</param>
/// <param name="TaskPath">Where Task Scheduler lists the task.</param>
internal readonly record struct ElevatedStartState(bool IsRegistered, bool IsUsable, string TaskPath);

/// <summary>Whether Always run as administrator may be turned on from this copy and this account.</summary>
/// <param name="IsSystemInstall">This process is the Program Files system install, the only exe the task may start.</param>
/// <param name="AccountCanElevate">The account is an administrator, so elevating keeps it the same account.</param>
internal readonly record struct ElevatedStartAvailability(bool IsSystemInstall, bool AccountCanElevate);

/// <summary>
/// Always run as administrator: a scheduled task per account that starts the Program Files system install with the
/// account's highest privileges (an interactive-token principal at the HighestAvailable run level). An unelevated
/// plain start hands off to the task and exits, so the app comes up elevated without a UAC prompt. An account may run
/// its own task without elevation, but creating or changing a task that runs elevated needs administrator rights, so
/// the settings toggle relaunches this exe elevated with <see cref="ConfigureModeArgument"/> (see
/// <see cref="ElevatedSettingChange"/>), which registers or deletes the task and exits.
///
/// Only the system install may be the task's target: any program the user runs could replace a copy in the user's
/// profile and would then start elevated without a prompt. The task passes the start flags through its $(Arg0)
/// parameter, which any program running as the user can set when it runs the task, so a start by the task
/// (<see cref="TaskLaunchArgument"/> first) accepts nothing but --autostart and --hidden, and Program.Main checks for it
/// before every other mode.
/// </summary>
internal static class ElevatedStartTask
{
    /// <summary>Elevated self-relaunch mode that registers or deletes the account's task, then exits.</summary>
    public const string ConfigureModeArgument = "--set-elevated-start";

    /// <summary>The first argument of every start by the task.</summary>
    public const string TaskLaunchArgument = "--elevated-start-task";

    /// <summary>The exit code of a start by the task whose arguments were refused.</summary>
    public const int RejectedTaskLaunchExitCode = 2;

    // Task Scheduler replaces $(Arg0) with the text the hand-off passes to Run, and leaves it as it is when the task
    // runs without parameters (started from the Task Scheduler window, for example)
    private const string ParameterPlaceholder = "$(Arg0)";
    internal const string TaskActionArguments = TaskLaunchArgument + " " + ParameterPlaceholder;

    private const string TaskFolderName = TrayAppDotNETConstants.SharedRootFolderName;
    private const string TaskNamePrefix = Constants.ApplicationName + "-";

    private const string TaskSecurityDescriptorFormat = "D:(A;;FA;;;SY)(A;;FA;;;BA)(A;;FRFX;;;{0})";

    private const string TaskDescription =
        "Starts " + Constants.ApplicationName + " as administrator without a UAC prompt for the account this task " +
        "runs as. The app's Always run as administrator setting creates and deletes this task.";

    private const string TaskSchemaNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private const string InteractiveTokenLogonType = "InteractiveToken";
    private const string HighestAvailableRunLevel = "HighestAvailable";

    /// <summary>The task's name for one account: the app name and the account SID, like Windows' own per-user tasks.</summary>
    internal static string TaskName(string userSid) => TaskNamePrefix + userSid;

    /// <summary>The task's path in Task Scheduler for one account.</summary>
    internal static string TaskPath(string userSid) => $@"\{TaskFolderName}\{TaskName(userSid)}";

    /// <summary>
    /// The task's security descriptor: the account may read and run its task without elevation, while only SYSTEM and
    /// administrators may change or delete it.
    /// </summary>
    internal static string TaskSecurityDescriptor(string userSid) =>
        string.Format(CultureInfo.InvariantCulture, TaskSecurityDescriptorFormat, userSid);

    /// <summary>The only executable the task may start.</summary>
    private static string SystemInstallExecutable => AppServices.Install.Layout.ProgramFilesInstallExecutable;

    /// <summary>Reads whether this copy and this account may turn the setting on.</summary>
    public static ElevatedStartAvailability GetAvailability()
    {
        string systemInstallExecutable = SystemInstallExecutable;
        return new ElevatedStartAvailability(
            IsSystemInstall: File.Exists(systemInstallExecutable) &&
                             IsSameExecutable(Environment.ProcessPath, systemInstallExecutable),
            AccountCanElevate: !TrayAppDotNETElevationPolicy.ElevationWouldUseAnotherAccount(TADNLog.Log));
    }

    /// <summary>
    /// Whether the settings toggle may be used: both directions need an administrator account, turning the task on
    /// needs this copy to be the system install, and a registered task can be turned off from any copy.
    /// </summary>
    internal static bool CanToggle(ElevatedStartAvailability availability, ElevatedStartState state) =>
        availability.AccountCanElevate && (state.IsRegistered || availability.IsSystemInstall);

    /// <summary>Reads the current account's task from Task Scheduler.</summary>
    public static ElevatedStartState GetState()
    {
        string? userSid = CurrentUserSid();
        if (userSid == null) return new ElevatedStartState(IsRegistered: false, IsUsable: false, TaskPath: string.Empty);

        string taskPath = TaskPath(userSid);
        bool isRegistered = false;
        bool isUsable = false;
        _ = TryWithSession("ElevatedStartTask.GetState", session =>
        {
            int hr = session.TryOpenTask(TaskFolderName, TaskName(userSid), out ScheduledTask? task);
            if (TaskSchedulerSession.IsNotFound(hr)) return 0;
            if (hr < 0 || task == null) return hr;
            using (task)
            {
                isRegistered = true;
                hr = task.TryGetXml(out string? xml);
                isUsable = hr >= 0 && IsUsableDefinition(xml, SystemInstallExecutable);
                return hr;
            }
        });
        return new ElevatedStartState(isRegistered, isUsable, taskPath);
    }

    /// <summary>Relaunches this executable elevated to register or delete the account's task.</summary>
    public static Task<ElevatedSettingChangeResult> SetEnabledElevatedAsync(bool enable) =>
        ElevatedSettingChange.RunElevatedAsync(
            ConfigureModeArgument,
            enable,
            logSource: "ElevatedStartTask.SetEnabledElevatedAsync");

    /// <summary>Returns true when this launch is the elevated change of the account's task.</summary>
    public static bool IsElevatedConfigureLaunch(string[] arguments) =>
        ElevatedSettingChange.FindModeArgument(arguments, ConfigureModeArgument) >= 0;

    /// <summary>
    /// Registers or deletes the account's task when launched with <see cref="ConfigureModeArgument"/>, before the UI
    /// and single-instance machinery. Returns true when the launch was this change; <paramref name="exitCode"/> is the
    /// process exit code to return.
    /// </summary>
    public static bool TryHandleElevatedConfigure(string[] arguments, out int exitCode)
    {
        exitCode = ElevatedSettingChange.SuccessExitCode;
        int modeIndex = ElevatedSettingChange.FindModeArgument(arguments, ConfigureModeArgument);
        if (modeIndex < 0) return false;

        if (!ElevatedSettingChange.TryReadToggle(arguments, modeIndex, out bool enable))
        {
            TADNLog.Log($"ElevatedStartTask: '{ConfigureModeArgument}' requires 'on' or 'off'.");
            exitCode = ElevatedSettingChange.FailureExitCode;
            return true;
        }

        bool succeeded = SetEnabledForCurrentUser(enable);
        exitCode = succeeded ? ElevatedSettingChange.SuccessExitCode : ElevatedSettingChange.FailureExitCode;
        return true;
    }

    /// <summary>
    /// Registers or deletes the current account's task; needs administrator rights. The elevated configure mode and a
    /// system install's Always run as administrator choice both end here.
    /// </summary>
    internal static bool SetEnabledForCurrentUser(bool enable) => enable ? RegisterForCurrentUser() : DeleteForCurrentUser();

    /// <summary>
    /// Deletes every account's task during a system uninstall. The tasks only ever start the system install, so other
    /// uninstalls leave them. Best effort: a task left behind starts a missing exe and does nothing.
    /// </summary>
    public static void RemoveForUninstall(InstallScope scope)
    {
        if (scope != InstallScope.ProgramFiles) return;

        _ = TryWithSession("ElevatedStartTask.RemoveForUninstall", session =>
        {
            int hr = session.DeleteTasks(TaskFolderName, IsAppTaskName);
            if (TaskSchedulerSession.IsNotFound(hr)) return 0;
            DeleteFolderIfEmpty(session);
            return hr;
        });
    }

    /// <summary>
    /// Hands an unelevated plain start to the account's task, which starts this exe again elevated. Returns true when
    /// the task started and this process should exit; any other outcome returns false and the app starts unelevated as
    /// before. Only plain starts hand off (see <see cref="TryGetHandOffParameter"/>), and only to a task that starts
    /// this very exe elevated.
    /// </summary>
    public static bool TryHandOffLaunch(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!TryGetHandOffParameter(arguments, out string parameter)) return false;

        // Already elevated, or UAC is off; a debugger session stays in this process
        if (AppElevation.IsElevated || Debugger.IsAttached) return false;

        string? executable = Environment.ProcessPath;
        string? userSid = CurrentUserSid();
        if (string.IsNullOrEmpty(executable) || userSid == null) return false;

        bool started = false;
        _ = TryWithSession("ElevatedStartTask.TryHandOffLaunch", session =>
        {
            int hr = session.TryOpenTask(TaskFolderName, TaskName(userSid), out ScheduledTask? task);
            // The usual case: the setting is off
            if (TaskSchedulerSession.IsNotFound(hr)) return 0;
            if (hr < 0 || task == null) return hr;
            using (task)
            {
                hr = task.TryGetXml(out string? xml);
                if (hr < 0) return hr;

                // Another copy's task (the system install's, seen from a portable copy) is not this start's business
                TaskDefinitionSummary definition = ReadTaskDefinition(xml);
                if (!IsSameExecutable(definition.Executable, executable)) return 0;
                if (!definition.StartsElevated)
                {
                    TADNLog.Log(
                        $"ElevatedStartTask: {TaskPath(userSid)} was changed or disabled outside the app; starting " +
                        "without administrator rights.");
                    return 0;
                }

                hr = task.Run(parameter);
                started = hr >= 0;
                return hr;
            }
        });
        return started;
    }

    /// <summary>
    /// Picks the $(Arg0) text for a start that may hand off: no arguments, --autostart, --hidden, or a redirected
    /// taskmgr.exe start (whose switches mean nothing to the app). Any other argument, --settings-dir included, keeps
    /// the start in this process.
    /// </summary>
    internal static bool TryGetHandOffParameter(string[] arguments, out string parameter)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        parameter = string.Empty;
        if (TaskManagerReplacement.IsRedirectedLaunch(arguments)) return true;

        bool isStartupLaunch = false;
        bool isHiddenLaunch = false;
        foreach (string argument in arguments)
        {
            if (string.Equals(argument, TrayAppDotNETProgram.AutostartArgument, StringComparison.OrdinalIgnoreCase))
                isStartupLaunch = true;
            else if (string.Equals(argument, TrayAppDotNETInstallContract.HiddenArgument, StringComparison.OrdinalIgnoreCase))
                isHiddenLaunch = true;
            else
                return false;
        }

        parameter = string.Join(' ', LaunchFlags(isStartupLaunch, isHiddenLaunch));
        return true;
    }

    /// <summary>
    /// True when any argument is <see cref="TaskLaunchArgument"/>; such a start may only go through
    /// <see cref="TryGetTaskLaunchArguments"/>.
    /// </summary>
    public static bool IsTaskLaunch(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return Array.Exists(
            arguments,
            argument => string.Equals(argument, TaskLaunchArgument, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks a start by the task and returns the arguments of the plain start it stands for. The marker must come
    /// first and be followed by nothing, by an unreplaced $(Arg0), or by --autostart and --hidden at most once each,
    /// exactly as the hand-off writes them. Anything else could have come from another program through $(Arg0), so the
    /// start is refused.
    /// </summary>
    internal static bool TryGetTaskLaunchArguments(string[] arguments, out string[] launchArguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        launchArguments = [];
        if (arguments.Length == 0 || !string.Equals(arguments[0], TaskLaunchArgument, StringComparison.Ordinal))
            return false;
        if (arguments.Length == 2 && string.Equals(arguments[1], ParameterPlaceholder, StringComparison.Ordinal))
            return true;

        bool isStartupLaunch = false;
        bool isHiddenLaunch = false;
        for (int index = 1; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            if (!isStartupLaunch && string.Equals(argument, TrayAppDotNETProgram.AutostartArgument, StringComparison.Ordinal))
                isStartupLaunch = true;
            else if (!isHiddenLaunch && string.Equals(argument, TrayAppDotNETInstallContract.HiddenArgument, StringComparison.Ordinal))
                isHiddenLaunch = true;
            else
                return false;
        }

        launchArguments = LaunchFlags(isStartupLaunch, isHiddenLaunch);
        return true;
    }

    /// <summary>The task definition for one account: the system install, started elevated through the app's action.</summary>
    internal static string BuildTaskDefinition(string executable, string userSid)
    {
        StringBuilder builder = new();
        XmlWriterSettings settings = new() { OmitXmlDeclaration = true, Indent = true };
        using (XmlWriter writer = XmlWriter.Create(builder, settings))
        {
            writer.WriteStartElement("Task", TaskSchemaNamespace);
            writer.WriteAttributeString("version", "1.2");

            writer.WriteStartElement("RegistrationInfo", TaskSchemaNamespace);
            writer.WriteElementString("Author", TaskSchemaNamespace, Constants.ApplicationName);
            writer.WriteElementString("Description", TaskSchemaNamespace, TaskDescription);
            writer.WriteEndElement();

            writer.WriteStartElement("Principals", TaskSchemaNamespace);
            writer.WriteStartElement("Principal", TaskSchemaNamespace);
            writer.WriteAttributeString("id", "Author");
            writer.WriteElementString("UserId", TaskSchemaNamespace, userSid);
            writer.WriteElementString("LogonType", TaskSchemaNamespace, InteractiveTokenLogonType);
            writer.WriteElementString("RunLevel", TaskSchemaNamespace, HighestAvailableRunLevel);
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteStartElement("Settings", TaskSchemaNamespace);
            // Every start hands off, and the app's own single-instance takeover keeps the newest one, as without the
            // task; the default (IgnoreNew) would drop a start while an earlier one still runs
            writer.WriteElementString("MultipleInstancesPolicy", TaskSchemaNamespace, "Parallel");
            writer.WriteElementString("DisallowStartIfOnBatteries", TaskSchemaNamespace, "false");
            writer.WriteElementString("StopIfGoingOnBatteries", TaskSchemaNamespace, "false");
            // The default stops the task, and with it the app, after 72 hours
            writer.WriteElementString("ExecutionTimeLimit", TaskSchemaNamespace, "PT0S");
            // The default priority (7) starts the app below normal priority; 4 is the normal one for interactive tasks
            writer.WriteElementString("Priority", TaskSchemaNamespace, "4");
            writer.WriteEndElement();

            writer.WriteStartElement("Actions", TaskSchemaNamespace);
            writer.WriteAttributeString("Context", "Author");
            writer.WriteStartElement("Exec", TaskSchemaNamespace);
            writer.WriteElementString("Command", TaskSchemaNamespace, executable);
            writer.WriteElementString("Arguments", TaskSchemaNamespace, TaskActionArguments);
            writer.WriteElementString("WorkingDirectory", TaskSchemaNamespace, Path.GetDirectoryName(executable));
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteEndElement();
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether a registered definition still starts <paramref name="executable"/> elevated through the app's action.
    /// </summary>
    internal static bool IsUsableDefinition(string? xml, string? executable)
    {
        TaskDefinitionSummary definition = ReadTaskDefinition(xml);
        return definition.StartsElevated && IsSameExecutable(definition.Executable, executable);
    }

    /// <summary>
    /// Reads what a definition starts. <see cref="TaskDefinitionSummary.StartsElevated"/> needs exactly one action, an
    /// Exec with the app's own arguments, an interactive-token principal at the highest available run level, and the
    /// task enabled.
    /// </summary>
    internal static TaskDefinitionSummary ReadTaskDefinition(string? xml)
    {
        if (string.IsNullOrEmpty(xml)) return default;

        int actionCount = 0;
        string? command = null;
        string? actionArguments = null;
        string? logonType = null;
        string? runLevel = null;
        bool isEnabled = true;
        try
        {
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true
            };
            using XmlReader reader = XmlReader.Create(new StringReader(xml), settings);
            List<string> path = [];
            reader.MoveToContent();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    path.RemoveAt(path.Count - 1);
                    reader.Read();
                    continue;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    reader.Read();
                    continue;
                }

                if (path is ["Task", "Actions"]) actionCount++;
                string location = string.Join('/', path) + "/" + reader.LocalName;
                switch (location)
                {
                    case "Task/Actions/Exec/Command":
                        command = reader.ReadElementContentAsString();
                        continue;
                    case "Task/Actions/Exec/Arguments":
                        actionArguments = reader.ReadElementContentAsString();
                        continue;
                    case "Task/Principals/Principal/LogonType":
                        logonType = reader.ReadElementContentAsString();
                        continue;
                    case "Task/Principals/Principal/RunLevel":
                        runLevel = reader.ReadElementContentAsString();
                        continue;
                    case "Task/Settings/Enabled":
                        isEnabled = !string.Equals(reader.ReadElementContentAsString().Trim(), "false", StringComparison.Ordinal);
                        continue;
                }

                if (!reader.IsEmptyElement) path.Add(reader.LocalName);
                reader.Read();
            }
        }
        catch (XmlException exception)
        {
            TADNLog.Log($"ElevatedStartTask.ReadTaskDefinition: {exception.Message}");
            return default;
        }

        bool startsElevated = actionCount == 1 &&
                              command != null &&
                              string.Equals(actionArguments?.Trim(), TaskActionArguments, StringComparison.Ordinal) &&
                              string.Equals(logonType?.Trim(), InteractiveTokenLogonType, StringComparison.Ordinal) &&
                              string.Equals(runLevel?.Trim(), HighestAvailableRunLevel, StringComparison.Ordinal) &&
                              isEnabled;
        return new TaskDefinitionSummary(command?.Trim().Trim('"'), startsElevated);
    }

    /// <summary>Whether two executable paths name the same file (full, normalized, case-insensitive paths).</summary>
    internal static bool IsSameExecutable(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) &&
        !string.IsNullOrWhiteSpace(second) &&
        Path.IsPathFullyQualified(first) &&
        Path.IsPathFullyQualified(second) &&
        string.Equals(
            PathNormalization.Normalize(first),
            PathNormalization.Normalize(second),
            StringComparison.OrdinalIgnoreCase);

    private static string[] LaunchFlags(bool isStartupLaunch, bool isHiddenLaunch) => (isStartupLaunch, isHiddenLaunch) switch
    {
        (true, true) => [TrayAppDotNETProgram.AutostartArgument, TrayAppDotNETInstallContract.HiddenArgument],
        (true, false) => [TrayAppDotNETProgram.AutostartArgument],
        (false, true) => [TrayAppDotNETInstallContract.HiddenArgument],
        _ => []
    };

    private static bool RegisterForCurrentUser()
    {
        string executable = SystemInstallExecutable;
        if (!File.Exists(executable))
        {
            TADNLog.Log($"ElevatedStartTask: no system install at '{executable}'; only a Program Files install can run as administrator without a prompt.");
            return false;
        }

        string? userSid = CurrentUserSid();
        if (userSid == null) return false;

        string definition = BuildTaskDefinition(executable, userSid);
        return TryWithSession(
            "ElevatedStartTask.RegisterForCurrentUser",
            session => session.RegisterTask(
                TaskFolderName,
                TaskName(userSid),
                definition,
                userSid,
                TaskSecurityDescriptor(userSid)));
    }

    private static bool DeleteForCurrentUser()
    {
        string? userSid = CurrentUserSid();
        if (userSid == null) return false;

        return TryWithSession("ElevatedStartTask.DeleteForCurrentUser", session =>
        {
            int hr = session.DeleteTask(TaskFolderName, TaskName(userSid));
            if (hr < 0 && !TaskSchedulerSession.IsNotFound(hr)) return hr;
            DeleteFolderIfEmpty(session);
            return 0;
        });
    }

    /// <summary>Deletes the shared TrayAppDotNET task folder once no task or folder is left in it.</summary>
    private static void DeleteFolderIfEmpty(TaskSchedulerSession session)
    {
        int hr = session.DeleteFolder(TaskFolderName);
        if (hr < 0 && !TaskSchedulerSession.IsNotEmpty(hr) && !TaskSchedulerSession.IsNotFound(hr))
            TADNLog.Log($"ElevatedStartTask: deleting the task folder failed with 0x{hr:X8}.");
    }

    private static bool IsAppTaskName(string name) => name.StartsWith(TaskNamePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs <paramref name="action"/> on a Task Scheduler session and returns whether it succeeded. Failures are logged
    /// under <paramref name="logSource"/>; nothing throws, so no caller (startup, settings, uninstall) is stopped by it.
    /// </summary>
    private static bool TryWithSession(string logSource, Func<TaskSchedulerSession, int> action)
    {
        try
        {
            using ComApartmentScope apartment = ComApartmentScope.Enter(
                Ole32.COINIT_APARTMENTTHREADED | Ole32.COINIT_DISABLE_OLE1DDE);
            if (!apartment.IsUsable)
            {
                TADNLog.Log($"{logSource}: COM initialization failed with 0x{apartment.HResult:X8}.");
                return false;
            }

            int hr = TaskSchedulerSession.TryConnect(out TaskSchedulerSession? session);
            if (hr < 0 || session == null)
            {
                TADNLog.Log($"{logSource}: connecting to Task Scheduler failed with 0x{hr:X8}.");
                return false;
            }

            using (session)
            {
                hr = action(session);
            }

            if (hr >= 0) return true;
            TADNLog.Log($"{logSource}: Task Scheduler failed with 0x{hr:X8}.");
            return false;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"{logSource}: {exception}");
            return false;
        }
    }

    private static string? CurrentUserSid()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
        {
            TADNLog.Log($"ElevatedStartTask: reading the account SID failed: {exception.Message}");
            return null;
        }
    }
}

/// <summary>What a task definition starts, as <see cref="ElevatedStartTask.ReadTaskDefinition"/> reads it.</summary>
/// <param name="Executable">The Exec action's command, unquoted.</param>
/// <param name="StartsElevated">The definition starts that command elevated through the app's own action.</param>
internal readonly record struct TaskDefinitionSummary(string? Executable, bool StartsElevated);
