namespace Wayther.Domain;

/// <summary>A geographic point in decimal degrees (WGS84).</summary>
public sealed record Coordinate(double Latitude, double Longitude);
