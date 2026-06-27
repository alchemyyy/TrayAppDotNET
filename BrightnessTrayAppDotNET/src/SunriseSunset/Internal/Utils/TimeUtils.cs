namespace BrightnessTrayAppDotNET.SunriseSunset.Internal.Utils;

internal static class TimeUtils
{
    /// <summary>Convert day fraction (0-1) to a local hour in [0, 24).</summary>
    public static double DayfracToLocalHr(double dayfrac, double timezone)
        => 24.0 * MathUtils.LimitZero2One(dayfrac + timezone / 24.0);

    /// <summary>
    /// Build a <see cref="DateTimeOffset"/> from a local-clock fractional hour and timezone offset,
    /// anchored on the supplied calendar date.
    /// Returns null for non-finite or negative inputs (polar day / night sentinel).
    /// </summary>
    public static DateTimeOffset? FractionalHourToDate(
        int year,
        int month,
        int day,
        double fractionalHour,
        double timezoneOffsetHours)
    {
        if (double.IsNaN(fractionalHour) || double.IsInfinity(fractionalHour) || fractionalHour < 0) return null;

        long totalTicks = (long)Math.Round(fractionalHour * TimeSpan.TicksPerHour);
        DateTime localMidnight = new(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);
        DateTime localDateTime = localMidnight.AddTicks(totalTicks);

        TimeSpan offset = TimeSpan.FromHours(timezoneOffsetHours);
        return new DateTimeOffset(localDateTime, offset);
    }
}
