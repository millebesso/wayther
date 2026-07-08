using Wayther.Domain;

namespace Wayther.Api;

public static class RouteForecastEndpoints
{
    // The interval selector offers these two choices; anything else is a bad request.
    private static readonly int[] AllowedIntervalMinutes = [30, 60];

    public static void MapRouteForecastEndpoints(this WebApplication app)
    {
        // Turn the two map-clicked points plus a departure time and sampling
        // interval into a drawn route and the timed samples along it: the origin at
        // departure, a sample every N minutes of travel, and the arrival. A later
        // slice attaches a forecast to each sample.
        app.MapPost("/api/route-forecast", async (
            RouteForecastRequest request,
            RouteForecastService routeForecast,
            CancellationToken cancellationToken) =>
        {
            if (!AllowedIntervalMinutes.Contains(request.IntervalMinutes))
                return Results.BadRequest(
                    $"intervalMinutes must be one of {string.Join(", ", AllowedIntervalMinutes)}.");

            var forecast = await routeForecast.GetRouteForecastAsync(
                new Coordinate(request.Origin.Lat, request.Origin.Lon),
                new Coordinate(request.Destination.Lat, request.Destination.Lon),
                request.DepartureTime,
                TimeSpan.FromMinutes(request.IntervalMinutes),
                cancellationToken);

            var geometry = forecast.Route.Geometry
                .Select(point => new PointDto(point.Latitude, point.Longitude))
                .ToArray();

            var samples = forecast.Samples
                .Select(sample => new SampleDto(sample.Position.Latitude, sample.Position.Longitude, sample.Time))
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

/// <summary>One timed sample: where the traveller will be and when.</summary>
public sealed record SampleDto(double Lat, double Lon, DateTimeOffset Time);

/// <summary>The drawn route geometry plus the ordered timed samples along it.</summary>
public sealed record RouteForecastResponse(
    IReadOnlyList<PointDto> Geometry,
    IReadOnlyList<SampleDto> Samples);
