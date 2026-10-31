using System.Globalization;
using TrayAppDotNETCommon.Serialization;

namespace FanControlTrayAppDotNET.Services;

/// <summary>
/// Tracks whether an earlier instance in this boot left fans under software control without restoring them.
/// LHM's Super I/O drivers snapshot the fan registers at the first SetSoftware of a process and SetDefault
/// restores that snapshot. After a crash the registers stay at the crashed instance's last duty, so the next
/// instance's snapshot (and every SetDefault) is that duty, not BIOS auto. The marker file holds the boot
/// time; a marker from the same boot means firmware defaults can't be trusted until the next reboot.
/// The marker tracks machine state, not user settings, so it lives under the shared root
/// (<c>%LOCALAPPDATA%\TrayAppDotNET</c>), which <c>--settings-dir</c> does not move: a crash under one settings
/// folder is still seen by a later launch under another. Older builds kept it beside the settings
/// (<see cref="LegacyPath"/>); Load honours a same-boot marker there and deletes that file.
/// Known limitation: the boot time is wall time minus uptime, and Fast Startup resumes the kernel session with its
/// uptime, so a Fast Startup power cycle counts as the same boot. That keeps defaults untrusted (curve fallbacks write
/// Max Duty instead of SetDefault) until a full restart: the loud, safe side.
/// </summary>
internal sealed class FanControlDirtyShutdownMarker
{
    internal const string FileName = "fan-control-active.marker";

    internal static string DefaultPath => Path.Combine(Program.LocalAppDataRoot, FileName);

    /// <summary>Where builds before the shared-root move wrote the marker: the app's settings folder.</summary>
    internal static string LegacyPath => Path.Combine(AppSettings.GetDefaultDirectory(), FileName);

    private readonly string _path;
    private readonly string? _legacyPath;
    private readonly Lock _lock = new();
    private bool _written;

    public FanControlDirtyShutdownMarker(string path, string? legacyPath = null)
    {
        _path = path;
        _legacyPath = string.Equals(legacyPath, path, StringComparison.OrdinalIgnoreCase) ? null : legacyPath;
    }

    /// <summary>
    /// False after Load found a marker from this boot: SetDefault would restore a crashed instance's duty.
    /// </summary>
    public bool FirmwareDefaultsTrusted { get; private set; } = true;

    /// <summary>
    /// Boot time of the running OS, derived from wall time minus the monotonic uptime.
    /// </summary>
    public static DateTime CurrentBootTimeUtc() => BootTimeUtc(DateTime.UtcNow, Environment.TickCount64);

    internal static DateTime BootTimeUtc(DateTime utcNow, long tickCount64Ms) =>
        utcNow - TimeSpan.FromMilliseconds(tickCount64Ms);

    internal static bool IsSameBoot(DateTime markerBootUtc, DateTime currentBootUtc) =>
        Math.Abs((markerBootUtc - currentBootUtc).TotalMilliseconds) <= TimeConstants.BootIdentityToleranceMs;

    internal static string Format(DateTime bootUtc) =>
        bootUtc.Ticks.ToString(CultureInfo.InvariantCulture);

    internal static bool TryParse(string? text, out DateTime bootUtc)
    {
        bootUtc = default;
        if (!long.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
            return false;
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) return false;

        bootUtc = new DateTime(ticks, DateTimeKind.Utc);
        return true;
    }

    /// <summary>
    /// A marker from the same boot means the last instance exited without restoring its fans. A missing,
    /// unreadable, or earlier-boot marker means firmware defaults are fine (a reboot resets the fan chip).
    /// </summary>
    internal static bool AreFirmwareDefaultsTrusted(string? markerText, DateTime currentBootUtc) =>
        !(TryParse(markerText, out DateTime markerBootUtc) && IsSameBoot(markerBootUtc, currentBootUtc));

    /// <summary>
    /// Startup check. Keeps a same-boot marker (the registers stay suspect until reboot) and deletes any other. A
    /// legacy-path marker counts the same; it is moved to the shared root when it is from this boot, then deleted.
    /// </summary>
    public void Load(DateTime currentBootUtc)
    {
        lock (_lock)
        {
            string? legacyText = _legacyPath == null ? null : TryRead(_legacyPath);
            bool legacyTrusted = AreFirmwareDefaultsTrusted(legacyText, currentBootUtc);
            string? text = TryRead(_path, out bool readFailed);
            if (readFailed && legacyTrusted) return;

            bool trusted = AreFirmwareDefaultsTrusted(text, currentBootUtc);
            FirmwareDefaultsTrusted = trusted && legacyTrusted;
            if (FirmwareDefaultsTrusted)
            {
                TryDelete(_path);
                if (_legacyPath != null && legacyText != null) TryDelete(_legacyPath);
                return;
            }

            // A same-boot legacy marker that is the only record of the unclean exit moves to the shared root first;
            // it stays where it is only when that copy could not be written
            bool legacyIsOnlyRecord = !legacyTrusted && trusted;
            bool carried = legacyIsOnlyRecord && !readFailed && TryWrite(legacyText!);
            if (_legacyPath != null && legacyText != null && (!legacyIsOnlyRecord || carried))
                TryDelete(_legacyPath);

            _written = true;
            TADNLog.Log(
                "FanControlDirtyShutdownMarker: an earlier instance this boot exited without restoring fans; "
                + "firmware defaults are untrusted, curve fallbacks write Max Duty instead of SetDefault");
        }
    }

    /// <summary>
    /// Called before the first SetSoftware of this process. Writes once; failures are logged, not thrown.
    /// </summary>
    public void MarkSoftwareControl(DateTime currentBootUtc)
    {
        lock (_lock)
        {
            if (_written) return;
            _written = true;
            _ = TryWrite(Format(currentBootUtc));
        }
    }

    /// <summary>
    /// Called after the clean-exit restore completed. With trusted defaults that restore put the fans back
    /// on firmware control, so the marker goes. With untrusted defaults it only restored the crashed
    /// instance's duty, so the marker stays until a reboot.
    /// </summary>
    public void ClearAfterCleanRestore()
    {
        lock (_lock)
        {
            if (!FirmwareDefaultsTrusted) return;

            TryDelete(_path);
            _written = false;
        }
    }

    private static string? TryRead(string path) => TryRead(path, out _);

    private static string? TryRead(string path, out bool failed)
    {
        failed = false;
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex)
        {
            failed = true;
            TADNLog.Log($"FanControlDirtyShutdownMarker: reading '{path}' failed: {ex.Message}");
            return null;
        }
    }

    private bool TryWrite(string text)
    {
        try
        {
            AtomicFile.WriteAllText(_path, text);
            return true;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"FanControlDirtyShutdownMarker: write failed: {ex.Message}");
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"FanControlDirtyShutdownMarker: deleting '{path}' failed: {ex.Message}");
        }
    }
}
