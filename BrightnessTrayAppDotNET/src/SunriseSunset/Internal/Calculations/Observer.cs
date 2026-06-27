using BrightnessTrayAppDotNET.SunriseSunset.Internal.Utils;

namespace BrightnessTrayAppDotNET.SunriseSunset.Internal.Calculations;

internal readonly record struct ParallaxResult(double DeltaAlpha, double DeltaPrime);

internal static class Observer
{
    public static double GreenwichMeanSiderealTime(double jd, double jc)
    {
        return MathUtils.LimitDegrees(
            280.46061837 +
            360.98564736629 * (jd - 2451545.0) +
            jc * jc * (0.000387933 - jc / 38710000.0));
    }

    public static double GreenwichSiderealTime(double nu0, double deltaPsi, double epsilon)
        => nu0 + deltaPsi * Math.Cos(MathUtils.Deg2Rad(epsilon));

    public static double HourAngle(double nu, double longitude, double alphaDeg)
        => MathUtils.LimitDegrees(nu + longitude - alphaDeg);

    public static ParallaxResult RightAscensionParallaxAndTopocentricDec(
        double latitude,
        double elevation,
        double xi,
        double h,
        double delta)
    {
        double latRad = MathUtils.Deg2Rad(latitude);
        double xiRad = MathUtils.Deg2Rad(xi);
        double hRad = MathUtils.Deg2Rad(h);
        double deltaRad = MathUtils.Deg2Rad(delta);

        double u = Math.Atan(0.99664719 * Math.Tan(latRad));
        double y = 0.99664719 * Math.Sin(u) + (elevation * Math.Sin(latRad)) / 6378140.0;
        double x = Math.Cos(u) + (elevation * Math.Cos(latRad)) / 6378140.0;

        double deltaAlphaRad = Math.Atan2(
            -x * Math.Sin(xiRad) * Math.Sin(hRad),
            Math.Cos(deltaRad) - x * Math.Sin(xiRad) * Math.Cos(hRad));

        double deltaPrime = MathUtils.Rad2Deg(
            Math.Atan2(
                (Math.Sin(deltaRad) - y * Math.Sin(xiRad)) * Math.Cos(deltaAlphaRad),
                Math.Cos(deltaRad) - x * Math.Sin(xiRad) * Math.Cos(hRad)));

        return new ParallaxResult(MathUtils.Rad2Deg(deltaAlphaRad), deltaPrime);
    }

    public static double TopocentricRightAscension(double alphaDeg, double deltaAlpha)
        => alphaDeg + deltaAlpha;

    public static double TopocentricLocalHourAngle(double h, double deltaAlpha)
        => h - deltaAlpha;

    public static double TopocentricElevationAngle(double latitude, double deltaPrime, double hPrime)
    {
        double latRad = MathUtils.Deg2Rad(latitude);
        double deltaPrimeRad = MathUtils.Deg2Rad(deltaPrime);

        return MathUtils.Rad2Deg(
            Math.Asin(
                Math.Sin(latRad) * Math.Sin(deltaPrimeRad)
                + Math.Cos(latRad) * Math.Cos(deltaPrimeRad) * Math.Cos(MathUtils.Deg2Rad(hPrime))));
    }

    public static double AtmosphericRefractionCorrection(
        double pressure,
        double temperature,
        double atmosphericRefraction,
        double e0)
    {
        if (e0 < -1 * (SPAConstants.SunRadius + atmosphericRefraction)) return 0.0;

        return (pressure / 1010.0)
               * (283.0 / (273.0 + temperature))
               * (1.02 / (60.0 * Math.Tan(MathUtils.Deg2Rad(e0 + 10.3 / (e0 + 5.11)))));
    }

    public static double TopocentricElevationAngleCorrected(double e0, double deltaE)
        => e0 + deltaE;

    public static double TopocentricZenithAngle(double e) => 90.0 - e;

    public static double TopocentricAzimuthAngleAstro(double hPrime, double latitude, double deltaPrime)
    {
        double hPrimeRad = MathUtils.Deg2Rad(hPrime);
        double latRad = MathUtils.Deg2Rad(latitude);

        return MathUtils.LimitDegrees(
            MathUtils.Rad2Deg(
                Math.Atan2(
                    Math.Sin(hPrimeRad),
                    Math.Cos(hPrimeRad) * Math.Sin(latRad)
                    - Math.Tan(MathUtils.Deg2Rad(deltaPrime)) * Math.Cos(latRad))));
    }

    public static double TopocentricAzimuthAngle(double azimuthAstro)
        => MathUtils.LimitDegrees(azimuthAstro + 180.0);

    public static double SurfaceIncidenceAngle(
        double zenith,
        double azimuthAstro,
        double azimuthRotation,
        double slope)
    {
        double zenithRad = MathUtils.Deg2Rad(zenith);
        double slopeRad = MathUtils.Deg2Rad(slope);

        return MathUtils.Rad2Deg(
            Math.Acos(
                Math.Cos(zenithRad) * Math.Cos(slopeRad)
                + Math.Sin(slopeRad)
                * Math.Sin(zenithRad)
                * Math.Cos(MathUtils.Deg2Rad(azimuthAstro - azimuthRotation))));
    }
}
