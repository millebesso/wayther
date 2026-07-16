namespace Wayther.Domain;

/// <summary>
/// Resolves a driving route visiting an ordered list of waypoints (the first is
/// the start, the last is the destination, any in between are intermediate stops).
/// Implemented by an external routing service in Infrastructure; the Domain depends
/// only on this seam so the orchestration logic can be exercised with a faked
/// provider (no network).
/// </summary>
public interface IRoutingProvider
{
    Task<Route> GetRouteAsync(
        IReadOnlyList<Coordinate> waypoints,
        CancellationToken cancellationToken = default);
}
