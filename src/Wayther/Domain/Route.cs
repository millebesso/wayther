namespace Wayther.Domain;

/// <summary>
/// A driving route between two points: the ordered geometry vertices plus the
/// per-segment travel-time annotations that distribute the total duration along
/// that geometry. Each <see cref="RouteSegment"/> references geometry vertices by
/// index, so a later slice can interpolate position-at-time from cumulative
/// duration. This slice draws the geometry only; the durations are carried but
/// not yet consumed.
/// </summary>
public sealed record Route(
    IReadOnlyList<Coordinate> Geometry,
    IReadOnlyList<RouteSegment> Segments,
    double TotalDistanceMeters,
    double TotalDurationSeconds);

/// <summary>
/// One piece of a route spanning geometry vertices <c>[StartIndex, EndIndex]</c>,
/// carrying the travel time and distance across that span.
/// </summary>
public sealed record RouteSegment(
    int StartIndex,
    int EndIndex,
    double DistanceMeters,
    double DurationSeconds);
