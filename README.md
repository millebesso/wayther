# wayther

An app for weather forecasts along a predefined route.

## Stack

- **Backend:** ASP.NET (.NET 10), EF Core + Npgsql. Single project under `src/Wayther`
  with `Domain/`, `Infrastructure/`, and `Api/` folders.
- **Frontend:** React + Leaflet (Vite) under `frontend/`.
- **Database:** PostgreSQL — used only as a forecast cache.
- Fully containerized: the dotnet process serves both the API and the built
  frontend from a single origin.

## Running (Docker)

```bash
cp .env.example .env      # adjust credentials if you like
docker compose up --build
```

Then open http://localhost:8080 — the Leaflet map is served by the backend, and
`GET /api/health` returns `{ "status": "ok" }`. Postgres self-provisions its
schema on first boot via EF Core migrations applied at startup; its data persists
in the `wayther-db` volume across `docker compose down`/`up`.

## Local development (hot reload)

Run the backend and the Vite dev server separately:

```bash
# backend (needs a reachable Postgres; see ConnectionStrings:Postgres)
dotnet run --project src/Wayther

# frontend with HMR — proxies /api to the backend on :5283
cd frontend && npm run dev
```

## Database migrations

Migrations live in `src/Wayther/Infrastructure/Migrations` and are applied
automatically on startup. To add one after a model change:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/Wayther --output-dir Infrastructure/Migrations
```
