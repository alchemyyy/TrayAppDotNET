namespace BrightnessTrayAppDotNET.DDCCI;

/// <summary>
/// Represents a monitor enumerated through EnumDisplayMonitors.
/// Holds the HMONITOR handle needed to look up the associated physical monitor for DDC/CI transactions,
/// the EDID-derived identity fields, and the per-monitor VCP-command profile resolved against
/// <c>monitor-database.json</c> (brightness code, power-off commands, quirks).
/// Named <c>DDCMonitor</c> to avoid collision with <see cref="BrightnessTrayAppDotNET.Models.MonitorInfo"/>.
/// </summary>
public class DDCMonitor
{
    /// <summary>
    /// HMONITOR handle returned by EnumDisplayMonitors.
    /// Not stable across display topology changes - refresh by matching on <see cref="DeviceID"/>.
    /// </summary>
    public IntPtr Handle { get; set; }

    /// <summary>HDC passed to the enumeration callback (unused for DDC/CI, kept for parity).</summary>
    public IntPtr HDC { get; set; }

    /// <summary>
    /// Adapter device name from MONITORINFOEX (e.g. "\\.\DISPLAY1").
    /// Not a stable per-physical-monitor identifier - Windows can reassign the trailing index when monitors are
    /// hot-plugged.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Stable per-(monitor,port) identifier from <c>EnumDisplayDevices</c>, e.g. <c>MONITOR\LGE1234\{GUID}\0001</c>.
    /// Survives reboots and unplug/replug of the same monitor on the same port.
    /// Empty string if resolution failed.
    /// </summary>
    public string DeviceID { get; set; } = string.Empty;

    /// <summary>
    /// 1-based friendly display number matching what Windows Settings &gt; Display shows for this panel.
    /// Sourced from the CCD API's per-adapter <c>sourceInfo.id</c>, which is bound to the GPU output port
    /// and stays stable across topology shuffles - unlike the trailing digits of <see cref="Name"/>,
    /// which Windows monotonically increments on every new entry it creates.
    /// Falls back to parsing <see cref="Name"/> when CCD lookup misses (rare); zero if neither source produced a value
    /// (typically a transient enumeration race).
    /// </summary>
    public int DisplayNumber { get; set; }

    /// <summary>
    /// Per-unit serial number from the monitor's EDID block - either the 0xFF descriptor string (preferred)
    /// or the 4-byte numeric serial.
    /// Empty when the EDID is unreadable or the monitor doesn't populate a serial.
    /// Stable across ports: the same physical panel reports the same string regardless of which output
    /// it's plugged into.
    /// </summary>
    public string EDIDSerial { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable model name from the EDID's 0xFC descriptor (e.g. "LG ULTRAGEAR+").
    /// Empty on monitors that don't populate it.
    /// </summary>
    public string FriendlyName { get; set; } = string.Empty;

    /// <summary>
    /// 3-letter EDID PnP manufacturer ID from EDID bytes 8-9 (e.g. "DEL", "SAM", "GSM").
    /// Empty when the EDID is unreadable.
    /// </summary>
    public string EDIDManufacturerID { get; set; } = string.Empty;

    /// <summary>
    /// 16-bit vendor product code from EDID bytes 10-11, formatted as 4 uppercase hex digits (e.g. "4187").
    /// Empty when the EDID is unreadable or product code is zero.
    /// </summary>
    public string EDIDProductCode { get; set; } = string.Empty;

    /// <summary>
    /// 7-character EDID identifier in ddccontrol-db convention: <see cref="EDIDManufacturerID"/> +
    /// <see cref="EDIDProductCode"/> (e.g. "DEL4187"). The lookup key into <c>monitor-database.json</c>.
    /// Empty when either component is missing.
    /// </summary>
    public string EDIDIdentifier =>
        EDIDManufacturerID.Length == 3 && EDIDProductCode.Length == 4
            ? EDIDManufacturerID + EDIDProductCode
            : string.Empty;

    /// <summary>Left coordinate of the monitor on the virtual desktop (from EnumDisplayMonitors).</summary>
    public int X { get; set; }

    /// <summary>Top coordinate of the monitor on the virtual desktop (from EnumDisplayMonitors).</summary>
    public int Y { get; set; }

    // -------- Profile fields populated from the embedded monitor-database.json --------
    // Populated by DDCMonitorDatabase.ApplyProfile after EDID parse in DisplayService.TryGetMonitors.
    // Untouched (i.e. defaults below) when the EDID id has no DB match - the defaults are the VESA standard.

    /// <summary>
    /// VCP code used to set brightness. Universally 0x10 (Luminance) across the surveyed corpus;
    /// the override slot is reserved for monitors that respond only to the deprecated 0x13 (Backlight Control).
    /// </summary>
    public byte BrightnessCode { get; set; } = 0x10;

    /// <summary>
    /// Model name from the database (e.g. "Dell P2719HC"). Empty when the monitor's EDID id was not in the DB
    /// - the rest of the profile fields then carry their VESA-standard defaults.
    /// </summary>
    public string ProfileModelName { get; set; } = string.Empty;

    /// <summary>
    /// Power-off VCP commands ordered by preference. The first entry drives <see cref="ResolvePowerOff"/>
    /// and <see cref="ResolvePowerOn"/>; later entries are diagnostic alternates (e.g. inverted-Dell 0xE1
    /// when 0xD6 is also advertised).
    /// Defaults to the VESA DPMS standard at 0xD6 when no DB entry was matched.
    /// </summary>
    public IReadOnlyList<MonitorPowerCommand> PowerOffCommands { get; set; } = VESADefaultPowerCommands;

    /// <summary>
    /// Free-form notes from the upstream sources (ddccontrol-db caps, ddcutil per-model fixes).
    /// Surfaced through logs only; UI does not consume these directly.
    /// </summary>
    public IReadOnlyList<string> ProfileQuirks { get; set; } = [];

    /// <summary>True iff a database entry was matched - i.e. <see cref="ProfileModelName"/> is non-empty.</summary>
    public bool HasKnownProfile => !string.IsNullOrEmpty(ProfileModelName);

    /// <summary>
    /// Picks the (code, value) pair to send for power-on. Always uses the first power command's ValueOn
    /// - that's 0x01 for the VESA DPMS path (the universal case) and 0x00 for the inverted-Dell 0xE1 path.
    /// </summary>
    public (byte Code, byte Value) ResolvePowerOn()
    {
        if (PowerOffCommands.Count == 0) return (0xD6, 0x01);
        MonitorPowerCommand cmd = PowerOffCommands[0];
        return (cmd.Code, cmd.ValueOn);
    }

    /// <summary>
    /// Picks the (code, value) pair to send for a power-off level (Sleep / Soft / Hard).
    /// Uses the first power command (0xD6 in the vast majority of cases - VESA inheritance ensures it).
    /// Falls back to the next-aggressive level if the requested one is not defined for this monitor.
    /// </summary>
    public (byte Code, byte Value) ResolvePowerOff(PowerOffLevel level)
    {
        if (PowerOffCommands.Count == 0) return (0xD6, 0x05);
        MonitorPowerCommand cmd = PowerOffCommands[0];

        byte value = level switch
        {
            PowerOffLevel.Sleep => cmd.ValueStandby ?? cmd.ValueSoftOff ?? cmd.ValueHardOff,
            PowerOffLevel.Soft => cmd.ValueSoftOff ?? cmd.ValueHardOff,
            _ => cmd.ValueHardOff,
        };
        return (cmd.Code, value);
    }

    /// <summary>
    /// VESA-standard power commands used as the default when no database entry matches.
    /// 0xD6 DPMS with the canonical {1=On, 2=Standby, 3=Suspend, 4=Soft-off, 5=Hard-off} mapping,
    /// plus 0xE1 vendor power with the VESA-default {1=On, 0=Off}.
    /// </summary>
    private static readonly IReadOnlyList<MonitorPowerCommand> VESADefaultPowerCommands =
    [
        new()
        {
            Code = 0xD6,
            ValueOn = 0x01,
            ValueStandby = 0x02,
            ValueSoftOff = 0x04,
            ValueHardOff = 0x05,
            IsInverted = false,
            Label = "VESA DPMS / Power Mode (MCCS 0xD6, default)",
        },
        new()
        {
            Code = 0xE1,
            ValueOn = 0x01,
            ValueHardOff = 0x00,
            IsInverted = false,
            Label = "VESA Power Control (MCCS 0xE1, default)",
        },
    ];
}

/// <summary>
/// One power-control command the monitor accepts.
/// MCCS defines two competing power-off conventions:
///   * 0xD6 "Power Mode" with values {1=On, 2=Standby, 3=Suspend, 4=Soft-off, 5=Hard-off}
///   * 0xE1 "Vendor Power" with values {1=On, 0=Off} per VESA default - but Dell P/U-series invert it
/// A monitor's caps string can declare either or both; <see cref="DDCMonitor.PowerOffCommands"/> holds them
/// in preference order.
/// </summary>
public sealed class MonitorPowerCommand
{
    public byte Code { get; init; }
    public byte ValueOn { get; init; } = 0x01;

    /// <summary>DPMS Standby (low-power, fast wake). Only meaningful for 0xD6.</summary>
    public byte? ValueStandby { get; init; }

    /// <summary>DPMS Soft-off (deeper sleep, monitor stays on the DDC bus). Only meaningful for 0xD6.</summary>
    public byte? ValueSoftOff { get; init; }

    /// <summary>
    /// Hard power-off. For 0xD6 this is 0x05 (write-only, monitor leaves the bus).
    /// For 0xE1 this is the "Off" value, which is 0 by default but 1 for inverted Dell quirks.
    /// </summary>
    public byte ValueHardOff { get; init; }

    /// <summary>
    /// True iff the monitor inverts 0xE1 - i.e. ValueOn=0 and ValueOff=1 instead of the VESA default.
    /// Applies only to 0xE1; 0xD6 is never inverted in the corpus.
    /// </summary>
    public bool IsInverted { get; init; }

    public string Label { get; init; } = string.Empty;

    public override string ToString() =>
        $"0x{Code:X2}=0x{ValueHardOff:X2}{(IsInverted ? " [inverted]" : "")}";
}

/// <summary>
/// Three power-off intensities the rest of the codebase asks for.
/// Matches the user-facing <c>PowerOffMode</c> setting one-to-one - kept as a separate enum
/// inside the DDCCI namespace so DDCMonitor doesn't depend on the Models layer.
/// </summary>
public enum PowerOffLevel
{
    /// <summary>Lightest - DPMS Standby (0xD6=2 in VESA defaults).</summary>
    Sleep,

    /// <summary>Mid - DPMS Soft-off (0xD6=4).</summary>
    Soft,

    /// <summary>Hardest - DPMS hard-off (0xD6=5) or inverted-Dell 0xE1=1.</summary>
    Hard,
}
