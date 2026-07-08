namespace Wayther.Infrastructure.MetNo;

/// <summary>Configuration for the MET Norway (met.no) LocationForecast client.</summary>
public sealed class MetNoOptions
{
    public const string SectionName = "MetNo";

    /// <summary>Base URL of the met.no API.</summary>
    public string BaseUrl { get; set; } = "https://api.met.no";

    /// <summary>
    /// The identifying <c>User-Agent</c> met.no requires on every request (a product
    /// token plus contact info, e.g. <c>wayther/0.1 (you@example.com)</c>). Requests
    /// without it are rejected. Supplied via the git-ignored <c>.env</c> as
    /// <c>METNO_USER_AGENT</c>; never committed.
    /// </summary>
    public string UserAgent { get; set; } = string.Empty;
}
