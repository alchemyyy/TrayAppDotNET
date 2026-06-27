namespace BrightnessTrayAppDotNET.SunriseSunset.Internal;

/// <summary>
/// Mutable input/intermediate/output bag for <see cref="SPACore"/>.
/// Internal to the pipeline; consumers should never see it.
/// </summary>
internal sealed class SPAData
{
    // Input values
    public int Year;
    public int Month;
    public int Day;
    public int Hour;
    public int Minute;
    public double Second;
    public double DeltaUt1;
    public double DeltaT = 67.0;
    public double Timezone;
    public double Longitude;
    public double Latitude;
    public double Elevation;
    public double Pressure = 1013.0;
    public double Temperature = 15.0;
    public double Slope;
    public double AzimuthRotation;
    public double AtmosphericRefraction = SPAConstants.RefractionCorrection;
    public string TimezoneId = string.Empty;

    // Intermediate values
    public double Jd;
    public double Jc;
    public double Jde;
    public double Jce;
    public double Jme;
    public double L;
    public double B;
    public double R;
    public double Theta;
    public double Beta;
    public double X0;
    public double X1;
    public double X2;
    public double X3;
    public double X4;
    public double DelPsi;
    public double DelEpsilon;
    public double Epsilon0;
    public double Epsilon;
    public double DelTau;
    public double Lamda;
    public double Nu0;
    public double Nu;
    public double Alpha;
    public double Delta;
    public double H;
    public double Xi;
    public double DelAlpha;
    public double DeltaPrime;
    public double AlphaPrime;
    public double HPrime;
    public double E0;
    public double DelE;
    public double E;
    public double Eot;
    public double Srha;
    public double Ssha;
    public double Sta;

    // Output values
    public double Zenith;
    public double AzimuthAstro;
    public double Azimuth;
    public double Incidence;
    public double Suntransit;
    public double Sunrise;
    public double Sunset;
}
