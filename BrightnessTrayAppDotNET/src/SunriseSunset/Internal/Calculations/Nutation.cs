using BrightnessTrayAppDotNET.SunriseSunset.Internal.Utils;

namespace BrightnessTrayAppDotNET.SunriseSunset.Internal.Calculations;

internal readonly record struct NutationResult(double DelPsi, double DelEpsilon);

internal static class Nutation
{
    public static double MeanElongationMoonSun(double jce)
        => MathUtils.ThirdOrderPolynomial(1.0 / 189474.0, -0.0019142, 445267.11148, 297.85036, jce);

    public static double MeanAnomalySun(double jce)
        => MathUtils.ThirdOrderPolynomial(-1.0 / 300000.0, -0.0001603, 35999.05034, 357.52772, jce);

    public static double MeanAnomalyMoon(double jce)
        => MathUtils.ThirdOrderPolynomial(1.0 / 56250.0, 0.0086972, 477198.867398, 134.96298, jce);

    public static double ArgumentLatitudeMoon(double jce)
        => MathUtils.ThirdOrderPolynomial(1.0 / 327270.0, -0.0036825, 483202.017538, 93.27191, jce);

    public static double AscendingLongitudeMoon(double jce)
        => MathUtils.ThirdOrderPolynomial(1.0 / 450000.0, 0.0020708, -1934.136261, 125.04452, jce);

    private static double XyTermSummation(int i, double[] x)
    {
        double sum = 0;
        for (int j = 0; j < SPAConstants.TermXCount; j++) sum += x[j] * SPAConstants.YTerms[i][j];
        return sum;
    }

    public static NutationResult LongitudeAndObliquity(double jce, double[] x)
    {
        double sumPsi = 0;
        double sumEpsilon = 0;

        for (int i = 0; i < SPAConstants.YCount; i++)
        {
            double xyTermSum = MathUtils.Deg2Rad(XyTermSummation(i, x));
            sumPsi += (SPAConstants.PeTerms[i][SPAConstants.TermPsiA]
                       + jce * SPAConstants.PeTerms[i][SPAConstants.TermPsiB])
                      * Math.Sin(xyTermSum);
            sumEpsilon += (SPAConstants.PeTerms[i][SPAConstants.TermEpsC]
                           + jce * SPAConstants.PeTerms[i][SPAConstants.TermEpsD])
                          * Math.Cos(xyTermSum);
        }

        return new NutationResult(sumPsi / 36000000.0, sumEpsilon / 36000000.0);
    }

    public static double EclipticMeanObliquity(double jme)
    {
        double u = jme / 10.0;
        return 84381.448 +
               u * (-4680.93 +
                    u * (-1.55 +
                         u * (1999.25 +
                              u * (-51.38 +
                                   u * (-249.67 +
                                        u * (-39.05 +
                                             u * (7.12 +
                                                  u * (27.87 +
                                                       u * (5.79 +
                                                            u * 2.45)))))))));
    }

    public static double EclipticTrueObliquity(double deltaEpsilon, double epsilon0)
        => deltaEpsilon + epsilon0 / 3600.0;
}
