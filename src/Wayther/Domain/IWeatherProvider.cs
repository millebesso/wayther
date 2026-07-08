namespace Wayther.Domain;

/// <summary>
/// Resolves the forecast for a single point at a single time. Implemented by an
/// external weather service (MET Norway) in Infrastructure; the Domain depends
/// only on this seam so the orchestration logic can be exercised with a faked
/// provider (no network).
/// </summary>
/// <remarks>
/// Introduced here as the second provider seam the <see cref="RouteForecastService"/>
/// depends on. Attaching a forecast to each sample lands in a later slice; this
/// slice produces the timed samples the forecast will hang off.
/// </remarks>
public interface IWeatherProvider
{
    Task<WeatherForecast> GetForecastAsync(
        Coordinate location,
        DateTimeOffset time,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The nearest-hour forecast selected for one sample: a MET Norway weather symbol
/// code, the air temperature, and the precipitation expected over the hour.
/// </summary>
public sealed record WeatherForecast(
    string SymbolCode,
    double TemperatureCelsius,
    double PrecipitationMm);
