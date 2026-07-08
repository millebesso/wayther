namespace Wayther.Domain;

/// <summary>
/// Resolves the forecast timeline for a single point. Implemented by an external
/// weather service (MET Norway) in Infrastructure; the Domain depends only on this
/// seam so the orchestration logic — including nearest-hour selection — can be
/// exercised with a faked provider (no network).
/// </summary>
/// <remarks>
/// A single met.no call returns the whole hourly timeline for a point, so the seam
/// hands back every hour it knows about and the <see cref="RouteForecastService"/>
/// picks the nearest hour for each sample. Keeping selection in the Domain (rather
/// than the provider) is what lets the "correct bucket for a given arrival minute"
/// logic be unit-tested through the fake provider.
/// </remarks>
public interface IWeatherProvider
{
    Task<WeatherTimeline> GetForecastAsync(
        Coordinate location,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A point's forecast timeline: the ordered per-hour entries met.no returns for a
/// location. The <see cref="RouteForecastService"/> selects the nearest hour to a
/// sample's arrival time from these.
/// </summary>
public sealed record WeatherTimeline(IReadOnlyList<WeatherHour> Hours);

/// <summary>One hourly entry in a <see cref="WeatherTimeline"/>: the hour it covers and its forecast.</summary>
public sealed record WeatherHour(DateTimeOffset Time, WeatherForecast Forecast);

/// <summary>
/// The forecast for one hour: a MET Norway weather symbol code, the air
/// temperature, and the precipitation expected over the hour.
/// </summary>
public sealed record WeatherForecast(
    string SymbolCode,
    double TemperatureCelsius,
    double PrecipitationMm);
