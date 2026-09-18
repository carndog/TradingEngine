# Current configuration persistence

The current watched-instrument configuration is stored in SQL Server through EF Core. The adapter implements the application port `IWatchedInstrumentStore` and persists a `WatchedInstrument` together with its current `ChartAnalysisDefinition` atomically: both rows are written in a single `SaveChangesAsync` call, so a failure leaves no partial configuration behind.

## Schema

The model consists of two tables in a required one-to-one relationship that shares the instrument identifier as the primary key.

`WatchedInstruments`:

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | `uniqueidentifier` | Primary key, application-assigned. |
| `Symbol` | `nvarchar(64)` | Required, normalised upper-case symbol. |
| `Exchange` | `nvarchar(20)` | Required. |
| `QuoteCurrency` | `nvarchar(10)` | Required. |
| `MonitoringState` | `nvarchar(16)` | Required; check constraint allows `Configured` or `Monitored`. |
| `SamplingIntervalSeconds` | `int` | Required; check constraint enforces 1-3600. |
| `CreatedAt` | `datetime2(7)` | Required, UTC. |
| `LastChangedAt` | `datetime2(7)` | Required, UTC; check constraint enforces `LastChangedAt >= CreatedAt`. |

A unique index `UX_WatchedInstruments_Exchange_Symbol_QuoteCurrency` on `(Exchange, Symbol, QuoteCurrency)` prevents duplicate instruments.

`ChartAnalysisDefinitions`:

| Column | Type | Notes |
| --- | --- | --- |
| `WatchedInstrumentId` | `uniqueidentifier` | Primary key and foreign key to `WatchedInstruments.Id` (cascade delete). |
| `DefinitionXml` | `xml` | Required; the canonical XML produced by `ChartAnalysisDefinitionXmlSerializer`. |

The shared key means each instrument has exactly one current chart-analysis definition, and the definition row cannot exist without its instrument.

## Timestamps

`NodaTime.Instant` values are mapped to `datetime2(7)` columns through explicit value converters. `Instant.ToDateTimeUtc()` truncates sub-tick nanoseconds to the 100 ns resolution of `datetime2`; reads apply `DateTime.SpecifyKind(..., DateTimeKind.Utc)` before `Instant.FromDateTimeUtc` so round-tripped values remain UTC instants.

## Expected and unexpected failures

The store translates expected outcomes into the `Result` foundation:

- Duplicate `Id` (primary-key violation) returns `watched_instrument.duplicate_id` as a `Conflict` error.
- Duplicate `(Exchange, Symbol, QuoteCurrency)` returns `watched_instrument.duplicate_business_key` as a `Conflict` error.
- A missing configuration returns `watched_instrument.configuration_not_found` as a `NotFound` error.

Unexpected database failures, cancellation and corrupt persisted data (for example an unreadable monitoring state, an instrument that fails domain restoration, or invalid definition XML) are not converted into `Result` values; they surface as exceptions.

## Prerequisites

- .NET 10 SDK (see `global.json`).
- The repository-local EF Core tool manifest. Restore it once from the repository root:

```bash
dotnet tool restore
```

- A reachable SQL Server instance for running migrations or the integration tests. The integration tests use Testcontainers and therefore require a running Docker engine.

## Connection string

The runtime connection string is supplied by the composition root through `AddTradingEngineInfrastructure(connectionString)`. The conventional configuration key is `ConnectionStrings:TradingEngine`, settable through the `ConnectionStrings__TradingEngine` environment variable. The design-time factory `TradingEngineDbContextFactory` reads the same environment variable and falls back to a localdb placeholder so that `dotnet ef` commands work without a live server.

## Migrations

Migrations live in `src/TradingEngine.Infrastructure/Persistence/Migrations` alongside the model snapshot. Common commands, run from the repository root:

```bash
dotnet ef migrations add <Name> --project src/TradingEngine.Infrastructure --startup-project src/TradingEngine.Infrastructure
dotnet ef database update --project src/TradingEngine.Infrastructure --startup-project src/TradingEngine.Infrastructure
dotnet ef migrations bundle --project src/TradingEngine.Infrastructure --startup-project src/TradingEngine.Infrastructure --output efbundle.exe
```

The generated bundle is a self-contained deployment artefact; run it against a target server with `efbundle.exe --connection "<connection string>"`.

## Testing

`tests/TradingEngine.Infrastructure.IntegrationTests` runs the store against a real SQL Server instance in a Testcontainers container. The fixture applies all migrations at startup and then verifies the round-trip, conflict and not-found outcomes, the `xml` column type, canonical XML storage, atomicity of failed writes, cancellation propagation and that unexpected failures throw.

```bash
dotnet test tests/TradingEngine.Infrastructure.IntegrationTests --configuration Release
```

A Docker engine must be running for these tests; they are skipped from environments without one only in the sense that they will fail fast when the container cannot start.
