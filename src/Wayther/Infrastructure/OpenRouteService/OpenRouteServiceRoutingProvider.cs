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
        Coordinate origin,
        Coordinate destination,
        CancellationToken cancellationToken = default)
    {
        // ORS expects [longitude, latitude] pairs. instructions=true yields the
        // segments/steps whose per-step durations are the annotations we carry.
        var request = new OrsDirectionsRequest(
            Coordinates:
            [
                [origin.Longitude, origin.Latitude],
                [destination.Longitude, destination.Latitude],
            ],
            Instructions: true);

        var path = $"/v2/directions/{_options.Profile}/geojson";
        using var response = await http.PostAsJsonAsync(path, request, cancellationToken);
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
