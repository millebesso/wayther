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
/// sampling interval it resolves the route (via <see cref="IRoutingProvider"/>),
/// works out where the traveller will be and when at each interval along it, and
/// attaches the nearest-hour forecast (via <see cref="IWeatherProvider"/>) to each.
/// Depends only on the two provider seams so it can be unit-tested with faked
/// providers and no network.
/// </summary>
/// <remarks>
/// Position-at-time is interpolated over the route geometry using the cumulative
/// per-segment durations ORS annotates the route with — so a leg driven slowly
/// consumes more of the clock than an equally-long leg driven fast, rather than
/// assuming one constant speed for the whole trip. For each sample the coordinate
/// is rounded to 4 decimal places before querying met.no (met.no requests this; it
/// also improves cache hit rate), and the timeline's nearest-hour entry to the
/// sample's arrival time is chosen — no temporal interpolation.
/// </remarks>
public sealed class RouteForecastService(IRoutingProvider routing, IWeatherProvider weather)
{
    // met.no asks callers to round coordinates to ≤4 decimal places (~11 m).
    private const int CoordinateDecimals = 4;

    public async Task<RouteForecast> GetRouteForecastAsync(
        Coordinate origin,
        Coordinate destination,
        DateTimeOffset departureTime,
        TimeSpan interval,
        CancellationToken cancellationToken = default)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must be positive.");

        var route = await routing.GetRouteAsync(origin, destination, cancellationToken);

        var samples = new List<RouteSample>();
        foreach (var (position, time) in SampleRoute(route, departureTime, interval))
        {
            // Fetches are sequential; the IWeatherProvider seam is cache-backed, so
            // overlapping samples and repeated routes are served without re-hitting met.no.
            var timeline = await weather.GetForecastAsync(position.Rounded(CoordinateDecimals), cancellationToken);
            samples.Add(new RouteSample(position, time, NearestHour(timeline, time)));
        }

        return new RouteForecast(route, samples);
    }

    /// <summary>
    /// Produces a timed position at the origin@departure, then one every
    /// <paramref name="interval"/> of travel time, and always the arrival point as
    /// the final one — even when the arrival does not fall on an interval boundary.
    /// </summary>
    private static IEnumerable<(Coordinate Position, DateTimeOffset Time)> SampleRoute(
        Route route,
        DateTimeOffset departureTime,
        TimeSpan interval)
    {
        var geometry = route.Geometry;
        if (geometry.Count == 0)
            yield break;

        var vertexTimes = CumulativeVertexTimes(route);
        var totalTravelSeconds = vertexTimes[^1];
        var intervalSeconds = interval.TotalSeconds;

        for (var elapsed = 0.0; elapsed < totalTravelSeconds; elapsed += intervalSeconds)
        {
            var position = PositionAt(elapsed, geometry, vertexTimes, totalTravelSeconds);
            yield return (position, departureTime.AddSeconds(elapsed));
        }

        // The arrival is always the final sample, regardless of where the interval landed.
        yield return (geometry[^1], departureTime.AddSeconds(totalTravelSeconds));
    }

    /// <summary>
    /// Selects the timeline entry whose hour is nearest the sample's arrival time —
    /// no interpolation. On an exact tie the earlier hour wins.
    /// </summary>
    private static WeatherForecast NearestHour(WeatherTimeline timeline, DateTimeOffset time)
    {
        if (timeline.Hours.Count == 0)
            throw new InvalidOperationException("Weather timeline contained no hourly entries.");

        var nearest = timeline.Hours[0];
        var nearestDelta = (nearest.Time - time).Duration();
        foreach (var hour in timeline.Hours)
        {
            var delta = (hour.Time - time).Duration();
            if (delta < nearestDelta)
            {
                nearest = hour;
                nearestDelta = delta;
            }
        }

        return nearest.Forecast;
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
