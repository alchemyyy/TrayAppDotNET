using BrightnessTrayAppDotNET.SunriseSunset.Internal.Utils;

namespace BrightnessTrayAppDotNET.SunriseSunset.Internal.Calculations;

internal static class Sun
{
    public static double GeocentricLongitude(double l)
    {
        double theta = l + 180.0;
        if (theta >= 360.0) theta -= 360.0;
        return theta;
    }

    public static double GeocentricLatitude(double b) => -b;

    public static double AberrationCorrection(double r) => -20.4898 / (3600.0 * r);

    public static double ApparentSunLongitude(double theta, double deltaPsi, double deltaTau)
        => theta + deltaPsi + deltaTau;

    public static double GeocentricRightAscension(double lamda, double epsilon, double beta)
    {
        double lamdaRad = MathUtils.Deg2Rad(lamda);
        double epsilonRad = MathUtils.Deg2Rad(epsilon);

        return MathUtils.LimitDegrees(
            MathUtils.Rad2Deg(
                Math.Atan2(
                    Math.Sin(lamdaRad) * Math.Cos(epsilonRad)
                    - Math.Tan(MathUtils.Deg2Rad(beta)) * Math.Sin(epsilonRad),
                    Math.Cos(lamdaRad))));
    }

    public static double GeocentricDeclination(double beta, double epsilon, double lamda)
    {
        double betaRad = MathUtils.Deg2Rad(beta);
        double epsilonRad = MathUtils.Deg2Rad(epsilon);

        return MathUtils.Rad2Deg(
            Math.Asin(
                Math.Sin(betaRad) * Math.Cos(epsilonRad)
                + Math.Cos(betaRad) * Math.Sin(epsilonRad) * Math.Sin(MathUtils.Deg2Rad(lamda))));
    }

    public static double SunMeanLongitude(double jme)
    {
        return MathUtils.LimitDegrees(
            280.4664567 +
            jme * (360007.6982779 +
                   jme * (0.03032028 +
                          jme * (1.0 / 49931.0 +
                                 jme * (-1.0 / 15300.0 +
                                        jme * (-1.0 / 2000000.0))))));
    }

    public static double EquatorialHorizontalParallax(double r) => 8.794 / (3600.0 * r);
}
