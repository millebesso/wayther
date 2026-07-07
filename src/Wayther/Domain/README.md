# Domain

Pure, dependency-free domain logic — the primary test seam.

Slice 0 leaves this empty by design. Later slices add the `RouteForecastService`
orchestrator and its supporting logic (position-at-time interpolation, sample-set
generation, nearest-hour forecast selection, timeline assembly), depending only on
the `IRoutingProvider` / `IWeatherProvider` interfaces so it can be unit-tested
with faked providers and no network or database.
