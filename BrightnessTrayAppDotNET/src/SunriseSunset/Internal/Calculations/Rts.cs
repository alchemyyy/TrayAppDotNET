using BrightnessTrayAppDotNET.SunriseSunset.Internal.Utils;

namespace BrightnessTrayAppDotNET.SunriseSunset.Internal.Calculations;

internal readonly record struct RaDecResult(double Alpha, double Delta, double Nu);

internal readonly record struct RtsResult(
    double Sunrise,
    double Suntransit,
    double Sunset,
    double Srha,
    double Ssha,
    double Sta,
    double Eot);

internal readonly record struct CustomZenithTimes(double? Sunrise, double? Sunset);

internal static class Rts
{
    private const int JdMinus = 0;
    private const int JdZero = 1;
    private const int JdPlus = 2;
    private const int JdCount = 3;

    private const int SunTransit = 0;
    private const int SunRise = 1;
    private const int SunSet = 2;
    private const int SunCount = 3;

    /// <summary>Sun hour angle at rise/set for a given zenith. Returns INVALID for polar day/night.</summary>
    public static double SunHourAngleAtRiseSet(double latitude, double deltaZero, double h0Prime)
    {
        double latitudeRad = MathUtils.Deg2Rad(latitude);
        double deltaZeroRad = MathUtils.Deg2Rad(deltaZero);

        double argument =
            (Math.Sin(MathUtils.Deg2Rad(h0Prime))
             - Math.Sin(latitudeRad) * Math.Sin(deltaZeroRad))
            / (Math.Cos(latitudeRad) * Math.Cos(deltaZeroRad));

        if (Math.Abs(argument) <= 1) return MathUtils.LimitDegrees180(MathUtils.Rad2Deg(Math.Acos(argument)));

        return SPAConstants.InvalidValue;
    }

    public static double ApproxSunTransitTime(double alphaZero, double longitude, double nu)
        => (alphaZero - longitude - nu) / 360.0;

    public static void ApproxSunRiseAndSet(double[] mRts, double h0)
    {
        double h0Dfrac = h0 / 360.0;
        mRts[SunRise] = MathUtils.LimitZero2One(mRts[SunTransit] - h0Dfrac);
        mRts[SunSet] = MathUtils.LimitZero2One(mRts[SunTransit] + h0Dfrac);
        mRts[SunTransit] = MathUtils.LimitZero2One(mRts[SunTransit]);
    }

    public static double RtsAlphaDeltaPrime(double[] ad, double n)
    {
        double a = ad[JdZero] - ad[JdMinus];
        double b = ad[JdPlus] - ad[JdZero];

        if (Math.Abs(a) >= 2.0) a = MathUtils.LimitZero2One(a);
        if (Math.Abs(b) >= 2.0) b = MathUtils.LimitZero2One(b);

        return ad[JdZero] + (n * (a + b + (b - a) * n)) / 2.0;
    }

    public static double RtsSunAltitude(double latitude, double deltaPrime, double hPrime)
    {
        double latitudeRad = MathUtils.Deg2Rad(latitude);
        double deltaPrimeRad = MathUtils.Deg2Rad(deltaPrime);

        return MathUtils.Rad2Deg(
            Math.Asin(
                Math.Sin(latitudeRad) * Math.Sin(deltaPrimeRad)
                + Math.Cos(latitudeRad) * Math.Cos(deltaPrimeRad) * Math.Cos(MathUtils.Deg2Rad(hPrime))));
    }

    public static double SunRiseAndSet(
        double[] mRts,
        double[] hRts,
        double[] deltaPrime,
        double latitude,
        double[] hPrime,
        double h0Prime,
        int sun)
    {
        return mRts[sun]
               + (hRts[sun] - h0Prime)
               / (360.0
                  * Math.Cos(MathUtils.Deg2Rad(deltaPrime[sun]))
                  * Math.Cos(MathUtils.Deg2Rad(latitude))
                  * Math.Sin(MathUtils.Deg2Rad(hPrime[sun])));
    }

    public static double EquationOfTime(double m, double alpha, double delPsi, double epsilon)
        => MathUtils.LimitMinutes(
            4.0 * (m - 0.0057183 - alpha + delPsi * Math.Cos(MathUtils.Deg2Rad(epsilon))));

    /// <summary>Calculate equation of time and sun rise/transit/set times. Handles high-latitude polar cases.</summary>
    public static RtsResult CalculateEotAndSunRiseTransitSet(
        SPAData spa, Func<double, double, RaDecResult> calculateRaDec)
    {
        double h0Prime = -1 * (SPAConstants.SunRadius + spa.AtmosphericRefraction);

        double sunRtsJd = DateUtils.JulianDay(spa.Year, spa.Month, spa.Day, 0, 0, 0, 0, 0);

        RaDecResult rtsNoon = calculateRaDec(sunRtsJd, spa.DeltaT);
        double nu = rtsNoon.Nu;

        double m = Sun.SunMeanLongitude(spa.Jme);
        double eot = EquationOfTime(m, spa.Alpha, spa.DelPsi, spa.Epsilon);

        double[] alpha = new double[JdCount];
        double[] delta = new double[JdCount];
        for (int i = 0; i < JdCount; i++)
        {
            RaDecResult result = calculateRaDec(sunRtsJd + i - 1, spa.DeltaT);
            alpha[i] = result.Alpha;
            delta[i] = result.Delta;
        }

        double[] mRts = new double[SunCount];
        mRts[SunTransit] = ApproxSunTransitTime(alpha[JdZero], spa.Longitude, nu);

        double h0 = SunHourAngleAtRiseSet(spa.Latitude, delta[JdZero], h0Prime);

        if (h0 == SPAConstants.InvalidValue)
        {
            return new RtsResult(
                Sunrise: SPAConstants.InvalidValue,
                Suntransit: SPAConstants.InvalidValue,
                Sunset: SPAConstants.InvalidValue,
                Srha: SPAConstants.InvalidValue,
                Ssha: SPAConstants.InvalidValue,
                Sta: SPAConstants.InvalidValue,
                Eot: eot);
        }

        ApproxSunRiseAndSet(mRts, h0);

        double[] nuRts = new double[SunCount];
        double[] hPrime = new double[SunCount];
        double[] alphaPrime = new double[SunCount];
        double[] deltaPrime = new double[SunCount];
        double[] hRts = new double[SunCount];

        for (int i = 0; i < SunCount; i++)
        {
            nuRts[i] = nu + 360.985647 * mRts[i];
            double n = mRts[i] + spa.DeltaT / 86400.0;
            alphaPrime[i] = RtsAlphaDeltaPrime(alpha, n);
            deltaPrime[i] = RtsAlphaDeltaPrime(delta, n);
            hPrime[i] = MathUtils.LimitDegrees180Pm(nuRts[i] + spa.Longitude - alphaPrime[i]);
            hRts[i] = RtsSunAltitude(spa.Latitude, deltaPrime[i], hPrime[i]);
        }

        double srha = hPrime[SunRise];
        double ssha = hPrime[SunSet];
        double sta = hRts[SunTransit];

        double suntransit = TimeUtils.DayfracToLocalHr(
            mRts[SunTransit] - hPrime[SunTransit] / 360.0,
            spa.Timezone);

        double sunrise = TimeUtils.DayfracToLocalHr(
            SunRiseAndSet(mRts, hRts, deltaPrime, spa.Latitude, hPrime, h0Prime, SunRise),
            spa.Timezone);

        double sunset = TimeUtils.DayfracToLocalHr(
            SunRiseAndSet(mRts, hRts, deltaPrime, spa.Latitude, hPrime, h0Prime, SunSet),
            spa.Timezone);

        return new RtsResult(sunrise, suntransit, sunset, srha, ssha, sta, eot);
    }

    /// <summary>Calculate sunrise/sunset for a custom zenith angle (twilight/golden/blue calculations).</summary>
    public static CustomZenithTimes CalculateCustomZenithTimes(
        double latitude,
        double delta,
        double suntransit,
        double zenithAngle)
    {
        double latRad = MathUtils.Deg2Rad(latitude);
        double deltaRad = MathUtils.Deg2Rad(delta);
        double zenithRad = MathUtils.Deg2Rad(zenithAngle);

        double cosH0 =
            (Math.Cos(zenithRad) - Math.Sin(latRad) * Math.Sin(deltaRad))
            / (Math.Cos(latRad) * Math.Cos(deltaRad));

        if (cosH0 is < -1 or > 1) return new CustomZenithTimes(null, null);

        double h0H = MathUtils.Rad2Deg(Math.Acos(cosH0)) / 15.0;

        return new CustomZenithTimes(suntransit - h0H, suntransit + h0H);
    }
}
