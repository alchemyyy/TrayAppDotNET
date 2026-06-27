namespace BrightnessTrayAppDotNET.SunriseSunset.Internal.Utils;

internal static class MathUtils
{
    private const double DegToRad = Math.PI / 180.0;
    private const double RadToDeg = 180.0 / Math.PI;

    public static double Deg2Rad(double degrees) => DegToRad * degrees;

    public static double Rad2Deg(double radians) => RadToDeg * radians;

    /// <summary>Limit degrees to the [0, 360) range.</summary>
    public static double LimitDegrees(double degrees)
    {
        double limited = degrees / 360.0;
        limited = 360.0 * (limited - Math.Floor(limited));
        if (limited < 0) limited += 360.0;
        return limited;
    }

    /// <summary>Limit degrees to the [0, 180) range.</summary>
    public static double LimitDegrees180(double degrees)
    {
        double limited = degrees / 180.0;
        limited = 180.0 * (limited - Math.Floor(limited));
        if (limited < 0) limited += 180.0;
        return limited;
    }

    /// <summary>Limit degrees to the (-180, 180] range.</summary>
    public static double LimitDegrees180Pm(double degrees)
    {
        double limited = degrees / 360.0;
        limited = 360.0 * (limited - Math.Floor(limited));
        if (limited < -180.0)
            limited += 360.0;
        else if (limited > 180.0) limited -= 360.0;
        return limited;
    }

    /// <summary>Limit fractional value to the [0, 1) range.</summary>
    public static double LimitZero2One(double value)
    {
        double limited = value - Math.Floor(value);
        if (limited < 0) limited += 1.0;
        return limited;
    }

    /// <summary>Evaluate a third-order polynomial: ((a*x + b)*x + c)*x + d.</summary>
    public static double ThirdOrderPolynomial(double a, double b, double c, double d, double x)
        => ((a * x + b) * x + c) * x + d;

    /// <summary>Wrap minutes into [-20, 20] range for equation of time.</summary>
    public static double LimitMinutes(double minutes)
    {
        double limited = minutes;
        if (limited < -20.0)
            limited += 1440.0;
        else if (limited > 20.0) limited -= 1440.0;
        return limited;
    }
}
