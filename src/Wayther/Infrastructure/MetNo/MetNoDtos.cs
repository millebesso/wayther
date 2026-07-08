using System.Text.Json.Serialization;

namespace Wayther.Infrastructure.MetNo;

// Wire types for met.no's LocationForecast 2.0 "compact" response. Each timestep
// carries an `instant` block (the air temperature at that moment) and, for the
// hours within the hourly-resolution window, a `next_1_hours` block (the weather
// symbol and the precipitation expected over the following hour) — the pieces this
// slice selects the nearest hour from.

internal sealed record MetNoForecastResponse(
    [property: JsonPropertyName("properties")] MetNoProperties? Properties);

internal sealed record MetNoProperties(
    [property: JsonPropertyName("timeseries")] IReadOnlyList<MetNoTimestep>? Timeseries);

internal sealed record MetNoTimestep(
    [property: JsonPropertyName("time")] DateTimeOffset Time,
    [property: JsonPropertyName("data")] MetNoData? Data);

internal sealed record MetNoData(
    [property: JsonPropertyName("instant")] MetNoInstant? Instant,
    [property: JsonPropertyName("next_1_hours")] MetNoNextHours? Next1Hours);

internal sealed record MetNoInstant(
    [property: JsonPropertyName("details")] MetNoInstantDetails? Details);

internal sealed record MetNoInstantDetails(
    [property: JsonPropertyName("air_temperature")] double? AirTemperature);

internal sealed record MetNoNextHours(
    [property: JsonPropertyName("summary")] MetNoSummary? Summary,
    [property: JsonPropertyName("details")] MetNoNextHoursDetails? Details);

internal sealed record MetNoSummary(
    [property: JsonPropertyName("symbol_code")] string? SymbolCode);

internal sealed record MetNoNextHoursDetails(
    [property: JsonPropertyName("precipitation_amount")] double? PrecipitationAmount);
