using Wayther.Domain;

namespace Wayther.Infrastructure;

/// <summary>
/// Placeholder <see cref="IWeatherProvider"/> so the <see cref="RouteForecastService"/>
/// can be resolved from DI while weather is not yet wired. This slice produces the
/// timed samples but attaches no forecast, so the seam is never actually called;
/// the real MET Norway client replaces this registration in the weather slice.
/// It throws rather than returning fake data, so any premature call fails loudly.
/// </summary>
public sealed class PendingWeatherProvider : IWeatherProvider
{
    public Task<WeatherForecast> GetForecastAsync(
        Coordinate location,
        DateTimeOffset time,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(
            "Weather forecasting is not wired yet; it lands in the weather slice.");
}
