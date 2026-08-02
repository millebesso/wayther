namespace Wayther.Api;

/// <summary>
/// The shape rules a set of route inputs must satisfy, shared by the endpoints that
/// accept them: <c>POST /api/route-forecast</c> (which forecasts them) and
/// <c>POST /api/shares</c> (which persists them). Keeping the rules in one place means
/// a stored share can never carry inputs the forecast endpoint would later reject.
/// </summary>
internal static class RouteInputValidation
{
    /// <summary>The interval selector offers these two choices; anything else is a bad request.</summary>
    internal static readonly int[] AllowedIntervalMinutes = [30, 60];

    /// <summary>A route needs a start and an end; the upper bound caps the ORS request and the
    /// number of forecast lookups. The frontend enforces the same range.</summary>
    internal const int MinWaypoints = 2;
    internal const int MaxWaypoints = 10;

    /// <summary>
    /// Returns a human-readable reason the inputs are invalid, or <c>null</c> when they pass.
    /// </summary>
    internal static string? Validate(IReadOnlyList<PointDto>? waypoints, int intervalMinutes)
    {
        if (!AllowedIntervalMinutes.Contains(intervalMinutes))
            return $"intervalMinutes must be one of {string.Join(", ", AllowedIntervalMinutes)}.";

        var waypointCount = waypoints?.Count ?? 0;
        if (waypointCount < MinWaypoints || waypointCount > MaxWaypoints)
            return $"waypoints must contain between {MinWaypoints} and {MaxWaypoints} points.";

        return null;
    }
}
