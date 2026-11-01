using System.ComponentModel;
using System.Diagnostics;
using TrayAppDotNETCommon.Services.Ipc;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>The outcome of an elevated relaunch that turns a machine-level setting on or off.</summary>
internal enum ElevatedSettingChangeResult
{
    Enabled,
    Disabled,
    Declined,
    Failed
}

/// <summary>
/// The relaunch behind settings that change what only an administrator may change (the taskmgr.exe redirect and the
/// Always run as administrator task): this exe runs elevated with "&lt;mode argument&gt; on" or "off", makes the change
/// before the UI and single-instance machinery start, and exits. It writes no file, because a same-user process
/// controls the user's folders and could redirect an elevated write with a junction, so its exit code is the whole
/// report.
/// </summary>
internal static class ElevatedSettingChange
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

    /// <summary>Relaunches this executable elevated to apply the change, returning the observed outcome.</summary>
    public static async Task<ElevatedSettingChangeResult> RunElevatedAsync(
        string modeArgument,
        bool enable,
        string logSource)
    {
        string? sourceExecutable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(sourceExecutable))
        {
            TADNLog.Log($"{logSource}: the process path is unavailable.");
            return ElevatedSettingChangeResult.Failed;
        }

        string arguments = $"{modeArgument} {(enable ? EnableToken : DisableToken)}";
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
                if (process == null) return ElevatedSettingChangeResult.Failed;

                process.WaitForExit();
                if (process.ExitCode != SuccessExitCode)
                {
                    TADNLog.Log($"{logSource}: the elevated change exited with code {process.ExitCode}.");
                    return ElevatedSettingChangeResult.Failed;
                }

                return enable ? ElevatedSettingChangeResult.Enabled : ElevatedSettingChangeResult.Disabled;
            }
            catch (Win32Exception exception) when (TrayAppDotNETElevatedLaunch.IsUserCancelled(exception))
            {
                // The user declined the UAC prompt
                return ElevatedSettingChangeResult.Declined;
            }
            catch (Exception exception)
            {
                TADNLog.Log($"{logSource}: {exception}");
                return ElevatedSettingChangeResult.Failed;
            }
        }).ConfigureAwait(true);
    }
}
