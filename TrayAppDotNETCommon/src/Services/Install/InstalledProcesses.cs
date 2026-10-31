using System.Diagnostics;
using TrayAppDotNETCommon.Contracts;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.Services.Install;

/// <summary>
/// Finds, stops and restarts the TrayAppDotNET apps running from one install folder. Installs, updates and
/// uninstalls use it so that a replaced exe or shared native DLL is never in use, and so that what was stopped is
/// started again afterwards.
/// </summary>
internal static class TrayAppDotNETInstalledProcesses
{
    private const string AppExecutableSuffix = "TrayAppDotNET.exe";

    /// <summary>
    /// The exes whose processes must stop before <paramref name="targetExecutable"/> is replaced: the target itself,
    /// plus every other app exe in its folder when <paramref name="includeSiblings"/> (the shared native DLLs change).
    /// </summary>
    public static HashSet<string> ExecutablePaths(string targetExecutable, bool includeSiblings)
    {
        string normalizedTarget = Path.GetFullPath(targetExecutable);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase) { normalizedTarget };
        if (!includeSiblings) return paths;

        string? targetDirectory = Path.GetDirectoryName(normalizedTarget);
        if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory)) return paths;

        foreach (string executable in Directory.EnumerateFiles(targetDirectory, searchPattern: "*.exe"))
        {
            if (IsAppExecutableName(Path.GetFileName(executable))) paths.Add(Path.GetFullPath(executable));
        }

        return paths;
    }

    /// <summary>True for the exe file name of a TrayAppDotNET app, such as <c>BatteryTrayAppDotNET.exe</c>.</summary>
    public static bool IsAppExecutableName(string fileName) =>
        fileName.EndsWith(AppExecutableSuffix, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Path.GetExtension(fileName), b: ".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="relativePath"/> names one of the shared native DLLs at the folder root.</summary>
    public static bool IsSharedNativeDll(string relativePath) =>
        string.IsNullOrEmpty(Path.GetDirectoryName(relativePath))
        && (TrayAppDotNETInstallContract.SharedNativeDllNames.Contains(Path.GetFileName(relativePath), StringComparer.OrdinalIgnoreCase)
            || TrayAppDotNETInstallContract.OptionalSharedNativeDllNames.Contains(Path.GetFileName(relativePath), StringComparer.OrdinalIgnoreCase));

    /// <summary>Captures every process, other than this one, whose image is one of <paramref name="executables"/>.</summary>
    public static List<TrayAppDotNETProcessIdentity> Find(HashSet<string> executables)
    {
        List<TrayAppDotNETProcessIdentity> matches = [];
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == Environment.ProcessId) continue;

                // A process that has exited no longer opens, so this also stands in for HasExited
                string? executable = ProcessImagePath.TryGet(process.Id);
                if (executable == null || !executables.Contains(Path.GetFullPath(executable))) continue;

                TrayAppDotNETProcessIdentity? identity = TrayAppDotNETProcessIdentity.TryCapture(process.Id);
                if (identity != null) matches.Add(identity);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                // The process ended while it was inspected
            }
            finally
            {
                process.Dispose();
            }
        }

        return matches;
    }

    /// <summary>The distinct running images among <paramref name="executables"/>, in a stable order.</summary>
    public static List<string> RunningExecutables(HashSet<string> executables) =>
    [
        .. Find(executables)
            .Select(process => Path.GetFullPath(process.ImagePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
    ];

    /// <summary>
    /// Asks every matching app to exit, waits for the graceful exit, then kills what is left, retrying for apps
    /// that start meanwhile. Throws <see cref="IOException"/> when a process cannot be stopped.
    /// </summary>
    public static void Stop(HashSet<string> executables, int attempts, int retryDelayMs, Action<string> log)
    {
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            List<TrayAppDotNETProcessIdentity> processes = Find(executables);
            if (processes.Count == 0) return;

            foreach (TrayAppDotNETProcessIdentity process in processes)
                log($"Stopping PID {process.ProcessId}: {process.ImagePath}");
            IReadOnlyList<TrayAppDotNETProcessIdentity> remaining = TrayAppDotNETExitRequest.StopProcesses(
                processes,
                TimeConstants.GracefulExitRequestTimeoutMs,
                killEntireProcessTree: true,
                log);
            if (remaining.Count == 0) continue;

            if (attempt < attempts) Thread.Sleep(retryDelayMs);
        }

        List<TrayAppDotNETProcessIdentity> survivors = Find(executables);
        if (survivors.Count > 0)
        {
            throw new IOException(
                "Could not stop installed process IDs: "
                + string.Join(separator: ", ", survivors.Select(process => process.ProcessId)));
        }
    }

    /// <summary>
    /// Starts each exe as the signed-in user. <paramref name="argumentsFor"/> supplies the arguments of each one.
    /// Failures are logged; returns the exes that could not be started.
    /// </summary>
    public static List<string> Start(
        IEnumerable<string> executables,
        Func<string, IReadOnlyList<string>> argumentsFor,
        Action<string> log)
    {
        List<string> failed = [];
        foreach (string executable in executables)
        {
            if (!TrayAppDotNETAppLauncher.Launch(executable, argumentsFor(executable), log)) failed.Add(executable);
        }

        return failed;
    }
}
