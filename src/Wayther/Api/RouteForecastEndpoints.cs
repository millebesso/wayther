using Wayther.Domain;

namespace Wayther.Api;

public static class RouteForecastEndpoints
{
    // The interval selector offers these two choices; anything else is a bad request.
    private static readonly int[] AllowedIntervalMinutes = [30, 60];

    // A route needs a start and an end; the upper bound caps the ORS request and the
    // number of forecast lookups. The frontend enforces the same range.
    private const int MinWaypoints = 2;
    private const int MaxWaypoints = 10;

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

            var waypointCount = request.Waypoints?.Count ?? 0;
            if (waypointCount < MinWaypoints || waypointCount > MaxWaypoints)
                return Results.BadRequest(
                    $"waypoints must contain between {MinWaypoints} and {MaxWaypoints} points.");

            var waypoints = request.Waypoints!
                .Select(point => new Coordinate(point.Lat, point.Lon))
                .ToArray();

            // A variable-length list can't keep fixed per-point placeholders, so the
            // usage logs carry a stable count plus one compact joined form of the
            // ordered coordinates (start first, destination last).
            var waypointSummary = string.Join("; ",
                waypoints.Select(c => $"{c.Latitude},{c.Longitude}"));

            RouteForecast forecast;
            try
            {
                forecast = await routeForecast.GetRouteForecastAsync(
                    waypoints,
                    request.DepartureTime,
                    TimeSpan.FromMinutes(request.IntervalMinutes),
                    cancellationToken);
            }
            catch (RouteNotFoundException ex)
            {
                // Expected, user-correctable outcome: no route between the points.
                // Log the provider's reason (never shown to the user) and return a
                // 422 the frontend maps to its own "no route" copy.
                logger.LogWarning(
                    "Route forecast has no route: {WaypointCount} waypoints [{Waypoints}]: {Reason}",
                    waypointCount, waypointSummary, ex.Message);
                return Results.UnprocessableEntity(
                    new RouteForecastError("route_not_found", ex.Message));
            }
            catch (ForecastUnavailableException ex)
            {
                // Expected, user-correctable outcome: the departure sits past met.no's
                // hourly window. 422 with the forecast_unavailable discriminator.
                logger.LogWarning(
                    "Route forecast has no forecast: {WaypointCount} waypoints [{Waypoints}] " +
                    "departing {DepartureTime:o}: {Reason}",
                    waypointCount, waypointSummary, request.DepartureTime, ex.Message);
                return Results.UnprocessableEntity(
                    new RouteForecastError("forecast_unavailable", ex.Message));
            }
            catch (Exception ex)
            {
                // Unexpected failure: surface it with its inputs, then rethrow so the
                // framework still produces its normal error response (a 500).
                logger.LogError(ex,
                    "Route forecast failed: {WaypointCount} waypoints [{Waypoints}] " +
                    "departing {DepartureTime:o} every {IntervalMinutes}min",
                    waypointCount, waypointSummary, request.DepartureTime, request.IntervalMinutes);
                throw;
            }

            logger.LogInformation(
                "Route forecast requested: {WaypointCount} waypoints [{Waypoints}] " +
                "departing {DepartureTime:o} every {IntervalMinutes}min; " +
                "route {DistanceKm:F1}km/{DurationMin:F0}min, {SampleCount} samples",
                waypointCount, waypointSummary,
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
/// A route-forecast request: the ordered waypoints chosen on the map (first is the
/// start, last is the destination, any between are intermediate stops), when the
/// traveller departs, and how often (in minutes) to sample the journey.
/// </summary>
public sealed record RouteForecastRequest(
    IReadOnlyList<PointDto> Waypoints,
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

/// <summary>
/// A 422 error body for an expected, user-correctable failure. <see cref="Error"/>
/// is the stable discriminator the frontend branches on to pick its own copy
/// (<c>route_not_found</c> / <c>forecast_unavailable</c>); <see cref="Message"/> is
/// a diagnostic detail, not shown to the user.
/// </summary>
public sealed record RouteForecastError(string Error, string Message);
