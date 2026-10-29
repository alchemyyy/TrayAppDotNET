using System.Globalization;

namespace FanControlTrayAppDotNET.Services;

/// <summary>
/// Tracks whether an earlier instance in this boot left fans under software control without restoring them.
/// LHM's Super I/O drivers snapshot the fan registers at the first SetSoftware of a process and SetDefault
/// restores that snapshot. After a crash the registers stay at the crashed instance's last duty, so the next
/// instance's snapshot (and every SetDefault) is that duty, not BIOS auto. The marker file holds the boot
/// time; a marker from the same boot means firmware defaults can't be trusted until the next reboot.
/// </summary>
internal sealed class FanControlDirtyShutdownMarker
{
    internal const string FileName = "fan-control-active.marker";

    private readonly string _path;
    private readonly Lock _lock = new();
    private bool _written;

    public FanControlDirtyShutdownMarker(string path)
    {
        _path = path;
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
    /// Startup check. Keeps a same-boot marker (the registers stay suspect until reboot) and deletes any other.
    /// </summary>
    public void Load(DateTime currentBootUtc)
    {
        lock (_lock)
        {
            string? text;
            try
            {
                if (!File.Exists(_path)) return;
                text = File.ReadAllText(_path);
            }
            catch (Exception ex)
            {
                TADNLog.Log($"FanControlDirtyShutdownMarker: read failed: {ex.Message}");
                return;
            }

            FirmwareDefaultsTrusted = AreFirmwareDefaultsTrusted(text, currentBootUtc);
            if (!FirmwareDefaultsTrusted)
            {
                _written = true;
                TADNLog.Log(
                    "FanControlDirtyShutdownMarker: an earlier instance this boot exited without restoring fans; "
                    + "firmware defaults are untrusted, curve fallbacks write Max Duty instead of SetDefault");
                return;
            }

            TryDelete();
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
            try
            {
                string? directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(_path, Format(currentBootUtc));
            }
            catch (Exception ex)
            {
                TADNLog.Log($"FanControlDirtyShutdownMarker: write failed: {ex.Message}");
            }
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

            TryDelete();
            _written = false;
        }
    }

    private void TryDelete()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"FanControlDirtyShutdownMarker: delete failed: {ex.Message}");
        }
    }
}
