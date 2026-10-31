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
    MonitorOff
}

/// <summary>
/// One persisted Brightness hotkey binding; the attributes come from <see cref="HotkeyBindingBase{TAction}"/>.
/// <see cref="BrightnessHotkeyAction.ProfileSelect"/> stores the slot index ("0", "1", ...) in
/// <see cref="HotkeyBindingBase{TAction}.Parameter"/>; <see cref="BrightnessHotkeyAction.MonitorOff"/> stores a
/// <see cref="HotkeyTarget"/> ("#N" or "edid:&lt;EDIDKey&gt;"). Fixed-target actions leave it empty.
/// </summary>
public sealed class HotkeyBinding : HotkeyBindingBase<BrightnessHotkeyAction>;

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
