using Avalonia.Media;

namespace TrayAppDotNETCommon.Utils;

public static class ColorMath
{
    public static Color ParseHexStrict(string hex)
    {
        if (TryParseHexOrNull(hex) is { } color) return color;
        throw new FormatException($"Invalid color hex '{hex}'.");
    }

    public static Color? TryParseHexOrNull(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;

        string s = hex.Trim();
        if (s.StartsWith('#')) s = s[1..];

        try
        {
            return s.Length switch
            {
                6 => Color.FromRgb(
                    Convert.ToByte(s[..2], 16),
                    Convert.ToByte(s[2..4], 16),
                    Convert.ToByte(s[4..6], 16)),
                8 => Color.FromArgb(
                    Convert.ToByte(s[..2], 16),
                    Convert.ToByte(s[2..4], 16),
                    Convert.ToByte(s[4..6], 16),
                    Convert.ToByte(s[6..8], 16)),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }
}
