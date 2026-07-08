using Wayther.Domain;

namespace Wayther.Tests;

/// <summary>
/// Exercises the pure-Domain sampling logic through the <see cref="IRoutingProvider"/>
/// and <see cref="IWeatherProvider"/> seams with faked providers — no network. The
/// route geometry and per-segment durations are canned; the assertions are on the
/// produced samples (where and when), never on internals.
/// </summary>
public class RouteForecastServiceTests
{
    private static readonly Coordinate Origin = new(0, 0);
    private static readonly Coordinate Destination = new(0, 10);
    private static readonly DateTimeOffset Departure = new(2026, 7, 8, 9, 0, 0, TimeSpan.Zero);

    // A two-leg route driven at uneven pace: the first leg is geographically short
    // (0 → 0.1° lon) yet takes a full hour; the second is long (0.1 → 10° lon) and
    // also takes an hour. Total travel time 2h. A constant-speed-by-distance guess
    // would place the traveller far down the long leg after 30 min; per-segment
    // durations put them halfway along the short leg instead.
    private static Route UnevenPaceRoute() => new(
        Geometry: [new(0, 0), new(0, 0.1), new(0, 10)],
        Segments:
        [
            new RouteSegment(StartIndex: 0, EndIndex: 1, DistanceMeters: 100, DurationSeconds: 3600),
            new RouteSegment(StartIndex: 1, EndIndex: 2, DistanceMeters: 100_000, DurationSeconds: 3600),
        ],
        TotalDistanceMeters: 100_100,
        TotalDurationSeconds: 7200);

    private static Route SingleSegmentRoute(double durationSeconds, Coordinate end) => new(
        Geometry: [new(0, 0), end],
        Segments: [new RouteSegment(0, 1, DistanceMeters: 1000, DurationSeconds: durationSeconds)],
        TotalDistanceMeters: 1000,
        TotalDurationSeconds: durationSeconds);

    [Fact]
    public async Task Samples_every_30_minutes_plus_arrival()
    {
        var service = CreateService(UnevenPaceRoute());

        var forecast = await service.GetRouteForecastAsync(
            Origin, Destination, Departure, TimeSpan.FromMinutes(30));

        // 0, 30, 60, 90 min of travel, then the 120-min arrival always appended.
        Assert.Equal(5, forecast.Samples.Count);
        Assert.Collection(
            forecast.Samples.Select(s => s.Time),
            t => Assert.Equal(Departure, t),
            t => Assert.Equal(Departure.AddMinutes(30), t),
            t => Assert.Equal(Departure.AddMinutes(60), t),
            t => Assert.Equal(Departure.AddMinutes(90), t),
            t => Assert.Equal(Departure.AddMinutes(120), t));
    }

    [Fact]
    public async Task Samples_every_60_minutes_plus_arrival()
    {
        var service = CreateService(UnevenPaceRoute());

        var forecast = await service.GetRouteForecastAsync(
            Origin, Destination, Departure, TimeSpan.FromMinutes(60));

        // 0, 60 min of travel, then the 120-min arrival.
        Assert.Equal(3, forecast.Samples.Count);
        Assert.Equal(Departure, forecast.Samples[0].Time);
        Assert.Equal(Departure.AddMinutes(60), forecast.Samples[1].Time);
        Assert.Equal(Departure.AddMinutes(120), forecast.Samples[2].Time);
    }

    [Fact]
    public async Task First_sample_is_origin_and_final_sample_is_arrival()
    {
        var service = CreateService(UnevenPaceRoute());

        var forecast = await service.GetRouteForecastAsync(
            Origin, Destination, Departure, TimeSpan.FromMinutes(30));

        AssertCoordinate(new(0, 0), forecast.Samples[0].Position);
        AssertCoordinate(new(0, 10), forecast.Samples[^1].Position);
    }

    [Fact]
    public async Task Position_at_time_honours_per_segment_durations_not_uniform_distance()
    {
        var service = CreateService(UnevenPaceRoute());

        var forecast = await service.GetRouteForecastAsync(
            Origin, Destination, Departure, TimeSpan.FromMinutes(30));

        // 30 min = halfway (by time) through the short first leg → midpoint of it,
        // NOT ~25% of the total distance down the long leg.
        AssertCoordinate(new(0, 0.05), forecast.Samples[1].Position);
        // 60 min = exactly the segment boundary.
        AssertCoordinate(new(0, 0.1), forecast.Samples[2].Position);
        // 90 min = halfway (by time) through the long second leg → its midpoint.
        AssertCoordinate(new(0, 5.05), forecast.Samples[3].Position);
    }

    [Fact]
    public async Task Position_within_a_multi_vertex_segment_is_spread_by_leg_length()
    {
        // A single 60-min segment spanning three vertices, whose two legs have very
        // different geographic lengths (0→1° then 1→10°). With no finer timing than
        // the segment's duration, position-at-time is spread by leg length: the
        // short first leg is cleared quickly, the long second leg fills the rest.
        var route = new Route(
            Geometry: [new(0, 0), new(0, 1), new(0, 10)],
            Segments: [new RouteSegment(StartIndex: 0, EndIndex: 2, DistanceMeters: 1000, DurationSeconds: 3600)],
            TotalDistanceMeters: 1000,
            TotalDurationSeconds: 3600);
        var service = CreateService(route);

        var forecast = await service.GetRouteForecastAsync(
            Origin, Destination, Departure, TimeSpan.FromMinutes(30));

        // The first leg is ~1/10 of the length, so it takes ~6 min; the 30-min
        // sample is ~24 min into the ~54-min second leg → ~24/54 of the way from 1°
        // to 10° ≈ 5°. A uniform-per-leg split would instead sit at the boundary
        // (1°) at 30 min. The two decimal places cleanly separate the two behaviours
        // while tolerating haversine's slight non-linearity across the long leg.
        Assert.Equal(3, forecast.Samples.Count);
        var thirtyMin = forecast.Samples[1];
        Assert.Equal(Departure.AddMinutes(30), thirtyMin.Time);
        Assert.Equal(0, thirtyMin.Position.Latitude, 6);
        Assert.Equal(5.0, thirtyMin.Position.Longitude, 2);
    }

    [Fact]
    public async Task Arrival_is_included_even_when_it_misses_an_interval_boundary()
    {
        // 90-minute route sampled every 60 min: samples land at 0 and 60 min, and
        // the arrival at 90 min is appended even though it is not a 60-min multiple.
        var service = CreateService(SingleSegmentRoute(durationSeconds: 5400, end: Destination));

        var forecast = await service.GetRouteForecastAsync(
            Origin, Destination, Departure, TimeSpan.FromMinutes(60));

        Assert.Equal(3, forecast.Samples.Count);
        Assert.Equal(Departure.AddMinutes(60), forecast.Samples[1].Time);
        Assert.Equal(Departure.AddMinutes(90), forecast.Samples[^1].Time);
        AssertCoordinate(Destination, forecast.Samples[^1].Position);
    }

    [Fact]
    public async Task Route_shorter_than_one_interval_still_yields_origin_and_arrival()
    {
        // 10-minute route sampled every 30 min: only the origin and the arrival.
        var end = new Coordinate(0, 0.05);
        var service = CreateService(SingleSegmentRoute(durationSeconds: 600, end: end));

        var forecast = await service.GetRouteForecastAsync(
            Origin, end, Departure, TimeSpan.FromMinutes(30));

        Assert.Equal(2, forecast.Samples.Count);
        AssertCoordinate(new(0, 0), forecast.Samples[0].Position);
        Assert.Equal(Departure, forecast.Samples[0].Time);
        AssertCoordinate(end, forecast.Samples[^1].Position);
        Assert.Equal(Departure.AddMinutes(10), forecast.Samples[^1].Time);
    }

    [Fact]
    public async Task Response_carries_the_route_geometry_for_the_polyline()
    {
        var route = UnevenPaceRoute();
        var service = CreateService(route);

        var forecast = await service.GetRouteForecastAsync(
            Origin, Destination, Departure, TimeSpan.FromMinutes(30));

        Assert.Equal(route.Geometry, forecast.Route.Geometry);
    }

    [Fact]
    public async Task Non_positive_interval_is_rejected()
    {
        var service = CreateService(UnevenPaceRoute());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.GetRouteForecastAsync(Origin, Destination, Departure, TimeSpan.Zero));
    }

    private static RouteForecastService CreateService(Route route) =>
        new(new FakeRoutingProvider(route), new UnusedWeatherProvider());

    private static void AssertCoordinate(Coordinate expected, Coordinate actual)
    {
        Assert.Equal(expected.Latitude, actual.Latitude, 6);
        Assert.Equal(expected.Longitude, actual.Longitude, 6);
    }

    private sealed class FakeRoutingProvider(Route route) : IRoutingProvider
    {
        public Task<Route> GetRouteAsync(
            Coordinate origin, Coordinate destination, CancellationToken cancellationToken = default) =>
            Task.FromResult(route);
    }

    // This slice produces samples but no weather. If the sampling logic ever reaches
    // for the weather seam, that is a bug — so fail loudly rather than silently.
    private sealed class UnusedWeatherProvider : IWeatherProvider
    {
        public Task<WeatherForecast> GetForecastAsync(
            Coordinate location, DateTimeOffset time, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Weather is not sampled in this slice.");
    }
}
