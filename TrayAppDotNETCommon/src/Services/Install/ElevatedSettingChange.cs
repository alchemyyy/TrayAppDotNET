using System.ComponentModel;
using System.Diagnostics;
using TrayAppDotNETCommon.Services.Ipc;

namespace TrayAppDotNETCommon.Services.Install;

/// <summary>The outcome of an elevated relaunch that turns a machine-level setting on or off.</summary>
public enum TrayAppDotNETElevatedSettingChangeResult
{
    Enabled,
    Disabled,
    Declined,
    Failed
}

/// <summary>
/// The relaunch behind settings that change what only an administrator may change (Task Manager's taskmgr.exe redirect
/// and every app's Always run as administrator task): this exe runs elevated with "&lt;mode argument&gt; on" or "off",
/// makes the change before the UI and single-instance machinery start, and exits. It writes no file, because a
/// same-user process controls the user's folders and could redirect an elevated write with a junction, so its exit
/// code is the whole report.
/// </summary>
public static class TrayAppDotNETElevatedSettingChange
{
    public const int SuccessExitCode = 0;
    public const int FailureExitCode = 2;

    private const string EnableToken = "on";
    private const string DisableToken = "off";

    /// <summary>Returns the index of <paramref name="modeArgument"/>, or -1 when this launch is not that change.</summary>
    public static int FindModeArgument(string[] arguments, string modeArgument)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return Array.FindIndex(
            arguments,
            argument => string.Equals(argument, modeArgument, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Reads the on or off token after the mode argument; false when it is missing or neither.</summary>
    public static bool TryReadToggle(string[] arguments, int modeIndex, out bool enable)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string? token = modeIndex >= 0 && modeIndex + 1 < arguments.Length ? arguments[modeIndex + 1] : null;
        enable = string.Equals(token, EnableToken, StringComparison.OrdinalIgnoreCase);
        return enable || string.Equals(token, DisableToken, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Relaunches this executable elevated to apply the change off the calling thread.</summary>
    public static async Task<TrayAppDotNETElevatedSettingChangeResult> RunElevatedAsync(
        string modeArgument,
        bool enable,
        string logSource) =>
        await Task.Run(() => RunElevated(modeArgument, enable, logSource)).ConfigureAwait(true);

    /// <summary>
    /// Relaunches this executable elevated to apply the change and waits for it, returning the observed outcome. The
    /// UAC prompt and the change block the calling thread.
    /// </summary>
    public static TrayAppDotNETElevatedSettingChangeResult RunElevated(string modeArgument, bool enable, string logSource)
    {
        string? sourceExecutable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(sourceExecutable))
        {
            TADNLog.Log($"{logSource}: the process path is unavailable.");
            return TrayAppDotNETElevatedSettingChangeResult.Failed;
        }

        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = sourceExecutable,
                Arguments = $"{modeArgument} {(enable ? EnableToken : DisableToken)}",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using Process? process = Process.Start(startInfo);
            if (process == null) return TrayAppDotNETElevatedSettingChangeResult.Failed;

            process.WaitForExit();
            if (process.ExitCode != SuccessExitCode)
            {
                TADNLog.Log($"{logSource}: the elevated change exited with code {process.ExitCode}.");
                return TrayAppDotNETElevatedSettingChangeResult.Failed;
            }

            return enable ? TrayAppDotNETElevatedSettingChangeResult.Enabled : TrayAppDotNETElevatedSettingChangeResult.Disabled;
        }
        catch (Win32Exception exception) when (TrayAppDotNETElevatedLaunch.IsUserCancelled(exception))
        {
            // The user declined the UAC prompt
            return TrayAppDotNETElevatedSettingChangeResult.Declined;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"{logSource}: {exception}");
            return TrayAppDotNETElevatedSettingChangeResult.Failed;
        }
    }
}
