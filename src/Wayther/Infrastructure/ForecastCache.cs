namespace Wayther.Infrastructure;

/// <summary>
/// Cached weather forecast for a single point, keyed on coordinates rounded to
/// 4 decimal places. The full met.no forecast timeline for the point is stored
/// as a JSON payload. Populated by later slices; the table exists from slice 0.
/// </summary>
public class ForecastCache
{
    public double Lat4 { get; set; }
    public double Lon4 { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset FetchedAt { get; set; }
}
