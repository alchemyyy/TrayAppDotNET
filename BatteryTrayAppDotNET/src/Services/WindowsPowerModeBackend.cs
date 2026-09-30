using System.Diagnostics;
using System.Globalization;
using BatteryTrayAppDotNET.Models;

namespace BatteryTrayAppDotNET.Services;

internal static class WindowsPowerModeBackend
{
    private const int PowerCfgTimeoutMs = 5_000;
    private const string UltimateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private const string UltimateName = "Ultimate Performance";
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string PowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";
    private const string EnergySaverSubgroupGuid = "de830923-a562-41af-a086-e3a2c6bad2da";
    private const string EnergySaverBatteryThresholdGuid = "e69653ca-cf7f-4f05-aa73-cb833fa90ad4";
    private const string CurrentAcIndexLabel = "Current AC Power Setting Index";
    private const string CurrentDcIndexLabel = "Current DC Power Setting Index";
    private const int EnergySaverAlwaysThreshold = 100;
    private const int EnergySaverNeverThreshold = 0;

    // A mode switch and an Energy Saver change each take several powercfg calls against SCHEME_CURRENT. One gate
    // keeps them, and the reads that follow them, from interleaving.
    private static readonly SemaphoreSlim Gate = new(initialCount: 1, maxCount: 1);

    public static async Task<bool?> ReadEnergySaverAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CommandResult result = await RunAsync(
                ["/qh", "SCHEME_CURRENT", EnergySaverSubgroupGuid, EnergySaverBatteryThresholdGuid],
                cancellationToken).ConfigureAwait(false);
            if (!result.Success) return null;

            int? threshold = ParsePowerCfgIndex(result.Output, CurrentDcIndexLabel)
                             ?? ParsePowerCfgIndex(result.Output, CurrentAcIndexLabel);
            return threshold.HasValue ? threshold.Value >= EnergySaverAlwaysThreshold : null;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Sets Energy Saver to Always or Never on the active scheme.</summary>
    public static async Task<bool> ApplyEnergySaverAsync(bool enabled, CancellationToken cancellationToken)
    {
        string value = (enabled ? EnergySaverAlwaysThreshold : EnergySaverNeverThreshold)
            .ToString(CultureInfo.InvariantCulture);
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool acSuccess = (await RunAsync(
                ["/setacvalueindex", "SCHEME_CURRENT", EnergySaverSubgroupGuid, EnergySaverBatteryThresholdGuid, value],
                cancellationToken).ConfigureAwait(false)).Success;
            bool dcSuccess = (await RunAsync(
                ["/setdcvalueindex", "SCHEME_CURRENT", EnergySaverSubgroupGuid, EnergySaverBatteryThresholdGuid, value],
                cancellationToken).ConfigureAwait(false)).Success;
            bool activeSuccess = (await RunAsync(["/setactive", "SCHEME_CURRENT"], cancellationToken)
                .ConfigureAwait(false)).Success;
            return acSuccess && dcSuccess && activeSuccess;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<BatteryPowerModeReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CommandResult result = await RunAsync(["/getactivescheme"], cancellationToken).ConfigureAwait(false);
            if (!result.Success || ExtractGuid(result.Output) == null) return new(false, null);

            BatteryPowerMode? mode = result.Output.Contains(UltimateGuid, StringComparison.OrdinalIgnoreCase)
                                     || result.Output.Contains($"({UltimateName})", StringComparison.OrdinalIgnoreCase)
                ? BatteryPowerMode.Ultimate
                : result.Output.Contains(BalancedGuid, StringComparison.OrdinalIgnoreCase)
                    ? BatteryPowerMode.Balanced
                    : result.Output.Contains(PowerSaverGuid, StringComparison.OrdinalIgnoreCase)
                        ? BatteryPowerMode.PowerSaver
                        : null;
            return new(true, mode);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<bool> ApplyAsync(BatteryPowerMode mode, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (mode != BatteryPowerMode.Ultimate)
            {
                string guid = mode switch
                {
                    BatteryPowerMode.Balanced => BalancedGuid,
                    BatteryPowerMode.PowerSaver => PowerSaverGuid,
                    _ => throw new ArgumentOutOfRangeException(nameof(mode))
                };
                return await ActivateAsync(guid, cancellationToken).ConfigureAwait(false);
            }

            string? ultimate = await FindUltimateAsync(cancellationToken).ConfigureAwait(false);
            if (ultimate != null && await ActivateAsync(ultimate, cancellationToken).ConfigureAwait(false))
                return true;

            CommandResult created = await RunAsync(["/duplicatescheme", UltimateGuid], cancellationToken)
                .ConfigureAwait(false);
            ultimate = created.Success ? ExtractGuid(created.Output) : null;
            ultimate ??= await FindUltimateAsync(cancellationToken).ConfigureAwait(false);
            return ultimate != null && await ActivateAsync(ultimate, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<bool> ActivateAsync(string scheme, CancellationToken cancellationToken)
    {
        await CarryEnergySaverThresholdAsync(scheme, cancellationToken).ConfigureAwait(false);
        return (await RunAsync(["/setactive", scheme], cancellationToken).ConfigureAwait(false)).Success;
    }

    /// <summary>
    /// Energy Saver's threshold is stored per scheme, so each mode would otherwise switch Energy Saver to its own
    /// setting. Copying the active scheme's threshold keeps Energy Saver where the user left it across mode changes.
    /// </summary>
    private static async Task CarryEnergySaverThresholdAsync(string targetScheme, CancellationToken cancellationToken)
    {
        CommandResult current = await RunAsync(
            ["/qh", "SCHEME_CURRENT", EnergySaverSubgroupGuid, EnergySaverBatteryThresholdGuid],
            cancellationToken).ConfigureAwait(false);
        // The query output opens with the active scheme's GUID
        string? currentScheme = current.Success ? ExtractGuid(current.Output) : null;
        if (currentScheme == null || currentScheme.Equals(targetScheme, StringComparison.OrdinalIgnoreCase)) return;

        if (ParsePowerCfgIndex(current.Output, CurrentAcIndexLabel) is { } acThreshold)
        {
            await RunAsync(
                ["/setacvalueindex", targetScheme, EnergySaverSubgroupGuid, EnergySaverBatteryThresholdGuid,
                    acThreshold.ToString(CultureInfo.InvariantCulture)],
                cancellationToken).ConfigureAwait(false);
        }

        if (ParsePowerCfgIndex(current.Output, CurrentDcIndexLabel) is { } dcThreshold)
        {
            await RunAsync(
                ["/setdcvalueindex", targetScheme, EnergySaverSubgroupGuid, EnergySaverBatteryThresholdGuid,
                    dcThreshold.ToString(CultureInfo.InvariantCulture)],
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string?> FindUltimateAsync(CancellationToken cancellationToken)
    {
        CommandResult result = await RunAsync(["/list"], cancellationToken).ConfigureAwait(false);
        if (!result.Success) return null;

        string? namedGuid = null;
        foreach (string line in result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string? guid = ExtractGuid(line);
            if (guid == null) continue;
            if (guid.Equals(UltimateGuid, StringComparison.OrdinalIgnoreCase)) return guid;
            if (line.Contains($"({UltimateName})", StringComparison.OrdinalIgnoreCase)) namedGuid ??= guid;
        }

        return namedGuid;
    }

    private static string? ExtractGuid(string output)
    {
        foreach (string token in output.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            if (Guid.TryParse(token, out Guid guid)) return guid.ToString("D");
        return null;
    }

    private static int? ParsePowerCfgIndex(string output, string label)
    {
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.Contains(label, StringComparison.OrdinalIgnoreCase)) continue;
            int colon = line.IndexOf(':');
            if (colon < 0 || colon == line.Length - 1) return null;
            string token = line[(colon + 1)..].Trim();
            return token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.TryParse(token[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hexValue)
                    ? hexValue : null
                : int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                    ? value : null;
        }

        return null;
    }

    private static async Task<CommandResult> RunAsync(string[] arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PowerCfgTimeoutMs);
        ProcessStartInfo start = new()
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "powercfg.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = new() { StartInfo = start };
        try
        {
            if (!process.Start()) return new(false, string.Empty);

            // Drain both pipes concurrently with a cancellable exit wait so neither pipe can bypass the timeout.
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
            if (process.ExitCode != 0)
                TADNLog.Log($"WindowsPowerModeBackend powercfg {string.Join(' ', arguments)}: exit {process.ExitCode}");
            return new(process.ExitCode == 0, await output.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
            TADNLog.Log($"WindowsPowerModeBackend powercfg {string.Join(' ', arguments)}: timed out");
            return new(false, string.Empty);
        }
        catch (Exception ex)
        {
            TryKill(process);
            TADNLog.Log($"WindowsPowerModeBackend powercfg {string.Join(' ', arguments)}: {ex.Message}");
            return new(false, string.Empty);
        }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { /* Best effort after timeout, cancellation, or startup failure. */ }
    }

    private readonly record struct CommandResult(bool Success, string Output);
}
