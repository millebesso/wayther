using Wayther.Domain;

namespace Wayther.Api;

public static class RouteForecastEndpoints
{
    public static void MapRouteForecastEndpoints(this WebApplication app)
    {
        // Turn two map-clicked points into a drawn driving route. Later slices
        // extend the request with departure time / interval and the response with
        // the sampled weather timeline; for now it returns the route geometry.
        app.MapPost("/api/route-forecast", async (
            RouteForecastRequest request,
            IRoutingProvider routing,
            CancellationToken cancellationToken) =>
        {
            var route = await routing.GetRouteAsync(
                new Coordinate(request.Origin.Lat, request.Origin.Lon),
                new Coordinate(request.Destination.Lat, request.Destination.Lon),
                cancellationToken);

            var geometry = route.Geometry
                .Select(point => new PointDto(point.Latitude, point.Longitude))
                .ToArray();

            return Results.Ok(new RouteForecastResponse(geometry));
        });
    }
}

/// <summary>A route-forecast request: the two coordinates chosen on the map.</summary>
public sealed record RouteForecastRequest(PointDto Origin, PointDto Destination);

/// <summary>A geographic point in the API's wire format (decimal degrees).</summary>
public sealed record PointDto(double Lat, double Lon);

/// <summary>The drawn route geometry as an ordered list of lat/lon points.</summary>
public sealed record RouteForecastResponse(IReadOnlyList<PointDto> Geometry);
