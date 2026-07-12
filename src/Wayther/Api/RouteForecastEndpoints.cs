using Wayther.Domain;

namespace Wayther.Api;

public static class RouteForecastEndpoints
{
    // The interval selector offers these two choices; anything else is a bad request.
    private static readonly int[] AllowedIntervalMinutes = [30, 60];

    public static void MapRouteForecastEndpoints(this WebApplication app)
    {
        // A stable, named logger for usage tracking. RouteForecastEndpoints is a
        // static class (so it can't be an ILogger<T> category), so name the category
        // explicitly here and close over the logger in the handler.
        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Wayther.RouteForecast");

        // Turn the two map-clicked points plus a departure time and sampling
        // interval into a drawn route and the timed, forecasted samples along it:
        // the origin at departure, a sample every N minutes of travel, and the
        // arrival — each carrying its nearest-hour forecast.
        app.MapPost("/api/route-forecast", async (
            RouteForecastRequest request,
            RouteForecastService routeForecast,
            CancellationToken cancellationToken) =>
        {
            if (!AllowedIntervalMinutes.Contains(request.IntervalMinutes))
                return Results.BadRequest(
                    $"intervalMinutes must be one of {string.Join(", ", AllowedIntervalMinutes)}.");

            RouteForecast forecast;
            try
            {
                forecast = await routeForecast.GetRouteForecastAsync(
                    new Coordinate(request.Origin.Lat, request.Origin.Lon),
                    new Coordinate(request.Destination.Lat, request.Destination.Lon),
                    request.DepartureTime,
                    TimeSpan.FromMinutes(request.IntervalMinutes),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                // Surface the failed route request with its inputs, then rethrow so
                // the framework still produces its normal error response.
                logger.LogError(ex,
                    "Route forecast failed: {OriginLat},{OriginLon} -> {DestLat},{DestLon} " +
                    "departing {DepartureTime:o} every {IntervalMinutes}min",
                    request.Origin.Lat, request.Origin.Lon,
                    request.Destination.Lat, request.Destination.Lon,
                    request.DepartureTime, request.IntervalMinutes);
                throw;
            }

            logger.LogInformation(
                "Route forecast requested: {OriginLat},{OriginLon} -> {DestLat},{DestLon} " +
                "departing {DepartureTime:o} every {IntervalMinutes}min; " +
                "route {DistanceKm:F1}km/{DurationMin:F0}min, {SampleCount} samples",
                request.Origin.Lat, request.Origin.Lon,
                request.Destination.Lat, request.Destination.Lon,
                request.DepartureTime, request.IntervalMinutes,
                forecast.Route.TotalDistanceMeters / 1000.0,
                forecast.Route.TotalDurationSeconds / 60.0,
                forecast.Samples.Count);

            var geometry = forecast.Route.Geometry
                .Select(point => new PointDto(point.Latitude, point.Longitude))
                .ToArray();

            var samples = forecast.Samples
                .Select(sample => new SampleDto(
                    sample.Position.Latitude,
                    sample.Position.Longitude,
                    sample.Time,
                    new ForecastDto(
                        sample.Forecast.SymbolCode,
                        sample.Forecast.TemperatureCelsius,
                        sample.Forecast.PrecipitationMm)))
                .ToArray();

            return Results.Ok(new RouteForecastResponse(geometry, samples));
        });
    }
}

/// <summary>
/// A route-forecast request: the two coordinates chosen on the map, when the
/// traveller departs, and how often (in minutes) to sample the journey.
/// </summary>
public sealed record RouteForecastRequest(
    PointDto Origin,
    PointDto Destination,
    DateTimeOffset DepartureTime,
    int IntervalMinutes);

/// <summary>A geographic point in the API's wire format (decimal degrees).</summary>
public sealed record PointDto(double Lat, double Lon);

/// <summary>One timed sample: where the traveller will be, when, and the forecast there.</summary>
public sealed record SampleDto(double Lat, double Lon, DateTimeOffset Time, ForecastDto Forecast);

/// <summary>The nearest-hour forecast for a sample: symbol code, temperature (°C), precipitation (mm).</summary>
public sealed record ForecastDto(string SymbolCode, double TemperatureCelsius, double PrecipitationMm);

/// <summary>The drawn route geometry plus the ordered timed samples along it.</summary>
public sealed record RouteForecastResponse(
    IReadOnlyList<PointDto> Geometry,
    IReadOnlyList<SampleDto> Samples);
