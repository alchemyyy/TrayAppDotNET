using System.Xml.Serialization;
using Avalonia.Media;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.UI.Models;

/// <summary>
/// A user-overridable theme color with independent light and dark variants.
/// Either side may be null, meaning "unset" - callers should fall back to their per-color defaults.
/// Temporary colors are live-preview overrides used by color picker UI and are not persisted.
/// </summary>
public class NullableThemeColor
{
    private Action? _changed;

    public NullableThemeColor() { }

    public NullableThemeColor(Action onChanged) => Subscribe(onChanged);

    public void Subscribe(Action onChanged) => _changed += onChanged;

    public void Unsubscribe(Action onChanged) => _changed -= onChanged;

    public string? LightHex
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            _changed?.Invoke();
        }
    }

    public string? DarkHex
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            _changed?.Invoke();
        }
    }

    [XmlIgnore]
    public Color? TemporaryLightColor
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            _changed?.Invoke();
        }
    }

    [XmlIgnore]
    public Color? TemporaryDarkColor
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            _changed?.Invoke();
        }
    }

    public bool IsUnset => string.IsNullOrEmpty(LightHex) && string.IsNullOrEmpty(DarkHex);

    public Color? LightColor => TemporaryLightColor ?? ColorMath.TryParseHexOrNull(LightHex);

    public Color? DarkColor => TemporaryDarkColor ?? ColorMath.TryParseHexOrNull(DarkHex);

    public static string ToHex(Color c) =>
        c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>
    /// Yields <paramref name="fallback"/> when the input is null, empty, or malformed.
    /// </summary>
    public static Color ParseHexOrDefault(string? hex, Color fallback) =>
        ColorMath.TryParseHexOrNull(hex) ?? fallback;

    public Color? Resolve(bool isLightTheme) => isLightTheme ? LightColor : DarkColor;
}
