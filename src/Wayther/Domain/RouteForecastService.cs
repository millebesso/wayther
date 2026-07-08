namespace Wayther.Domain;

/// <summary>
/// The resolved route together with the timed samples taken along it. The
/// <see cref="Route"/> carries the full geometry (so the frontend can draw the
/// polyline); <see cref="Samples"/> is the ordered set of places-and-times the
/// forecast will hang off.
/// </summary>
public sealed record RouteForecast(Route Route, IReadOnlyList<RouteSample> Samples);

/// <summary>
/// The pure-Domain orchestrator. Given the two endpoints, a departure time and a
/// sampling interval it resolves the route (via <see cref="IRoutingProvider"/>)
/// and works out where the traveller will be, and when, at each interval along it.
/// Depends only on the two provider seams so it can be unit-tested with faked
/// providers and no network.
/// </summary>
/// <remarks>
/// Position-at-time is interpolated over the route geometry using the cumulative
/// per-segment durations ORS annotates the route with — so a leg driven slowly
/// consumes more of the clock than an equally-long leg driven fast, rather than
/// assuming one constant speed for the whole trip. The <see cref="IWeatherProvider"/>
/// seam is wired in ready for the next slice, which attaches a forecast to each
/// sample; this slice produces the samples only.
/// </remarks>
public sealed class RouteForecastService(IRoutingProvider routing, IWeatherProvider weather)
{
    public async Task<RouteForecast> GetRouteForecastAsync(
        Coordinate origin,
        Coordinate destination,
        DateTimeOffset departureTime,
        TimeSpan interval,
        CancellationToken cancellationToken = default)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must be positive.");

        // The weather seam is intentionally not consumed yet; see class remarks.
        _ = weather;

        var route = await routing.GetRouteAsync(origin, destination, cancellationToken);
        var samples = SampleRoute(route, departureTime, interval);
        return new RouteForecast(route, samples);
    }

    /// <summary>
    /// Produces a sample at the origin@departure, then one every <paramref name="interval"/>
    /// of travel time, and always the arrival point as the final sample — even when
    /// the arrival does not fall on an interval boundary.
    /// </summary>
    private static IReadOnlyList<RouteSample> SampleRoute(
        Route route,
        DateTimeOffset departureTime,
        TimeSpan interval)
    {
        var geometry = route.Geometry;
        if (geometry.Count == 0)
            return [];

        var vertexTimes = CumulativeVertexTimes(route);
        var totalTravelSeconds = vertexTimes[^1];
        var intervalSeconds = interval.TotalSeconds;

        var samples = new List<RouteSample>();
        for (var elapsed = 0.0; elapsed < totalTravelSeconds; elapsed += intervalSeconds)
        {
            var position = PositionAt(elapsed, geometry, vertexTimes, totalTravelSeconds);
            samples.Add(new RouteSample(position, departureTime.AddSeconds(elapsed)));
        }

        // The arrival is always the final sample, regardless of where the interval landed.
        samples.Add(new RouteSample(geometry[^1], departureTime.AddSeconds(totalTravelSeconds)));
        return samples;
    }

    /// <summary>
    /// The cumulative travel time (seconds) at each geometry vertex. Each route
    /// segment's duration is spread across the vertices it spans in proportion to
    /// their geographic length, so position-at-time reflects the per-segment pace.
    /// </summary>
    private static double[] CumulativeVertexTimes(Route route)
    {
        var geometry = route.Geometry;
        var times = new double[geometry.Count];

        // Fall back to a single whole-route span if the provider gave no segments.
        IEnumerable<(int Start, int End, double Duration)> spans = route.Segments.Count > 0
            ? route.Segments.Select(s => (s.StartIndex, s.EndIndex, s.DurationSeconds))
            : [(0, geometry.Count - 1, route.TotalDurationSeconds)];

        foreach (var (start, end, duration) in spans)
        {
            var legCount = end - start;
            if (legCount <= 0)
                continue;

            var legLengths = new double[legCount];
            var spanLength = 0.0;
            for (var i = start; i < end; i++)
            {
                var length = HaversineMeters(geometry[i], geometry[i + 1]);
                legLengths[i - start] = length;
                spanLength += length;
            }

            for (var i = start; i < end; i++)
            {
                // Split the span's duration by leg length; if the legs have no length
                // (coincident vertices), split it evenly so time still advances.
                var share = spanLength > 0 ? legLengths[i - start] / spanLength : 1.0 / legCount;
                times[i + 1] = times[i] + duration * share;
            }
        }

        return times;
    }

    /// <summary>Interpolates the position reached after <paramref name="elapsedSeconds"/> of travel.</summary>
    private static Coordinate PositionAt(
        double elapsedSeconds,
        IReadOnlyList<Coordinate> geometry,
        double[] vertexTimes,
        double totalTravelSeconds)
    {
        var t = Math.Clamp(elapsedSeconds, 0, totalTravelSeconds);
        for (var i = 0; i < vertexTimes.Length - 1; i++)
        {
            if (t <= vertexTimes[i + 1])
            {
                var span = vertexTimes[i + 1] - vertexTimes[i];
                var fraction = span > 0 ? (t - vertexTimes[i]) / span : 0;
                return Lerp(geometry[i], geometry[i + 1], fraction);
            }
        }

        return geometry[^1];
    }

    private static Coordinate Lerp(Coordinate a, Coordinate b, double fraction) => new(
        a.Latitude + (b.Latitude - a.Latitude) * fraction,
        a.Longitude + (b.Longitude - a.Longitude) * fraction);

    private static double HaversineMeters(Coordinate a, Coordinate b)
    {
        const double earthRadiusMeters = 6_371_000;
        var lat1 = double.DegreesToRadians(a.Latitude);
        var lat2 = double.DegreesToRadians(b.Latitude);
        var dLat = lat2 - lat1;
        var dLon = double.DegreesToRadians(b.Longitude - a.Longitude);

        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * earthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
