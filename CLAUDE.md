# wayther

An app for weather forecasts along a predefined route.

## Stack

- **Backend:** ASP.NET Core minimal APIs (.NET 10, C#), EF Core + Npgsql. Single
  project `src/Wayther`.
- **Frontend:** React 19 + TypeScript + Leaflet, built with Vite (`frontend/`).
- **Database:** PostgreSQL — a forecast cache plus stored shared-route snapshots
  (the inputs behind a `/m/{id}` share link; the forecast itself is never stored).
  Migrations apply on startup.
- Fully containerized: one dotnet process serves the API and the built frontend from a
  single origin. See `README.md` for run/dev/migration commands.

## Repo structure

```
src/Wayther/                     ASP.NET backend (single project)
  Program.cs                     DI wiring, middleware, endpoint mapping, startup migrations
  appsettings.json               config + Logging:LogLevel (secrets come from .env)
  Api/                           HTTP layer (minimal-API endpoint groups)
    HealthEndpoints.cs           GET /api/health
    RouteForecastEndpoints.cs    POST /api/route-forecast + request/response DTOs
    ShareEndpoints.cs            POST /api/shares + GET /api/shares/{id} (route sharing)
    RouteInputValidation.cs      waypoint-count + interval rules shared by both POSTs
  Domain/                        pure business logic — no I/O, no logging, provider seams only
    RouteForecastService.cs      orchestrates routing + weather into timed samples
    Route.cs, Coordinate.cs, RouteSample.cs
    IRoutingProvider.cs, IWeatherProvider.cs   the two seams the Domain depends on
  Infrastructure/                external services + persistence (implements the seams)
    WaytherDbContext.cs, ForecastCache.cs, SharedRoute.cs, Migrations/
    CachingWeatherProvider.cs    Postgres-backed 1h forecast cache decorating met.no
    MetNo/                       MET Norway weather provider (client, DTOs, options)
    OpenRouteService/            OpenRouteService routing provider (client, DTOs, options)
frontend/                        React + Leaflet SPA (Vite); src/App.tsx is the entry
tests/Wayther.Tests/             xUnit tests (Domain + Infrastructure)
docs/                            wayther_prd.md + docs/agents/ (skill guides)
Dockerfile, docker-compose.yml   multi-stage build; app + Postgres compose services
```

Request flow: `POST /api/route-forecast` (`Api`) → `RouteForecastService` (`Domain`)
resolves the route via `IRoutingProvider` (OpenRouteService) and attaches nearest-hour
forecasts via the cache-backed `IWeatherProvider` (met.no).

Share flow: `POST /api/shares` stores a route's inputs (waypoints + departure +
interval) under a 6-char base62 slug; opening `/m/{id}` (SPA fallback) has the frontend
`GET /api/shares/{id}`, hydrate the editor, rewrite the URL to `/`, and re-request a
fresh forecast. Nothing about the weather is persisted, so a shared link always shows
the current forecast for those inputs.

## Agent skills

### Issue tracker

Issues are tracked in **GitHub Issues** (`gh` CLI) on `millebesso/wayther`. External PRs are **not** a triage surface. See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one root `CONTEXT.md` + `docs/adr/` (created lazily by `/domain-modeling`). See `docs/agents/domain.md`.

## Logging

Application logging uses the built-in `ILogger` to the console (the default .NET
provider, captured by `docker compose logs`). Usage tracking lives at the API layer,
not in the Domain, which stays logging-free for testability.

- Route-forecast requests are logged in `src/Wayther/Api/RouteForecastEndpoints.cs`
  under the `Wayther.RouteForecast` category — one `Information` line per request
  (origin/destination coords, departure time, interval, plus outcome: route
  distance/duration and sample count), and an `Error` line with the same context on
  failure.
- Use **structured** message templates (named placeholders like `{OriginLat}`), not
  string interpolation, so fields stay queryable.
- Log levels are configured per category under `Logging:LogLevel` in
  `src/Wayther/appsettings.json`.
- Every line is prefixed with a UTC timestamp via `Logging:Console` in the same file
  (`FormatterName: simple` plus `FormatterOptions.TimestampFormat` /
  `UseUtcTimestamp` — the `FormatterName` is required for the options to bind).
