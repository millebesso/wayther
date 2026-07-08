using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Wayther.Domain;
using Wayther.Infrastructure.MetNo;

namespace Wayther.Tests;

public class MetNoWeatherProviderTests
{
    // A trimmed LocationForecast "compact" response: two hourly timesteps that carry
    // a next_1_hours block, then a later step that has none (beyond the hourly
    // window). The temperature comes from `instant`, the symbol and precipitation
    // from `next_1_hours`.
    private const string CannedForecast = """
    {
      "properties": {
        "timeseries": [
          {
            "time": "2026-07-08T09:00:00Z",
            "data": {
              "instant": { "details": { "air_temperature": 15.2 } },
              "next_1_hours": {
                "summary": { "symbol_code": "cloudy" },
                "details": { "precipitation_amount": 0.0 }
              }
            }
          },
          {
            "time": "2026-07-08T10:00:00Z",
            "data": {
              "instant": { "details": { "air_temperature": 16.8 } },
              "next_1_hours": {
                "summary": { "symbol_code": "rain" },
                "details": { "precipitation_amount": 1.4 }
              }
            }
          },
          {
            "time": "2026-07-10T18:00:00Z",
            "data": {
              "instant": { "details": { "air_temperature": 12.0 } },
              "next_6_hours": {
                "summary": { "symbol_code": "partlycloudy_day" }
              }
            }
          }
        ]
      }
    }
    """;

    [Fact]
    public async Task GetForecastAsync_sends_identifying_user_agent()
    {
        var handler = new CapturingHandler(CannedForecast);
        var provider = CreateProvider(handler, userAgent: "wayther/0.1 (ops@wayther.test)");

        await provider.GetForecastAsync(new Coordinate(59.9139, 10.7522));

        Assert.True(handler.Request!.Headers.Contains("User-Agent"));
        Assert.Equal(
            "wayther/0.1 (ops@wayther.test)",
            string.Join(" ", handler.Request.Headers.GetValues("User-Agent")));
    }

    [Fact]
    public async Task GetForecastAsync_queries_the_compact_endpoint_with_the_coordinate()
    {
        var handler = new CapturingHandler(CannedForecast);
        var provider = CreateProvider(handler);

        await provider.GetForecastAsync(new Coordinate(59.9139, 10.7522));

        var uri = handler.Request!.RequestUri!;
        Assert.Equal("/weatherapi/locationforecast/2.0/compact", uri.AbsolutePath);
        // Invariant formatting: the decimal point is a dot, never a locale comma.
        Assert.Equal("?lat=59.9139&lon=10.7522", uri.Query);
    }

    [Fact]
    public async Task GetForecastAsync_maps_hourly_timesteps_to_the_timeline()
    {
        var provider = CreateProvider(new CapturingHandler(CannedForecast));

        var timeline = await provider.GetForecastAsync(new Coordinate(59.9139, 10.7522));

        Assert.Collection(
            timeline.Hours,
            first =>
            {
                Assert.Equal(new DateTimeOffset(2026, 7, 8, 9, 0, 0, TimeSpan.Zero), first.Time);
                Assert.Equal("cloudy", first.Forecast.SymbolCode);
                Assert.Equal(15.2, first.Forecast.TemperatureCelsius);
                Assert.Equal(0.0, first.Forecast.PrecipitationMm);
            },
            second =>
            {
                Assert.Equal(new DateTimeOffset(2026, 7, 8, 10, 0, 0, TimeSpan.Zero), second.Time);
                Assert.Equal("rain", second.Forecast.SymbolCode);
                Assert.Equal(16.8, second.Forecast.TemperatureCelsius);
                Assert.Equal(1.4, second.Forecast.PrecipitationMm);
            });
    }

    [Fact]
    public async Task GetForecastAsync_drops_timesteps_without_a_next_1_hours_block()
    {
        var provider = CreateProvider(new CapturingHandler(CannedForecast));

        var timeline = await provider.GetForecastAsync(new Coordinate(59.9139, 10.7522));

        // The 2026-07-10 step only has next_6_hours, so it is not an hourly bucket.
        Assert.Equal(2, timeline.Hours.Count);
        Assert.DoesNotContain(
            timeline.Hours,
            hour => hour.Time == new DateTimeOffset(2026, 7, 10, 18, 0, 0, TimeSpan.Zero));
    }

    private static MetNoWeatherProvider CreateProvider(
        HttpMessageHandler handler, string userAgent = "wayther/0.1 (test@wayther.test)")
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://met.test") };
        var options = Options.Create(new MetNoOptions { UserAgent = userAgent });
        return new MetNoWeatherProvider(http, options);
    }

    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        }
    }
}
