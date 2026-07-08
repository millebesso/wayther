using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Wayther.Domain;

namespace Wayther.Infrastructure.MetNo;

/// <summary>
/// <see cref="IWeatherProvider"/> backed by MET Norway's LocationForecast 2.0
/// "compact" endpoint. Returns the point's hourly timeline (each timestep's air
/// temperature paired with its <c>next_1_hours</c> symbol and precipitation); the
/// Domain selects the nearest hour. A dumb translation layer behind the seam.
/// </summary>
/// <remarks>
/// met.no rejects requests without an identifying <c>User-Agent</c> (with contact
/// info), so it is set on every request from configuration. Timesteps without a
/// <c>next_1_hours</c> block (beyond the hourly-resolution window) are dropped:
/// they carry no hourly symbol or precipitation to select.
/// </remarks>
public sealed class MetNoWeatherProvider : IWeatherProvider
{
    private readonly HttpClient _http;

    public MetNoWeatherProvider(HttpClient http, IOptions<MetNoOptions> options)
    {
        _http = http;

        // met.no requires an identifying User-Agent on every request; add it once
        // for this typed client. TryAddWithoutValidation accepts arbitrary contact
        // strings that the structured User-Agent parser would otherwise reject.
        if (!string.IsNullOrWhiteSpace(options.Value.UserAgent)
            && !_http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.Value.UserAgent);
        }
    }

    public async Task<WeatherTimeline> GetForecastAsync(
        Coordinate location,
        CancellationToken cancellationToken = default)
    {
        // Coordinates arrive already rounded to ≤4 dp from the Domain. Format with
        // the invariant culture so the decimal point is never localized to a comma.
        var lat = location.Latitude.ToString(CultureInfo.InvariantCulture);
        var lon = location.Longitude.ToString(CultureInfo.InvariantCulture);
        var path = $"/weatherapi/locationforecast/2.0/compact?lat={lat}&lon={lon}";

        using var response = await _http.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<MetNoForecastResponse>(cancellationToken)
            ?? throw new InvalidOperationException("MET Norway returned an empty response body.");

        return MapToTimeline(body);
    }

    private static WeatherTimeline MapToTimeline(MetNoForecastResponse response)
    {
        var hours = (response.Properties?.Timeseries ?? [])
            .Select(ToHour)
            .Where(hour => hour is not null)
            .Select(hour => hour!)
            .ToArray();

        return new WeatherTimeline(hours);
    }

    /// <summary>Maps one timestep to a <see cref="WeatherHour"/>, or null if it has no hourly block.</summary>
    private static WeatherHour? ToHour(MetNoTimestep timestep)
    {
        var nextHour = timestep.Data?.Next1Hours;
        if (nextHour is null)
            return null;

        var forecast = new WeatherForecast(
            SymbolCode: nextHour.Summary?.SymbolCode ?? string.Empty,
            TemperatureCelsius: timestep.Data?.Instant?.Details?.AirTemperature ?? double.NaN,
            PrecipitationMm: nextHour.Details?.PrecipitationAmount ?? 0);

        return new WeatherHour(timestep.Time, forecast);
    }
}
