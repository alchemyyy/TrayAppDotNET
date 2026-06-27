using System.Globalization;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private static string FormatCoordinate(double value) =>
        value.ToString("F7", CultureInfo.InvariantCulture);

    private static bool TryParseCoordinate(string? text, out double value) =>
        double.TryParse(
            text?.Trim() ?? string.Empty,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);

    private static bool IsValidCoordinate(double latitude, double longitude) =>
        !((latitude == 0.0 && longitude == 0.0)
          || latitude < -90.0 || latitude > 90.0
          || longitude < -180.0 || longitude > 180.0);

    private static string FormatSunOverlayDate(DateTime date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryParseSunOverlayDate(string text, out DateTime result)
    {
        if (DateTime.TryParseExact(
                text,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out result))
            return true;

        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out result);
    }

    private static string FormatDisabledPeriodTime(double t)
    {
        bool use24 = !CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('h');
        int totalMinutes = (int)Math.Round(Math.Clamp(t, 0.0, 1.0) * 24 * 60);
        if (totalMinutes >= 24 * 60) totalMinutes = 24 * 60 - 1;
        int hour = totalMinutes / 60;
        int minute = totalMinutes % 60;

        if (use24) return $"{hour:D2}:{minute:D2}";

        (int displayHour, string suffix) = hour switch
        {
            0 => (12, "am"),
            < 12 => (hour, "am"),
            12 => (12, "pm"),
            _ => (hour - 12, "pm"),
        };

        return $"{displayHour}:{minute:D2}{suffix}";
    }

    private static bool TryParseDisabledPeriodTime(string text, out double dayFraction)
    {
        dayFraction = 0.0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string value = text.Trim().ToLowerInvariant();
        bool isPM = false;
        bool isAM = false;
        if (value.EndsWith("pm", StringComparison.Ordinal))
        {
            isPM = true;
            value = value[..^2].TrimEnd();
        }
        else if (value.EndsWith("am", StringComparison.Ordinal))
        {
            isAM = true;
            value = value[..^2].TrimEnd();
        }
        else if (value.EndsWith('p'))
        {
            isPM = true;
            value = value[..^1].TrimEnd();
        }
        else if (value.EndsWith('a'))
        {
            isAM = true;
            value = value[..^1].TrimEnd();
        }

        int hour;
        int minute;
        int colonIndex = value.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex >= 0)
        {
            if (!int.TryParse(value[..colonIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out hour))
                return false;
            if (!int.TryParse(value[(colonIndex + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out minute)) return false;
        }
        else
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out hour)) return false;
            minute = 0;
        }

        if (minute is < 0 or >= 60) return false;

        if (isAM || isPM)
        {
            if (hour is < 1 or > 12) return false;
            if (isAM && hour == 12) hour = 0;
            else if (isPM && hour != 12) hour += 12;
        }
        else
        {
            switch (hour)
            {
                case 24 when minute == 0:
                    hour = 0;
                    break;
                case < 0 or > 23:
                    return false;
            }
        }

        dayFraction = (hour * 60 + minute) / (24.0 * 60.0);
        return true;
    }
}
