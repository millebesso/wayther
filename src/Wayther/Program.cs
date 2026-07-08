using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wayther.Api;
using Wayther.Domain;
using Wayther.Infrastructure;
using Wayther.Infrastructure.MetNo;
using Wayther.Infrastructure.OpenRouteService;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "Connection string 'Postgres' is not configured. " +
        "Set ConnectionStrings__Postgres (see .env.example).");

builder.Services.AddDbContext<WaytherDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

// OpenRouteService routing. Non-secret settings come from configuration; the API
// key is injected separately from the git-ignored .env as ORS_API_KEY.
builder.Services.Configure<OpenRouteServiceOptions>(
    builder.Configuration.GetSection(OpenRouteServiceOptions.SectionName));
builder.Services.PostConfigure<OpenRouteServiceOptions>(options =>
    options.ApiKey = builder.Configuration["ORS_API_KEY"] ?? options.ApiKey);

builder.Services.AddHttpClient<IRoutingProvider, OpenRouteServiceRoutingProvider>((sp, http) =>
{
    var options = sp.GetRequiredService<IOptions<OpenRouteServiceOptions>>().Value;
    http.BaseAddress = new Uri(options.BaseUrl);
    // ORS sends the raw API key as the Authorization value (not a "scheme token"
    // pair), so skip the structured-header validation that rejects it.
    http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", options.ApiKey);
});

// MET Norway weather. Non-secret settings come from configuration; the required
// identifying User-Agent (with contact info) is injected from the git-ignored
// .env as METNO_USER_AGENT.
builder.Services.Configure<MetNoOptions>(
    builder.Configuration.GetSection(MetNoOptions.SectionName));
builder.Services.PostConfigure<MetNoOptions>(options =>
    options.UserAgent = builder.Configuration["METNO_USER_AGENT"] ?? options.UserAgent);

builder.Services.AddHttpClient<IWeatherProvider, MetNoWeatherProvider>((sp, http) =>
{
    var options = sp.GetRequiredService<IOptions<MetNoOptions>>().Value;
    http.BaseAddress = new Uri(options.BaseUrl);
});

// The pure-Domain orchestrator that turns the route into timed samples.
builder.Services.AddScoped<RouteForecastService>();

var app = builder.Build();

// Apply migrations on startup so the containerized Postgres self-provisions.
await ApplyMigrationsAsync(app);

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthEndpoints();
app.MapRouteForecastEndpoints();

// SPA fallback: serve the React index.html for any non-file route that isn't
// under /api, so unmatched API paths return 404 instead of the HTML shell.
// The `nonfile` constraint is required so real static assets (/assets/*.js,
// /favicon.svg) are not captured here — otherwise routing assigns them this
// endpoint and UseStaticFiles skips them, serving the HTML shell instead.
app.MapFallbackToFile("{*path:nonfile:regex(^(?!api/).*$)}", "index.html");

app.Run();

static async Task ApplyMigrationsAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WaytherDbContext>();
    await db.Database.MigrateAsync();
}
