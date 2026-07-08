namespace Wayther.Domain;

/// <summary>
/// Resolves a driving route between two coordinates. Implemented by an external
/// routing service in Infrastructure; the Domain depends only on this seam so the
/// orchestration logic can be exercised with a faked provider (no network).
/// </summary>
public interface IRoutingProvider
{
    Task<Route> GetRouteAsync(
        Coordinate origin,
        Coordinate destination,
        CancellationToken cancellationToken = default);
}
