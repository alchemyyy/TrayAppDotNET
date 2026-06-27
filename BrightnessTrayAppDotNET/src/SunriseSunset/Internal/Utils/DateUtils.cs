namespace BrightnessTrayAppDotNET.SunriseSunset.Internal.Utils;

internal readonly record struct DateTimeComponents(
    int Year,
    int Month,
    int Day,
    int Hour,
    int Minute,
    double Second,
    double TimezoneOffsetHours);

internal static class DateUtils
{
    /// <summary>Calculate the Julian Day from calendar date and time.</summary>
    public static double JulianDay(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        double second,
        double deltaUt1,
        double timezone)
    {
        int y = year;
        int m = month;

        double dayDecimal =
            day + (hour - timezone + (minute + (second + deltaUt1) / 60.0) / 60.0) / 24.0;

        if (m < 3)
        {
            m += 12;
            y--;
        }

        double jd =
            Math.Floor(365.25 * (y + 4716.0)) +
            Math.Floor(30.6001 * (m + 1)) +
            dayDecimal -
            1524.5;

        if (jd > 2299160.0)
        {
            double a = Math.Floor(y / 100.0);
            jd += 2 - a + Math.Floor(a / 4);
        }

        return jd;
    }

    public static double JulianCentury(double jd) => (jd - 2451545.0) / 36525.0;

    public static double JulianEphemerisDay(double jd, double deltaT) => jd + deltaT / 86400.0;

    public static double JulianEphemerisCentury(double jde) => (jde - 2451545.0) / 36525.0;

    public static double JulianEphemerisMillennium(double jce) => jce / 10.0;

    /// <summary>
    /// Resolve the calendar date/time context used by SPA calculations.
    /// Precedence: explicit numeric offset, then IANA timezone id, then the offset on the input.
    /// </summary>
    public static DateTimeComponents ResolveDateTimeComponents(
        DateTimeOffset date,
        double? timezoneOffsetHours,
        string? timezoneId)
    {
        if (timezoneOffsetHours.HasValue)
        {
            DateTimeOffset shifted = date.ToOffset(TimeSpan.FromHours(timezoneOffsetHours.Value));
            return ToComponents(shifted, timezoneOffsetHours.Value);
        }

        if (!string.IsNullOrEmpty(timezoneId))
        {
            try
            {
                TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
                DateTimeOffset converted = TimeZoneInfo.ConvertTime(date, tz);
                return ToComponents(converted, converted.Offset.TotalHours);
            }
            catch (TimeZoneNotFoundException)
            {
                /* fall through */
            }
            catch (InvalidTimeZoneException)
            {
                /* fall through */
            }
        }

        return ToComponents(date, date.Offset.TotalHours);
    }

    private static DateTimeComponents ToComponents(DateTimeOffset value, double offsetHours)
    {
        return new DateTimeComponents(
            Year: value.Year,
            Month: value.Month,
            Day: value.Day,
            Hour: value.Hour,
            Minute: value.Minute,
            Second: value.Second + value.Millisecond / 1000.0 + value.Microsecond / 1_000_000.0,
            TimezoneOffsetHours: offsetHours);
    }
}
