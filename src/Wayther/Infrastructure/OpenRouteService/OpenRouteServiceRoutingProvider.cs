using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Wayther.Domain;

// Disambiguate from Microsoft.AspNetCore.Routing.Route (implicit Web SDK using).
using Route = Wayther.Domain.Route;

namespace Wayther.Infrastructure.OpenRouteService;

/// <summary>
/// <see cref="IRoutingProvider"/> backed by OpenRouteService's directions API.
/// Requests the GeoJSON response with turn-by-turn instructions enabled, so the
/// route carries per-segment duration annotations: ORS distributes the total
/// travel time across the geometry via each step's <c>way_points</c> index range.
/// A dumb translation layer behind the <see cref="IRoutingProvider"/> seam.
/// </summary>
public sealed class OpenRouteServiceRoutingProvider(
    HttpClient http,
    IOptions<OpenRouteServiceOptions> options) : IRoutingProvider
{
    private readonly OpenRouteServiceOptions _options = options.Value;

    public async Task<Route> GetRouteAsync(
        IReadOnlyList<Coordinate> waypoints,
        CancellationToken cancellationToken = default)
    {
        // ORS expects [longitude, latitude] pairs, in visiting order (start first,
        // destination last, intermediate stops between). instructions=true yields the
        // segments/steps whose per-step durations are the annotations we carry.
        var request = new OrsDirectionsRequest(
            Coordinates: waypoints
                .Select(point => new[] { point.Longitude, point.Latitude })
                .ToArray(),
            Instructions: true);

        var path = $"/v2/directions/{_options.Profile}/geojson";
        using var response = await http.PostAsJsonAsync(path, request, cancellationToken);

        // ORS answers an unroutable request (a point in water or off the road
        // network) with a 404 error envelope. Translate that one status into the
        // domain's RouteNotFoundException; every other non-success (bad input,
        // auth, quota, outage) stays a generic failure via EnsureSuccessStatusCode.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var error = await response.Content
                .ReadFromJsonAsync<OrsErrorResponse>(cancellationToken);
            throw new RouteNotFoundException(
                error?.Error?.Message ?? "OpenRouteService could not find a route between the points.");
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OrsDirectionsResponse>(cancellationToken)
            ?? throw new InvalidOperationException("OpenRouteService returned an empty response body.");

        return MapToRoute(body);
    }

    private static Route MapToRoute(OrsDirectionsResponse response)
    {
        var feature = response.Features is { Count: > 0 }
            ? response.Features[0]
            : throw new InvalidOperationException("OpenRouteService response contained no route feature.");

        var geometry = feature.Geometry.Coordinates
            .Select(c => new Coordinate(Latitude: c[1], Longitude: c[0]))
            .ToArray();

        var segments = (feature.Properties.Segments ?? [])
            .SelectMany(segment => segment.Steps ?? [])
            .Select(step => new RouteSegment(
                StartIndex: step.WayPoints[0],
                EndIndex: step.WayPoints[1],
                DistanceMeters: step.Distance,
                DurationSeconds: step.Duration))
            .ToArray();

        var summary = feature.Properties.Summary;
        return new Route(geometry, segments, summary?.Distance ?? 0, summary?.Duration ?? 0);
    }
}
