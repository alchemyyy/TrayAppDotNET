using BrightnessTrayAppDotNET.SunriseSunset.Internal.Utils;

namespace BrightnessTrayAppDotNET.SunriseSunset.Internal.Calculations;

internal static class Earth
{
    private static double EarthPeriodicTermSummation(double[][] terms, int count, double jme)
    {
        double sum = 0;
        for (int i = 0; i < count; i++)
        {
            sum += terms[i][SPAConstants.TermA]
                   * Math.Cos(terms[i][SPAConstants.TermB] + terms[i][SPAConstants.TermC] * jme);
        }

        return sum;
    }

    private static double EarthValues(double[] termSum, int count, double jme)
    {
        double sum = 0;
        for (int i = 0; i < count; i++) sum += termSum[i] * Math.Pow(jme, i);
        return sum / 1.0e8;
    }

    public static double HeliocentricLongitude(double jme)
    {
        double[] sum = new double[SPAConstants.LCount];
        for (int i = 0; i < SPAConstants.LCount; i++)
            sum[i] = EarthPeriodicTermSummation(SPAConstants.LTerms[i], SPAConstants.LSubcount[i], jme);
        return MathUtils.LimitDegrees(MathUtils.Rad2Deg(EarthValues(sum, SPAConstants.LCount, jme)));
    }

    public static double HeliocentricLatitude(double jme)
    {
        double[] sum = new double[SPAConstants.BCount];
        for (int i = 0; i < SPAConstants.BCount; i++)
            sum[i] = EarthPeriodicTermSummation(SPAConstants.BTerms[i], SPAConstants.BSubcount[i], jme);
        return MathUtils.Rad2Deg(EarthValues(sum, SPAConstants.BCount, jme));
    }

    public static double RadiusVector(double jme)
    {
        double[] sum = new double[SPAConstants.RCount];
        for (int i = 0; i < SPAConstants.RCount; i++)
            sum[i] = EarthPeriodicTermSummation(SPAConstants.RTerms[i], SPAConstants.RSubcount[i], jme);
        return EarthValues(sum, SPAConstants.RCount, jme);
    }
}
