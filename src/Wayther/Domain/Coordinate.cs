namespace Wayther.Domain;

/// <summary>A geographic point in decimal degrees (WGS84).</summary>
public sealed record Coordinate(double Latitude, double Longitude)
{
    /// <summary>
    /// This point with both components rounded to <paramref name="decimals"/> decimal
    /// places (half away from zero). met.no asks callers to round to ≤4 dp (~11 m),
    /// which also keys the forecast cache so nearby samples share a cached timeline.
    /// </summary>
    public Coordinate Rounded(int decimals) => new(
        Math.Round(Latitude, decimals, MidpointRounding.AwayFromZero),
        Math.Round(Longitude, decimals, MidpointRounding.AwayFromZero));
}
