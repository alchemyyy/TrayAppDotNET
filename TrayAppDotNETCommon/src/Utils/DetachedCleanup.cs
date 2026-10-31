using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace TrayAppDotNETCommon.Utils;

/// <summary>What a detached cleanup does with one path.</summary>
public enum DetachedCleanupAction
{
    /// <summary>Deletes a file; the cleanup keeps retrying until it is gone.</summary>
    DeleteFile,

    /// <summary>Deletes a directory tree; the cleanup keeps retrying until it is gone.</summary>
    DeleteDirectoryTree,

    /// <summary>Removes a directory only once it is empty; other content keeps it, and the cleanup does not wait for it.</summary>
    RemoveDirectoryIfEmpty
}

/// <summary>One path for <see cref="DetachedCleanup"/>.</summary>
public sealed class DetachedCleanupTarget
{
    public DetachedCleanupTarget(DetachedCleanupAction action, string path)
    {
        Action = action;
        Path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public DetachedCleanupAction Action { get; }

    public string Path { get; }
}

/// <summary>
/// Deletes paths from a hidden <c>cmd.exe</c> that keeps retrying after the calling process has exited, for files
/// the caller cannot delete itself: its own mapped exe and native DLLs, a log it still holds, or a folder a child
/// process still uses as its working directory. Shared by the apps and, as linked source, by the .NET Framework
/// installer, so it sticks to APIs both have.
/// <para>
/// No script file is written and no path is part of any command text: the command line is a fixed one-liner that
/// reads every path from an environment variable of the child process. Nothing a user can write is executed, so
/// an elevated caller is safe, and any character Windows allows in a path, non-ASCII letters, <c>&amp;</c>,
/// <c>%</c>, <c>^</c> or <c>!</c> included, reaches cmd unchanged (the environment block is UTF-16, an expanded
/// variable is never expanded again, and every reference is quoted). <c>cmd.exe</c> and <c>timeout.exe</c> come
/// from the system directory, never from the caller's <c>PATH</c> or <c>ComSpec</c>.
/// </para>
/// </summary>
public static class DetachedCleanup
{
    public const int DefaultAttempts = 60;
    public const int DefaultRetryDelaySeconds = 1;

    internal const string PathVariablePrefix = "TADN_CLEANUP_PATH_";
    internal const string TimeoutVariable = "TADN_CLEANUP_TIMEOUT";

    /// <summary>
    /// Starts the cleanup and returns the started process ID, or null when it could not start (logged). Never throws
    /// for an I/O or process start failure.
    /// </summary>
    public static int? Schedule(
        IReadOnlyList<DetachedCleanupTarget> targets,
        Action<string>? log,
        int attempts = DefaultAttempts,
        int retryDelaySeconds = DefaultRetryDelaySeconds)
    {
        if (targets == null) throw new ArgumentNullException(nameof(targets));
        if (targets.Count == 0) return null;
        if (attempts <= 0) throw new ArgumentOutOfRangeException(nameof(attempts));
        if (retryDelaySeconds <= 0) throw new ArgumentOutOfRangeException(nameof(retryDelaySeconds));

        try
        {
            ProcessStartInfo startInfo = CreateStartInfo(targets, attempts, retryDelaySeconds);
            using Process? process = Process.Start(startInfo);
            if (process == null)
            {
                log?.Invoke("DetachedCleanup: cmd.exe did not start");
                return null;
            }

            log?.Invoke($"DetachedCleanup: removing {Describe(targets)} as PID {process.Id}");
            return process.Id;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            log?.Invoke($"DetachedCleanup: could not start the removal of {Describe(targets)}: {exception.Message}");
            return null;
        }
    }

    /// <summary>Builds the cmd start info; exposed for tests.</summary>
    internal static ProcessStartInfo CreateStartInfo(
        IReadOnlyList<DetachedCleanupTarget> targets,
        int attempts,
        int retryDelaySeconds)
    {
        string systemDirectory = Environment.SystemDirectory;
        ProcessStartInfo startInfo = new()
        {
            FileName = Path.Combine(systemDirectory, "cmd.exe"),
            Arguments = BuildArguments(targets, attempts, retryDelaySeconds),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            // Never inside a folder being removed
            WorkingDirectory = systemDirectory
        };
        startInfo.Environment[TimeoutVariable] = Path.Combine(systemDirectory, "timeout.exe");
        for (int index = 0; index < targets.Count; index++)
            startInfo.Environment[PathVariablePrefix + index.ToString(CultureInfo.InvariantCulture)] = targets[index].Path;
        return startInfo;
    }

    /// <summary>
    /// The cmd arguments: one loop that attempts every removal, exits as soon as every path that must disappear is
    /// gone, and otherwise waits and tries again until the attempts run out.
    /// </summary>
    internal static string BuildArguments(
        IReadOnlyList<DetachedCleanupTarget> targets,
        int attempts,
        int retryDelaySeconds)
    {
        StringBuilder removals = new();
        StringBuilder exitCondition = new();
        bool anyRequired = false;
        for (int index = 0; index < targets.Count; index++)
        {
            string reference = "\"%" + PathVariablePrefix + index.ToString(CultureInfo.InvariantCulture) + "%\"";
            string command = targets[index].Action switch
            {
                DetachedCleanupAction.DeleteFile => "del /f /q /a " + reference,
                DetachedCleanupAction.DeleteDirectoryTree => "rmdir /s /q " + reference,
                DetachedCleanupAction.RemoveDirectoryIfEmpty => "rmdir " + reference,
                _ => throw new ArgumentOutOfRangeException(nameof(targets))
            };
            removals.Append(command).Append(" >nul 2>&1 & ");
            if (targets[index].Action == DetachedCleanupAction.RemoveDirectoryIfEmpty) continue;

            exitCondition.Append("if not exist ").Append(reference).Append(' ');
            anyRequired = true;
        }

        // Without a path that must disappear, one pass is all there is to do
        int loopCount = anyRequired ? attempts : 1;
        string exitWhenDone = anyRequired ? "(" + exitCondition + "exit 0) & " : string.Empty;
        string delay = "\"%" + TimeoutVariable + "%\" /t "
                       + retryDelaySeconds.ToString(CultureInfo.InvariantCulture) + " /nobreak >nul 2>&1";
        // /d skips AutoRun commands, /e:on enables "for /l", /v:off keeps "!" literal whatever the registry says
        return "/d /q /e:on /v:off /c for /l %i in (1,1,"
               + loopCount.ToString(CultureInfo.InvariantCulture)
               + ") do @("
               + removals
               + exitWhenDone
               + delay
               + ")";
    }

    private static string Describe(IReadOnlyList<DetachedCleanupTarget> targets)
    {
        StringBuilder builder = new();
        foreach (DetachedCleanupTarget target in targets)
        {
            if (builder.Length > 0) builder.Append(", ");
            builder.Append(target.Path);
        }

        return builder.ToString();
    }
}
