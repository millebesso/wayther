using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wayther.Infrastructure;

namespace Wayther.Api;

public static class ShareEndpoints
{
    // The slug that appears in a /m/{id} link: 6 characters drawn from a base62
    // alphabet (~56.8 billion values) — short enough to share, unguessable enough
    // that routes can't be walked by incrementing an id.
    private const string SlugAlphabet =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int SlugLength = 6;

    // Collisions at 6 chars are astronomically rare, but not impossible; on the
    // unique-constraint violation we regenerate and retry a handful of times before
    // giving up rather than pretend it can't happen.
    private const int MaxInsertAttempts = 5;

    // Stored waypoints use the same camelCase wire shape as the rest of the API, so a
    // GET hands them back to the frontend unchanged (`{ "lat": .., "lon": .. }`).
    private static readonly JsonSerializerOptions WaypointJson =
        new(JsonSerializerDefaults.Web);

    public static void MapShareEndpoints(this WebApplication app)
    {
        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Wayther.Shares");

        // Persist a route's inputs (waypoints + departure + interval) as a shareable
        // snapshot and return its slug. The forecast is never stored — opening the
        // share re-requests a fresh one for these inputs.
        app.MapPost("/api/shares", async (
            ShareRequest request,
            WaytherDbContext db,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var validationError = RouteInputValidation.Validate(request.Waypoints, request.IntervalMinutes);
            if (validationError is not null)
                return Results.BadRequest(validationError);

            var waypointsJson = JsonSerializer.Serialize(request.Waypoints, WaypointJson);

            for (var attempt = 1; attempt <= MaxInsertAttempts; attempt++)
            {
                var share = new SharedRoute
                {
                    Id = GenerateSlug(),
                    Waypoints = waypointsJson,
                    // Npgsql's `timestamp with time zone` stores an instant and accepts
                    // only a UTC DateTimeOffset, so normalize any incoming offset to UTC
                    // (the instant is preserved) before persisting.
                    DepartureTime = request.DepartureTime.ToUniversalTime(),
                    IntervalMinutes = request.IntervalMinutes,
                    CreatedAt = timeProvider.GetUtcNow(),
                };

                db.SharedRoutes.Add(share);
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    logger.LogInformation(
                        "Share created {Id}: {WaypointCount} waypoints departing {DepartureTime:o} every {IntervalMinutes}min",
                        share.Id, request.Waypoints!.Count, request.DepartureTime, request.IntervalMinutes);
                    return Results.Created($"/m/{share.Id}", new ShareCreatedResponse(share.Id));
                }
                catch (DbUpdateException) when (attempt < MaxInsertAttempts)
                {
                    // Almost certainly a slug collision on the primary key. Detach the
                    // failed entity so the retry starts from a clean tracker, then loop
                    // with a freshly generated slug.
                    db.Entry(share).State = EntityState.Detached;
                    logger.LogWarning("Share slug collision on {Id}; retrying (attempt {Attempt})", share.Id, attempt);
                }
            }

            // Exhausted our attempts — treat as an unexpected server-side failure.
            logger.LogError("Share creation failed after {Attempts} slug attempts", MaxInsertAttempts);
            return Results.Problem("Could not create share link. Please try again.");
        });

        // Return a stored share's inputs by slug so the frontend can hydrate the editor
        // and re-request the forecast. Unknown slugs are a 404 the frontend maps to its
        // own "invalid or expired link" copy.
        app.MapGet("/api/shares/{id}", async (
            string id,
            WaytherDbContext db,
            CancellationToken cancellationToken) =>
        {
            var share = await db.SharedRoutes
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (share is null)
                return Results.NotFound();

            var waypoints = JsonSerializer.Deserialize<List<PointDto>>(share.Waypoints, WaypointJson)
                ?? [];

            return Results.Ok(new ShareResponse(waypoints, share.DepartureTime, share.IntervalMinutes));
        });
    }

    private static string GenerateSlug() =>
        RandomNumberGenerator.GetString(SlugAlphabet, SlugLength);
}

/// <summary>The inputs to persist as a shareable route snapshot.</summary>
public sealed record ShareRequest(
    IReadOnlyList<PointDto> Waypoints,
    DateTimeOffset DepartureTime,
    int IntervalMinutes);

/// <summary>The slug of a freshly created share; the frontend builds <c>/m/{id}</c> from it.</summary>
public sealed record ShareCreatedResponse(string Id);

/// <summary>A stored share's inputs, enough to reopen and re-forecast the trip.</summary>
public sealed record ShareResponse(
    IReadOnlyList<PointDto> Waypoints,
    DateTimeOffset DepartureTime,
    int IntervalMinutes);
