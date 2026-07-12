using System.Text.Json.Serialization;

namespace Wayther.Infrastructure.OpenRouteService;

// Wire types for the ORS directions GeoJSON API. Coordinates are [longitude,
// latitude] pairs. `instructions: true` makes ORS return the segments/steps whose
// per-step durations are the "per-segment duration annotations" this route needs.

internal sealed record OrsDirectionsRequest(
    [property: JsonPropertyName("coordinates")] double[][] Coordinates,
    [property: JsonPropertyName("instructions")] bool Instructions);

internal sealed record OrsDirectionsResponse(
    [property: JsonPropertyName("features")] IReadOnlyList<OrsFeature>? Features);

internal sealed record OrsFeature(
    [property: JsonPropertyName("geometry")] OrsGeometry Geometry,
    [property: JsonPropertyName("properties")] OrsProperties Properties);

internal sealed record OrsGeometry(
    [property: JsonPropertyName("coordinates")] double[][] Coordinates);

internal sealed record OrsProperties(
    [property: JsonPropertyName("segments")] IReadOnlyList<OrsSegment>? Segments,
    [property: JsonPropertyName("summary")] OrsSummary? Summary);

internal sealed record OrsSegment(
    [property: JsonPropertyName("steps")] IReadOnlyList<OrsStep>? Steps);

internal sealed record OrsStep(
    [property: JsonPropertyName("distance")] double Distance,
    [property: JsonPropertyName("duration")] double Duration,
    [property: JsonPropertyName("way_points")] int[] WayPoints);

internal sealed record OrsSummary(
    [property: JsonPropertyName("distance")] double Distance,
    [property: JsonPropertyName("duration")] double Duration);

// ORS reports an unroutable request as an HTTP 404 carrying this error envelope
// (e.g. code 2010 "Could not find routable point…", 2009 "Route could not be
// found…"). Only the message is kept, for the server log.
internal sealed record OrsErrorResponse(
    [property: JsonPropertyName("error")] OrsError? Error);

internal sealed record OrsError(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string? Message);
