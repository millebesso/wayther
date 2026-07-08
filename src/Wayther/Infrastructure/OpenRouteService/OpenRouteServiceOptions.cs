namespace Wayther.Infrastructure.OpenRouteService;

/// <summary>Configuration for the OpenRouteService routing client.</summary>
public sealed class OpenRouteServiceOptions
{
    public const string SectionName = "OpenRouteService";

    /// <summary>Base URL of the ORS directions API.</summary>
    public string BaseUrl { get; set; } = "https://api.openrouteservice.org";

    /// <summary>Routing profile, e.g. <c>driving-car</c>.</summary>
    public string Profile { get; set; } = "driving-car";

    /// <summary>
    /// API key sent in the <c>Authorization</c> header. Supplied via the
    /// git-ignored <c>.env</c> as <c>ORS_API_KEY</c>; never committed.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
