namespace Wayther.Domain;

/// <summary>
/// One point on the journey: where the traveller is expected to be
/// (<paramref name="Position"/>) and when they are expected to be there
/// (<paramref name="Time"/>). The forecast for this place-and-time is attached in
/// a later slice; here a sample is just <c>(lat, lon, time)</c>.
/// </summary>
public sealed record RouteSample(Coordinate Position, DateTimeOffset Time);
