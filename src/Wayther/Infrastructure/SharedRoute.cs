namespace Wayther.Infrastructure;

/// <summary>
/// A shared route snapshot: the inputs needed to reopen someone's planned trip
/// (the ordered waypoints, the departure time, and the sampling interval), keyed
/// by a short unguessable slug. The forecast itself is never stored — opening a
/// share re-requests a fresh forecast for these inputs. Rows are kept
/// indefinitely; <see cref="CreatedAt"/> records when the share was minted.
/// </summary>
public class SharedRoute
{
    /// <summary>The short base62 slug that appears in the <c>/m/{id}</c> share link.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The ordered waypoints as a JSON array of <c>{ "lat": .., "lon": .. }</c>.</summary>
    public string Waypoints { get; set; } = string.Empty;

    public DateTimeOffset DepartureTime { get; set; }

    public int IntervalMinutes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
