using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wayther.Domain;

namespace Wayther.Infrastructure;

/// <summary>
/// An <see cref="IWeatherProvider"/> decorator that backs the wrapped provider
/// (met.no) with the <c>forecast_cache</c> table, so repeated or overlapping
/// routes resolve fast and stay within met.no's terms. Each point's whole
/// forecast timeline is stored once, keyed on its coordinate rounded to 4 decimal
/// places, and re-served for a fixed 1-hour TTL.
/// </summary>
/// <remarks>
/// A single met.no fetch fills every hour that point needs — the whole timeline is
/// cached, not each hour separately. The payload is the mapped <see cref="WeatherTimeline"/>
/// (every hour met.no returned for the point), stored as JSON; because this slice does
/// no conditional requests or <c>Expires</c> handling, the raw upstream bytes are not
/// needed. Lookups are sequential with a fixed 1-hour TTL: a warm key (fetched under an
/// hour ago) is served from cache with no met.no call; a cold or expired key fetches
/// once and upserts. Coordinates already arrive rounded from the Domain; they are
/// rounded again here so the cache key is stable no matter which caller supplies them.
/// </remarks>
public sealed class CachingWeatherProvider(
    IWeatherProvider inner,
    WaytherDbContext db,
    TimeProvider time) : IWeatherProvider
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    // The cache key rounds to the same ≤4 dp met.no asks for (~11 m), so nearby
    // samples share a cached timeline.
    private const int CoordinateDecimals = 4;

    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    public async Task<WeatherTimeline> GetForecastAsync(
        Coordinate location, CancellationToken cancellationToken = default)
    {
        var key = location.Rounded(CoordinateDecimals);
        var now = time.GetUtcNow();

        var cached = await db.ForecastCache.FindAsync([key.Latitude, key.Longitude], cancellationToken);
        if (cached is not null && now - cached.FetchedAt < Ttl)
            return Deserialize(cached.Payload);

        // Cold or expired: one met.no call fills the whole timeline for this point.
        var timeline = await inner.GetForecastAsync(location, cancellationToken);
        var payload = JsonSerializer.Serialize(timeline, PayloadJson);

        if (cached is null)
            db.ForecastCache.Add(new ForecastCache
            {
                Lat4 = key.Latitude,
                Lon4 = key.Longitude,
                Payload = payload,
                FetchedAt = now,
            });
        else
        {
            cached.Payload = payload;
            cached.FetchedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return timeline;
    }

    private static WeatherTimeline Deserialize(string payload) =>
        JsonSerializer.Deserialize<WeatherTimeline>(payload, PayloadJson)
        ?? throw new InvalidOperationException("Cached forecast payload was empty or invalid.");
}
