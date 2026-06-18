namespace BrightnessTrayAppDotNET.SunriseSunset;

/// <summary>Optional parameters for SPA calculations; all values default to NREL/standard values</summary>
public sealed record SPAOptions
{
    /// <summary>Observer elevation in meters (default: 0)</summary>
    public double ElevationMeters { get; init; } = 0.0;

    /// <summary>Annual average local pressure in millibars (default: 1013)</summary>
    public double Pressure { get; init; } = 1013.0;

    /// <summary>Annual average local temperature in Celsius (default: 15)</summary>
    public double Temperature { get; init; } = 15.0;

    /// <summary>Fractional second difference between UTC and UT (default: 0)</summary>
    public double DeltaUt1 { get; init; } = 0.0;

    /// <summary>Difference between earth rotation time and terrestrial time, in seconds (default: 67)</summary>
    public double DeltaT { get; init; } = 67.0;

    /// <summary>Surface slope in degrees (default: 0)</summary>
    public double Slope { get; init; } = 0.0;

    /// <summary>Surface azimuth rotation in degrees (default: 0)</summary>
    public double AzimuthRotation { get; init; } = 0.0;

    /// <summary>Atmospheric refraction at sunrise/sunset in degrees (default: 0.5667)</summary>
    public double AtmosphericRefraction { get; init; } = 0.5667;

    /// <summary>
    /// Optional fixed timezone offset in hours from UTC (e.g., -5 for EST).
    /// Takes precedence over <see cref="TimezoneId"/> and the offset of the supplied date.
    /// </summary>
    public double? TimezoneOffsetHours { get; init; }

    /// <summary>
    /// Optional IANA timezone ID (e.g., "America/New_York"); used when <see cref="TimezoneOffsetHours"/> is null.
    /// On platforms where IANA IDs are not recognized natively, .NET translates from the equivalent Windows ID.
    /// </summary>
    public string? TimezoneId { get; init; }
}
