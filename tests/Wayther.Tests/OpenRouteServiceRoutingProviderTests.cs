using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Wayther.Domain;
using Wayther.Infrastructure.OpenRouteService;

namespace Wayther.Tests;

public class OpenRouteServiceRoutingProviderTests
{
    private const string CannedGeoJson = """
    {
      "type": "FeatureCollection",
      "features": [
        {
          "type": "Feature",
          "properties": {
            "summary": { "distance": 1000.0, "duration": 120.0 },
            "segments": [
              {
                "distance": 1000.0,
                "duration": 120.0,
                "steps": [
                  { "distance": 600.0, "duration": 72.0, "way_points": [0, 2] },
                  { "distance": 400.0, "duration": 48.0, "way_points": [2, 3] }
                ]
              }
            ]
          },
          "geometry": {
            "type": "LineString",
            "coordinates": [
              [10.0, 59.0],
              [10.1, 59.1],
              [10.2, 59.2],
              [10.3, 59.3]
            ]
          }
        }
      ]
    }
    """;

    [Fact]
    public async Task GetRouteAsync_posts_lon_lat_coordinates_and_requests_instructions()
    {
        var handler = new CapturingHandler(CannedGeoJson);
        var provider = CreateProvider(handler);

        await provider.GetRouteAsync(
            new Coordinate(Latitude: 59.0, Longitude: 10.0),
            new Coordinate(Latitude: 60.0, Longitude: 11.0));

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(
            "/v2/directions/driving-car/geojson",
            handler.Request.RequestUri!.AbsolutePath);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        var coordinates = body.RootElement.GetProperty("coordinates");
        // ORS expects [longitude, latitude] pairs, origin first.
        Assert.Equal(10.0, coordinates[0][0].GetDouble());
        Assert.Equal(59.0, coordinates[0][1].GetDouble());
        Assert.Equal(11.0, coordinates[1][0].GetDouble());
        Assert.Equal(60.0, coordinates[1][1].GetDouble());
        // instructions=true is what makes ORS return the per-segment durations.
        Assert.True(body.RootElement.GetProperty("instructions").GetBoolean());
    }

    [Fact]
    public async Task GetRouteAsync_maps_geometry_to_lat_lon()
    {
        var provider = CreateProvider(new CapturingHandler(CannedGeoJson));

        var route = await provider.GetRouteAsync(
            new Coordinate(59.0, 10.0),
            new Coordinate(59.3, 10.3));

        Assert.Equal(4, route.Geometry.Count);
        // GeoJSON is [lon, lat]; the domain coordinate is lat/lon.
        Assert.Equal(new Coordinate(59.0, 10.0), route.Geometry[0]);
        Assert.Equal(new Coordinate(59.3, 10.3), route.Geometry[3]);
    }

    [Fact]
    public async Task GetRouteAsync_carries_per_segment_durations_mapped_to_geometry_indices()
    {
        var provider = CreateProvider(new CapturingHandler(CannedGeoJson));

        var route = await provider.GetRouteAsync(
            new Coordinate(59.0, 10.0),
            new Coordinate(59.3, 10.3));

        Assert.Collection(
            route.Segments,
            first =>
            {
                Assert.Equal(0, first.StartIndex);
                Assert.Equal(2, first.EndIndex);
                Assert.Equal(72.0, first.DurationSeconds);
                Assert.Equal(600.0, first.DistanceMeters);
            },
            second =>
            {
                Assert.Equal(2, second.StartIndex);
                Assert.Equal(3, second.EndIndex);
                Assert.Equal(48.0, second.DurationSeconds);
                Assert.Equal(400.0, second.DistanceMeters);
            });

        Assert.Equal(1000.0, route.TotalDistanceMeters);
        Assert.Equal(120.0, route.TotalDurationSeconds);
    }

    private static OpenRouteServiceRoutingProvider CreateProvider(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://ors.test") };
        var options = Options.Create(new OpenRouteServiceOptions { Profile = "driving-car" });
        return new OpenRouteServiceRoutingProvider(http, options);
    }

    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }
}
