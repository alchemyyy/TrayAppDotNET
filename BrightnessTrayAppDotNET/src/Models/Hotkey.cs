using System.Xml.Serialization;
using TrayAppDotNETCommon.Models;

namespace BrightnessTrayAppDotNET.Models;

public enum BrightnessHotkeyAction
{
    OpenSettings,
    OpenFlyout,
    FullBright,
    FullDim,
    IncrementMasterBrightness,
    DecrementMasterBrightness,
    ToggleNightLight,
    IncrementNightLight,
    DecrementNightLight,
    NormalizeBrightnesses,
    PowerOffAllMonitors,
    ProfileSelect,
    MonitorOff,
}

/// <summary>
/// One persisted hotkey binding.
/// Identity = (Action, Parameter, BindingID): per (action, parameter) pair there can be N bindings,
/// distinguished by BindingID. BindingID == 0 is the legacy/primary row; legacy XML files without
/// the attribute load to 0 and so become the primary row by default.
/// Modifiers and VirtualKey are raw Win32 values (MOD_* and VK_*) so the storage shape matches RegisterHotKey directly
/// and the settings model doesn't depend on UI input enums.
/// MOD_NOREPEAT is added at registration time, never persisted.
/// </summary>
public sealed class HotkeyBinding
    : IHotkeyBinding<BrightnessHotkeyAction>
{
    [XmlAttribute]
    public BrightnessHotkeyAction Action { get; set; }

    /// <summary>
    /// Action-specific parameter, encoded as a string for stable explicit XML persistence.
    /// <see cref="BrightnessHotkeyAction.ProfileSelect"/> stores the slot index ("0", "1", ...).
    /// <see cref="BrightnessHotkeyAction.MonitorOff"/> stores either "#N" (Windows-assigned display number)
    /// or "edid:&lt;EDIDKey&gt;".
    /// Empty for fixed-target actions.
    /// </summary>
    [XmlAttribute]
    public string Parameter { get; set; } = string.Empty;

    /// <summary>
    /// MOD_* flags from RegisterHotKey (no MOD_NOREPEAT - added at registration time).
    /// </summary>
    [XmlAttribute]
    public uint Modifiers { get; set; }

    /// <summary>VK_* virtual key code passed to RegisterHotKey.</summary>
    [XmlAttribute]
    public uint VirtualKey { get; set; }

    [XmlAttribute]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Disambiguator for multiple bindings sharing the same (Action, Parameter).
    /// 0 = primary/legacy row; 1, 2, ... = additional bindings added by the user.
    /// Missing in legacy XML, loads as 0.
    /// </summary>
    [XmlAttribute]
    public int BindingID { get; set; }

    /// <summary>
    /// Tombstone flag: true means the user explicitly removed this binding through the UI.
    /// Tombstones are kept in the persisted list (instead of being deleted) so the default-seeder
    /// in the default hotkey seeder can tell that a default was removed
    /// on purpose and must not be re-added on the next launch.
    /// Filtered out of UI display and hotkey registration.
    /// </summary>
    [XmlAttribute]
    public bool RemovedByUser { get; set; }

    [XmlIgnore]
    public bool IsBound => VirtualKey != 0 && Modifiers != 0;

    /// <summary>
    /// "Any binding for this (Action, Parameter)" lookup.
    /// Use the 3-arg overload when row identity matters for persistence/status.
    /// </summary>
    public bool Matches(BrightnessHotkeyAction action, string? parameter) =>
        Action == action && string.Equals(Parameter, parameter ?? string.Empty, StringComparison.Ordinal);

    /// <summary>Strict identity check including the per-row BindingID disambiguator.</summary>
    public bool Matches(BrightnessHotkeyAction action, string? parameter, int bindingID) =>
        Action == action
        && string.Equals(Parameter, parameter ?? string.Empty, StringComparison.Ordinal)
        && BindingID == bindingID;
}

/// <summary>
/// Helpers for encoding/decoding the <see cref="HotkeyBinding.Parameter"/> string
/// for actions that target a specific monitor.
/// Two flavors:
///   * <c>#N</c> - by Windows-assigned display number (the badge in Settings &gt; Display).
///     Targets whatever monitor currently holds that number, even across reboots or hotplug.
///   * <c>edid:&lt;EDIDKey&gt;</c> - by stable EDID-first identifier.
///     Targets the specific physical panel regardless of which port it's plugged into.
/// </summary>
public static class HotkeyTarget
{
    private const string EDIDPrefix = "edid:";
    private const string NumberPrefix = "#";

    public static string ForDisplayNumber(int n) =>
        NumberPrefix + n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string ForEDID(string EDIDKey) => EDIDPrefix + EDIDKey;

    public static bool TryParseDisplayNumber(string? parameter, out int n)
    {
        n = 0;
        if (string.IsNullOrEmpty(parameter)) return false;

        return parameter.StartsWith(NumberPrefix, StringComparison.Ordinal) && int.TryParse(
            parameter.AsSpan(NumberPrefix.Length), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out n);
    }

    public static bool TryParseEDID(string? parameter, out string EDIDKey)
    {
        EDIDKey = string.Empty;
        if (string.IsNullOrEmpty(parameter)) return false;

        if (!parameter.StartsWith(EDIDPrefix, StringComparison.Ordinal)) return false;

        EDIDKey = parameter[EDIDPrefix.Length..];
        return EDIDKey.Length > 0;
    }
}
