namespace Wayther.Domain;

/// <summary>
/// Thrown when no route can be resolved between the requested points — e.g. an
/// endpoint sits in water or off the road network, so the routing provider has
/// nowhere routable to snap it to. A user-correctable outcome, not a fault: the
/// API surfaces it as a <c>route_not_found</c> error rather than a 500.
/// </summary>
public sealed class RouteNotFoundException(string message) : Exception(message);

/// <summary>
/// Thrown when no forecast covers a sample's time — the weather provider carries
/// hourly data only out to a limited window, so a departure past that window has
/// no nearest hour close enough to attach. A user-correctable outcome (pick an
/// earlier departure), surfaced as a <c>forecast_unavailable</c> error, not a 500.
/// </summary>
public sealed class ForecastUnavailableException(string message) : Exception(message);
