using System.Globalization;
using BrightnessTrayAppDotNET.Utils;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private void StampSunShiftAnchor(EnvironmentalCurve curve)
    {
        curve.LastSunShiftDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        curve.LastSunShiftLatitude = _settings.EnvironmentalLatitude;
        curve.LastSunShiftLongitude = _settings.EnvironmentalLongitude;
        curve.LastSunShiftUseDaylightSavings = curve.UseDaylightSavings;
    }

    private void BootstrapSunShiftAnchor(EnvironmentalCurve curve)
    {
        if (!curve.FollowTheSun) return;
        if (IsValidCoordinate(curve.LastSunShiftLatitude, curve.LastSunShiftLongitude)) return;
        if (!IsValidCoordinate(_settings.EnvironmentalLatitude, _settings.EnvironmentalLongitude)) return;
        StampSunShiftAnchor(curve);
        _profileManager?.Save();
    }

    private void StampDisabledPeriodSunShiftAnchor(EnvironmentalCurve curve)
    {
        curve.LastDisabledPeriodSunShiftDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        curve.LastDisabledPeriodSunShiftLatitude = _settings.EnvironmentalLatitude;
        curve.LastDisabledPeriodSunShiftLongitude = _settings.EnvironmentalLongitude;
        curve.LastDisabledPeriodSunShiftUseDaylightSavings = curve.UseDaylightSavings;
    }

    private void BootstrapDisabledPeriodSunShiftAnchor(EnvironmentalCurve curve)
    {
        if (!curve.DisabledPeriodFollowTheSun) return;
        if (IsValidCoordinate(curve.LastDisabledPeriodSunShiftLatitude,
                curve.LastDisabledPeriodSunShiftLongitude)) return;
        if (!IsValidCoordinate(_settings.EnvironmentalLatitude, _settings.EnvironmentalLongitude)) return;
        StampDisabledPeriodSunShiftAnchor(curve);
        _profileManager?.Save();
    }

    private (double Start, double End) ResolveDisplayDisabledPeriod(EnvironmentalCurve stored, DateTime target)
    {
        if (!stored.DisabledPeriodFollowTheSun) return (stored.DisabledPeriodStart, stored.DisabledPeriodEnd);

        double toLatitude = _settings.EnvironmentalLatitude;
        double toLongitude = _settings.EnvironmentalLongitude;
        if (!IsValidCoordinate(toLatitude, toLongitude)) return (stored.DisabledPeriodStart, stored.DisabledPeriodEnd);

        if (string.IsNullOrEmpty(stored.LastDisabledPeriodSunShiftDate)
            || !DateTime.TryParseExact(
                stored.LastDisabledPeriodSunShiftDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime fromDate))
            return (stored.DisabledPeriodStart, stored.DisabledPeriodEnd);

        double fromLatitude;
        double fromLongitude;
        if (IsValidCoordinate(stored.LastDisabledPeriodSunShiftLatitude, stored.LastDisabledPeriodSunShiftLongitude))
        {
            fromLatitude = stored.LastDisabledPeriodSunShiftLatitude;
            fromLongitude = stored.LastDisabledPeriodSunShiftLongitude;
        }
        else
        {
            fromLatitude = toLatitude;
            fromLongitude = toLongitude;
        }

        bool toUseDST = stored.UseDaylightSavings;
        bool fromUseDST = stored.LastDisabledPeriodSunShiftUseDaylightSavings;
        SunAnchor from = new(fromDate, fromLatitude, fromLongitude, fromUseDST);
        SunAnchor to = new(target, toLatitude, toLongitude, toUseDST);

        if (fromDate.Date == target
            && fromLatitude == toLatitude
            && fromLongitude == toLongitude
            && fromUseDST == toUseDST)
            return (stored.DisabledPeriodStart, stored.DisabledPeriodEnd);

        return (
            SunShifter.ShiftTime(stored.DisabledPeriodStart, from, to),
            SunShifter.ShiftTime(stored.DisabledPeriodEnd, from, to));
    }
}
