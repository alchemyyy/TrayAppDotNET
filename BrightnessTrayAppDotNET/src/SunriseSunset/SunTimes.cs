namespace BrightnessTrayAppDotNET.SunriseSunset;

/// <summary>Aggregate result containing sunrise, sunset, solar noon and twilight times</summary>
public sealed record SunTimes(
    DateTimeOffset? Sunrise,
    DateTimeOffset? Sunset,
    DateTimeOffset? SolarNoon,
    TwilightTimes? Twilight);
