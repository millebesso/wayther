namespace Wayther.Domain;

/// <summary>
/// One point on the journey: where the traveller is expected to be
/// (<paramref name="Position"/>), when they are expected to be there
/// (<paramref name="Time"/>), and the nearest-hour <paramref name="Forecast"/>
/// selected for that place-and-time.
/// </summary>
public sealed record RouteSample(Coordinate Position, DateTimeOffset Time, WeatherForecast Forecast);
