using BrightnessTrayAppDotNET.SunriseSunset.Internal.Calculations;
using BrightnessTrayAppDotNET.SunriseSunset.Internal.Utils;

namespace BrightnessTrayAppDotNET.SunriseSunset.Internal;

/// <summary>
/// Core SPA orchestrator.
/// Validates inputs, calculates Julian day and geocentric position,
/// then derives observer-specific topocentric and rise/transit/set values.
/// </summary>
internal static class SPACore
{
    /// <summary>
    /// Validate SPA input values.
    /// Returns 0 on success, or a non-zero error code.
    /// </summary>
    public static int ValidateInputs(SPAData spa)
    {
        if (spa.Year is < -2000 or > 6000) return 1;
        if (spa.Month is < 1 or > 12) return 2;
        if (spa.Day is < 1 or > 31) return 3;
        if (spa.Hour is < 0 or > 24) return 4;
        if (spa.Minute is < 0 or > 59) return 5;
        if (spa.Second is < 0 or >= 60) return 6;
        if (spa.Pressure is < 0 or > 5000) return 12;
        if (spa.Temperature is <= -273 or > 6000) return 13;
        if (spa.DeltaUt1 is <= -1 or >= 1) return 17;
        if (spa is { Hour: 24, Minute: > 0 }) return 5;
        if (spa is { Hour: 24, Second: > 0 }) return 6;
        if (Math.Abs(spa.DeltaT) > 8000) return 7;
        if (Math.Abs(spa.Timezone) > 18) return 8;
        if (Math.Abs(spa.Longitude) > 180) return 9;
        if (Math.Abs(spa.Latitude) > 90) return 10;
        if (Math.Abs(spa.AtmosphericRefraction) > 5) return 16;
        if (spa.Elevation < -6500000) return 11;
        return 0;
    }

    /// <summary>Calculate the geocentric sun right ascension and declination.</summary>
    public static void CalculateGeocentricSunRaAndDec(SPAData spa)
    {
        spa.Jc = DateUtils.JulianCentury(spa.Jd);
        spa.Jde = DateUtils.JulianEphemerisDay(spa.Jd, spa.DeltaT);
        spa.Jce = DateUtils.JulianEphemerisCentury(spa.Jde);
        spa.Jme = DateUtils.JulianEphemerisMillennium(spa.Jce);

        spa.L = Earth.HeliocentricLongitude(spa.Jme);
        spa.B = Earth.HeliocentricLatitude(spa.Jme);
        spa.R = Earth.RadiusVector(spa.Jme);

        spa.Theta = Sun.GeocentricLongitude(spa.L);
        spa.Beta = Sun.GeocentricLatitude(spa.B);

        spa.X0 = Nutation.MeanElongationMoonSun(spa.Jce);
        spa.X1 = Nutation.MeanAnomalySun(spa.Jce);
        spa.X2 = Nutation.MeanAnomalyMoon(spa.Jce);
        spa.X3 = Nutation.ArgumentLatitudeMoon(spa.Jce);
        spa.X4 = Nutation.AscendingLongitudeMoon(spa.Jce);

        double[] x = [spa.X0, spa.X1, spa.X2, spa.X3, spa.X4];
        NutationResult nutation = Nutation.LongitudeAndObliquity(spa.Jce, x);
        spa.DelPsi = nutation.DelPsi;
        spa.DelEpsilon = nutation.DelEpsilon;

        spa.Epsilon0 = Nutation.EclipticMeanObliquity(spa.Jme);
        spa.Epsilon = Nutation.EclipticTrueObliquity(spa.DelEpsilon, spa.Epsilon0);

        spa.DelTau = Sun.AberrationCorrection(spa.R);
        spa.Lamda = Sun.ApparentSunLongitude(spa.Theta, spa.DelPsi, spa.DelTau);

        spa.Nu0 = Observer.GreenwichMeanSiderealTime(spa.Jd, spa.Jc);
        spa.Nu = Observer.GreenwichSiderealTime(spa.Nu0, spa.DelPsi, spa.Epsilon);

        spa.Alpha = Sun.GeocentricRightAscension(spa.Lamda, spa.Epsilon, spa.Beta);
        spa.Delta = Sun.GeocentricDeclination(spa.Beta, spa.Epsilon, spa.Lamda);
    }

    private static RaDecResult CalculateRaDecForJd(double jd, double deltaT)
    {
        double jc = DateUtils.JulianCentury(jd);
        double jde = DateUtils.JulianEphemerisDay(jd, deltaT);
        double jce = DateUtils.JulianEphemerisCentury(jde);
        double jme = DateUtils.JulianEphemerisMillennium(jce);

        double l = Earth.HeliocentricLongitude(jme);
        double b = Earth.HeliocentricLatitude(jme);
        double r = Earth.RadiusVector(jme);

        double theta = Sun.GeocentricLongitude(l);
        double beta = Sun.GeocentricLatitude(b);

        double x0 = Nutation.MeanElongationMoonSun(jce);
        double x1 = Nutation.MeanAnomalySun(jce);
        double x2 = Nutation.MeanAnomalyMoon(jce);
        double x3 = Nutation.ArgumentLatitudeMoon(jce);
        double x4 = Nutation.AscendingLongitudeMoon(jce);

        NutationResult nutation = Nutation.LongitudeAndObliquity(jce, [x0, x1, x2, x3, x4]);

        double epsilon0 = Nutation.EclipticMeanObliquity(jme);
        double epsilon = Nutation.EclipticTrueObliquity(nutation.DelEpsilon, epsilon0);

        double delTau = Sun.AberrationCorrection(r);
        double lamda = Sun.ApparentSunLongitude(theta, nutation.DelPsi, delTau);

        double nu0 = Observer.GreenwichMeanSiderealTime(jd, jc);
        double nu = Observer.GreenwichSiderealTime(nu0, nutation.DelPsi, epsilon);

        double alpha = Sun.GeocentricRightAscension(lamda, epsilon, beta);
        double delta = Sun.GeocentricDeclination(beta, epsilon, lamda);

        return new RaDecResult(alpha, delta, nu);
    }

    /// <summary>
    /// Run the full SPA calculation including topocentric corrections and RTS times.
    /// Returns 0 on success or the validation error code.
    /// </summary>
    public static int Calculate(SPAData spa)
    {
        int result = ValidateInputs(spa);
        if (result != 0) return result;

        spa.Jd = DateUtils.JulianDay(
            spa.Year,
            spa.Month,
            spa.Day,
            spa.Hour,
            spa.Minute,
            spa.Second,
            spa.DeltaUt1,
            spa.Timezone);

        CalculateGeocentricSunRaAndDec(spa);

        spa.H = Observer.HourAngle(spa.Nu, spa.Longitude, spa.Alpha);
        spa.Xi = Sun.EquatorialHorizontalParallax(spa.R);

        ParallaxResult parallax = Observer.RightAscensionParallaxAndTopocentricDec(
            spa.Latitude,
            spa.Elevation,
            spa.Xi,
            spa.H,
            spa.Delta);
        spa.DelAlpha = parallax.DeltaAlpha;
        spa.DeltaPrime = parallax.DeltaPrime;

        spa.AlphaPrime = Observer.TopocentricRightAscension(spa.Alpha, spa.DelAlpha);
        spa.HPrime = Observer.TopocentricLocalHourAngle(spa.H, spa.DelAlpha);

        spa.E0 = Observer.TopocentricElevationAngle(spa.Latitude, spa.DeltaPrime, spa.HPrime);
        spa.DelE = Observer.AtmosphericRefractionCorrection(
            spa.Pressure,
            spa.Temperature,
            spa.AtmosphericRefraction,
            spa.E0);
        spa.E = Observer.TopocentricElevationAngleCorrected(spa.E0, spa.DelE);

        spa.Zenith = Observer.TopocentricZenithAngle(spa.E);
        spa.AzimuthAstro = Observer.TopocentricAzimuthAngleAstro(spa.HPrime, spa.Latitude, spa.DeltaPrime);
        spa.Azimuth = Observer.TopocentricAzimuthAngle(spa.AzimuthAstro);

        spa.Incidence = Observer.SurfaceIncidenceAngle(
            spa.Zenith,
            spa.AzimuthAstro,
            spa.AzimuthRotation,
            spa.Slope);

        RtsResult rts = Rts.CalculateEotAndSunRiseTransitSet(spa, CalculateRaDecForJd);
        spa.Sunrise = rts.Sunrise;
        spa.Suntransit = rts.Suntransit;
        spa.Sunset = rts.Sunset;
        spa.Srha = rts.Srha;
        spa.Ssha = rts.Ssha;
        spa.Sta = rts.Sta;
        spa.Eot = rts.Eot;

        return 0;
    }

    /// <summary>
    /// Initialize an <see cref="SPAData"/> object from a <see cref="DateTimeOffset"/>, observer location, and options.
    /// Resolves calendar components by timezone precedence: numeric override -> IANA id -> input offset.
    /// </summary>
    public static SPAData Initialize(
        DateTimeOffset date,
        double latitude,
        double longitude,
        SPAOptions? options)
    {
        SPAOptions opts = options ?? new SPAOptions();
        SPAData spa = new();
        DateTimeComponents components =
            DateUtils.ResolveDateTimeComponents(date, opts.TimezoneOffsetHours, opts.TimezoneId);

        spa.Year = components.Year;
        spa.Month = components.Month;
        spa.Day = components.Day;
        spa.Hour = components.Hour;
        spa.Minute = components.Minute;
        spa.Second = components.Second;
        spa.Timezone = components.TimezoneOffsetHours;

        spa.TimezoneId = opts.TimezoneId ?? string.Empty;

        spa.Latitude = latitude;
        spa.Longitude = longitude;

        spa.Elevation = opts.ElevationMeters;
        spa.Pressure = opts.Pressure;
        spa.Temperature = opts.Temperature;
        spa.DeltaUt1 = opts.DeltaUt1;
        spa.DeltaT = opts.DeltaT;
        spa.Slope = opts.Slope;
        spa.AzimuthRotation = opts.AzimuthRotation;
        spa.AtmosphericRefraction = opts.AtmosphericRefraction;

        return spa;
    }

    /// <summary>True for any non-polar, finite, non-negative fractional hour value.</summary>
    public static bool IsValidSunTime(double time)
        => time != SPAConstants.InvalidValue && !double.IsNaN(time) && !double.IsInfinity(time) && time >= 0;
}
