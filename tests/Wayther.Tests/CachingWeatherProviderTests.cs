using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Wayther.Domain;
using Wayther.Infrastructure;

namespace Wayther.Tests;

/// <summary>
/// Exercises the <see cref="CachingWeatherProvider"/> against a real Postgres (via
/// Testcontainers) — the cache's whole reason to exist is its behaviour against the
/// database, so an in-memory fake would prove nothing. A counting fake stands in for
/// met.no so "no met.no call" is observable, and a fixed <see cref="TimeProvider"/>
/// drives the 1-hour TTL. Covers the three states in the slice: cold-miss, warm-hit,
/// expired-refetch.
/// </summary>
public sealed class CachingWeatherProviderTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 7, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly Coordinate Oslo = new(59.9139, 10.7522);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewDbContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Cold_key_fetches_from_met_no_once_and_stores_the_full_payload()
    {
        var metno = new CountingWeatherProvider(Timeline("cold"));
        var time = new FixedTimeProvider(Now);

        var timeline = await Cache(metno, time).GetForecastAsync(Oslo);

        Assert.Equal(1, metno.Calls);
        AssertSameTimeline(Timeline("cold"), timeline);

        await using var db = NewDbContext();
        var row = await db.ForecastCache.SingleAsync();
        Assert.Equal(59.9139, row.Lat4);
        Assert.Equal(10.7522, row.Lon4);
        Assert.Equal(Now, row.FetchedAt);
        Assert.False(string.IsNullOrWhiteSpace(row.Payload));
    }

    [Fact]
    public async Task Warm_key_is_served_from_cache_with_no_met_no_call()
    {
        var time = new FixedTimeProvider(Now);

        // First run populates the cache with one met.no call.
        var first = new CountingWeatherProvider(Timeline("warm"));
        await Cache(first, time).GetForecastAsync(Oslo);
        Assert.Equal(1, first.Calls);

        // 30 minutes later — within the 1-hour TTL — a fresh request serves the cache.
        time.Now = Now.AddMinutes(30);
        var second = new CountingWeatherProvider(Timeline("should-not-be-used"));
        var timeline = await Cache(second, time).GetForecastAsync(Oslo);

        Assert.Equal(0, second.Calls); // no met.no call on the warm hit
        AssertSameTimeline(Timeline("warm"), timeline);
    }

    [Fact]
    public async Task Expired_key_triggers_a_refetch_and_upserts_a_single_row()
    {
        var time = new FixedTimeProvider(Now);

        var first = new CountingWeatherProvider(Timeline("stale"));
        await Cache(first, time).GetForecastAsync(Oslo);

        // 90 minutes later — past the 1-hour TTL — the key is expired and refetched.
        time.Now = Now.AddMinutes(90);
        var second = new CountingWeatherProvider(Timeline("fresh"));
        var timeline = await Cache(second, time).GetForecastAsync(Oslo);

        Assert.Equal(1, second.Calls);
        AssertSameTimeline(Timeline("fresh"), timeline);

        await using var db = NewDbContext();
        // Upsert, not insert: still a single row, now carrying the refetch time.
        var row = await db.ForecastCache.SingleAsync();
        Assert.Equal(Now.AddMinutes(90), row.FetchedAt);
    }

    private CachingWeatherProvider Cache(IWeatherProvider inner, TimeProvider time) =>
        new(inner, NewDbContext(), time);

    private WaytherDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<WaytherDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    /// <summary>A two-hour timeline tagged with <paramref name="tag"/> so callers can tell instances apart.</summary>
    private static WeatherTimeline Timeline(string tag) => new(
    [
        new WeatherHour(Now, new WeatherForecast($"{tag}-h09", 15.2, 0.0)),
        new WeatherHour(Now.AddHours(1), new WeatherForecast($"{tag}-h10", 16.8, 1.4)),
    ]);

    private static void AssertSameTimeline(WeatherTimeline expected, WeatherTimeline actual) =>
        Assert.Equal(expected.Hours, actual.Hours);

    private sealed class CountingWeatherProvider(WeatherTimeline timeline) : IWeatherProvider
    {
        public int Calls { get; private set; }

        public Task<WeatherTimeline> GetForecastAsync(
            Coordinate location, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(timeline);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
