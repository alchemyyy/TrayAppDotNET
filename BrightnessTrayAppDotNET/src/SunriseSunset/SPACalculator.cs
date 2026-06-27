using BrightnessTrayAppDotNET.SunriseSunset.Internal;
using BrightnessTrayAppDotNET.SunriseSunset.Internal.Calculations;
using BrightnessTrayAppDotNET.SunriseSunset.Internal.Utils;

namespace BrightnessTrayAppDotNET.SunriseSunset;

/// <summary>Public API for sunrise / sunset / solar position calculations based on the NREL SPA</summary>
public static class SPACalculator
{
    /// <summary>Get the sunrise time for a given location and date</summary>
    /// <param name="latitude">Observer latitude in degrees (positive north)</param>
    /// <param name="longitude">Observer longitude in degrees (positive east)</param>
    /// <param name="date">Date for the calculation; defaults to <see cref="DateTimeOffset.Now"/></param>
    /// <param name="options">Optional SPA configuration</param>
    /// <returns>Sunrise instant, or null on polar night or calculation error</returns>
    public static DateTimeOffset? GetSunrise(
        double latitude,
        double longitude,
        DateTimeOffset? date = null,
        SPAOptions? options = null)
    {
        SPAData spa = SPACore.Initialize(date ?? DateTimeOffset.Now, latitude, longitude, options);
        if (SPACore.Calculate(spa) != 0 || !SPACore.IsValidSunTime(spa.Sunrise)) return null;
        return TimeUtils.FractionalHourToDate(spa.Year, spa.Month, spa.Day, spa.Sunrise, spa.Timezone);
    }

    /// <summary>Get the sunset time for a given location and date</summary>
    public static DateTimeOffset? GetSunset(
        double latitude,
        double longitude,
        DateTimeOffset? date = null,
        SPAOptions? options = null)
    {
        SPAData spa = SPACore.Initialize(date ?? DateTimeOffset.Now, latitude, longitude, options);
        if (SPACore.Calculate(spa) != 0 || !SPACore.IsValidSunTime(spa.Sunset)) return null;
        return TimeUtils.FractionalHourToDate(spa.Year, spa.Month, spa.Day, spa.Sunset, spa.Timezone);
    }

    /// <summary>Get solar noon (sun transit) time for a given location and date</summary>
    public static DateTimeOffset? GetSolarNoon(
        double latitude,
        double longitude,
        DateTimeOffset? date = null,
        SPAOptions? options = null)
    {
        SPAData spa = SPACore.Initialize(date ?? DateTimeOffset.Now, latitude, longitude, options);
        if (SPACore.Calculate(spa) != 0 || !SPACore.IsValidSunTime(spa.Suntransit)) return null;
        return TimeUtils.FractionalHourToDate(spa.Year, spa.Month, spa.Day, spa.Suntransit, spa.Timezone);
    }

    /// <summary>Get the current solar position (zenith, azimuth, elevation, etc.) for the given instant</summary>
    public static SolarPosition? GetSolarPosition(
        double latitude,
        double longitude,
        DateTimeOffset? date = null,
        SPAOptions? options = null)
    {
        SPAData spa = SPACore.Initialize(date ?? DateTimeOffset.Now, latitude, longitude, options);
        if (SPACore.Calculate(spa) != 0) return null;
        return new SolarPosition(
            Zenith: spa.Zenith,
            Azimuth: spa.Azimuth,
            AzimuthAstro: spa.AzimuthAstro,
            Elevation: spa.E,
            RightAscension: spa.Alpha,
            Declination: spa.Delta,
            HourAngle: spa.H);
    }

    /// <summary>Get civil, nautical and astronomical twilight, plus golden- and blue-hour windows</summary>
    public static TwilightTimes? GetTwilight(
        double latitude,
        double longitude,
        DateTimeOffset? date = null,
        SPAOptions? options = null)
    {
        SPAData spa = SPACore.Initialize(date ?? DateTimeOffset.Now, latitude, longitude, options);
        if (SPACore.Calculate(spa) != 0 || !SPACore.IsValidSunTime(spa.Suntransit)) return null;
        return BuildTwilight(spa, latitude);
    }

    /// <summary>Get sunrise, sunset, solar noon and twilight values in a single call</summary>
    public static SunTimes GetSunTimes(
        double latitude,
        double longitude,
        DateTimeOffset? date = null,
        SPAOptions? options = null)
    {
        SPAData spa = SPACore.Initialize(date ?? DateTimeOffset.Now, latitude, longitude, options);
        if (SPACore.Calculate(spa) != 0) return new SunTimes(null, null, null, null);

        DateTimeOffset? sunrise = SPACore.IsValidSunTime(spa.Sunrise)
            ? TimeUtils.FractionalHourToDate(spa.Year, spa.Month, spa.Day, spa.Sunrise, spa.Timezone)
            : null;
        DateTimeOffset? sunset = SPACore.IsValidSunTime(spa.Sunset)
            ? TimeUtils.FractionalHourToDate(spa.Year, spa.Month, spa.Day, spa.Sunset, spa.Timezone)
            : null;
        DateTimeOffset? solarNoon = SPACore.IsValidSunTime(spa.Suntransit)
            ? TimeUtils.FractionalHourToDate(spa.Year, spa.Month, spa.Day, spa.Suntransit, spa.Timezone)
            : null;

        TwilightTimes? twilight = SPACore.IsValidSunTime(spa.Suntransit) ? BuildTwilight(spa, latitude) : null;

        return new SunTimes(sunrise, sunset, solarNoon, twilight);
    }

    private static TwilightTimes BuildTwilight(SPAData spa, double latitude)
    {
        CustomZenithTimes civil = Rts.CalculateCustomZenithTimes(
            latitude, spa.Delta, spa.Suntransit, SPAConstants.ZenithCivilTwilight);
        CustomZenithTimes nautical = Rts.CalculateCustomZenithTimes(
            latitude, spa.Delta, spa.Suntransit, SPAConstants.ZenithNauticalTwilight);
        CustomZenithTimes astronomical = Rts.CalculateCustomZenithTimes(
            latitude, spa.Delta, spa.Suntransit, SPAConstants.ZenithAstronomicalTwilight);
        CustomZenithTimes golden = Rts.CalculateCustomZenithTimes(
            latitude, spa.Delta, spa.Suntransit, SPAConstants.ZenithGoldenHour);
        CustomZenithTimes blue = Rts.CalculateCustomZenithTimes(
            latitude, spa.Delta, spa.Suntransit, SPAConstants.ZenithBlueHour);

        DateTimeOffset? ToDate(double? hours)
        {
            if (hours is null) return null;
            double v = hours.Value;
            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0 || v > 24) return null;
            return TimeUtils.FractionalHourToDate(spa.Year, spa.Month, spa.Day, v, spa.Timezone);
        }

        DateTimeOffset? ValidSunDate(double hours)
            => SPACore.IsValidSunTime(hours)
                ? TimeUtils.FractionalHourToDate(spa.Year, spa.Month, spa.Day, hours, spa.Timezone)
                : null;

        DateTimeOffset? sunriseDate = ValidSunDate(spa.Sunrise);
        DateTimeOffset? sunsetDate = ValidSunDate(spa.Sunset);

        return new TwilightTimes(
            CivilDawn: ToDate(civil.Sunrise),
            CivilDusk: ToDate(civil.Sunset),
            NauticalDawn: ToDate(nautical.Sunrise),
            NauticalDusk: ToDate(nautical.Sunset),
            AstronomicalDawn: ToDate(astronomical.Sunrise),
            AstronomicalDusk: ToDate(astronomical.Sunset),
            GoldenHour: new HourPeriod(
                Morning: new TwilightWindow(sunriseDate, ToDate(golden.Sunrise)),
                Evening: new TwilightWindow(ToDate(golden.Sunset), sunsetDate)),
            BlueHour: new HourPeriod(
                Morning: new TwilightWindow(ToDate(blue.Sunrise), sunriseDate),
                Evening: new TwilightWindow(sunsetDate, ToDate(blue.Sunset))));
    }
}
