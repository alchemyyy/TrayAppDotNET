namespace BrightnessTrayAppDotNET.SunriseSunset;

/// <summary>
/// Start/end window where either bound can be null at high latitudes,
/// where the sun does not reach the relevant zenith angle.
/// </summary>
public sealed record TwilightWindow(DateTimeOffset? Start, DateTimeOffset? End);

/// <summary>Morning and evening transitions for golden or blue hour</summary>
public sealed record HourPeriod(TwilightWindow Morning, TwilightWindow Evening);

/// <summary>Civil, nautical, astronomical twilight, plus golden and blue hour windows</summary>
/// <param name="CivilDawn">Sun crosses 6 deg below the horizon while ascending</param>
/// <param name="CivilDusk">Sun crosses 6 deg below the horizon while descending</param>
/// <param name="NauticalDawn">Sun crosses 12 deg below the horizon while ascending</param>
/// <param name="NauticalDusk">Sun crosses 12 deg below the horizon while descending</param>
/// <param name="AstronomicalDawn">Sun crosses 18 deg below the horizon while ascending</param>
/// <param name="AstronomicalDusk">Sun crosses 18 deg below the horizon while descending</param>
/// <param name="GoldenHour">
/// Golden hour windows.
/// Morning runs from sunrise to ~6 deg elevation;
/// evening from ~6 deg elevation to sunset.
/// </param>
/// <param name="BlueHour">Blue hour windows (sun ~4 deg-6 deg below horizon)</param>
public sealed record TwilightTimes(
    DateTimeOffset? CivilDawn,
    DateTimeOffset? CivilDusk,
    DateTimeOffset? NauticalDawn,
    DateTimeOffset? NauticalDusk,
    DateTimeOffset? AstronomicalDawn,
    DateTimeOffset? AstronomicalDusk,
    HourPeriod GoldenHour,
    HourPeriod BlueHour);
