namespace BrightnessTrayAppDotNET.SunriseSunset;

/// <summary>Solar position output values, all in degrees</summary>
/// <param name="Zenith">Topocentric zenith angle</param>
/// <param name="Azimuth">Topocentric azimuth angle (eastward from north); navigator convention</param>
/// <param name="AzimuthAstro">Topocentric azimuth angle (westward from south); astronomer convention</param>
/// <param name="Elevation">Topocentric elevation angle, corrected for atmospheric refraction</param>
/// <param name="RightAscension">Geocentric sun right ascension</param>
/// <param name="Declination">Geocentric sun declination</param>
/// <param name="HourAngle">Observer hour angle</param>
public sealed record SolarPosition(
    double Zenith,
    double Azimuth,
    double AzimuthAstro,
    double Elevation,
    double RightAscension,
    double Declination,
    double HourAngle);
