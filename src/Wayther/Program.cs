using Microsoft.EntityFrameworkCore;
using Wayther.Api;
using Wayther.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "Connection string 'Postgres' is not configured. " +
        "Set ConnectionStrings__Postgres (see .env.example).");

builder.Services.AddDbContext<WaytherDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

var app = builder.Build();

// Apply migrations on startup so the containerized Postgres self-provisions.
await ApplyMigrationsAsync(app);

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthEndpoints();

// SPA fallback: serve the React index.html for any non-file route that isn't
// under /api, so unmatched API paths return 404 instead of the HTML shell.
app.MapFallbackToFile("{*path:regex(^(?!api/).*$)}", "index.html");

app.Run();

static async Task ApplyMigrationsAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WaytherDbContext>();
    await db.Database.MigrateAsync();
}
