# Timer-triggered price collection

**Status:** Implemented, read-only.
**Scope:** Periodically collects current quotes for monitored instruments through the registered `IMarketDataProvider` and persists provider-native observations with full provenance. No strategy evaluation, no notifications, no order placement — collection only.

## Architecture

The timer runs **inside the existing `TradingEngine.Api` host** on Azure App Service. There is no Azure Functions project; `PriceCollectionHostedService` is a `BackgroundService` started by the same process that serves the REST API (`dotnet TradingEngine.Api.dll`).

```
PeriodicTimer (PriceCollectionHostedService, thin entry point)
  └── per tick: new DI scope
        └── CollectDuePricesHandler (TradingEngine.Application.PriceCollection)
              ├── IMonitoredInstrumentSource   → SqlServerMonitoredInstrumentSource
              ├── IPriceObservationStore       → SqlServerPriceObservationStore
              ├── IMarketSessionPolicy         → WeeklyScheduleMarketSessionPolicy
              ├── ISamplingPolicy              → resolved from PriceCollection:SamplingPolicy
              ├── IMarketDataProvider          → eToro adapter (rate gate + retries unchanged)
              └── IClock                       → system clock (FakeClock in tests)
```

Every tick obtains the current time through NodaTime `IClock`, lists instruments whose `MonitoringState` is `Monitored` and `DeletedAt` is null, and processes them sequentially inside one bounded run.

### Due-slot computation

For each monitored instrument the handler computes `scheduledFor` by flooring the run's start instant to the instrument's effective interval boundary (`scheduledFor = floor(now, effectiveInterval)`). An instrument is attempted only when no stored observation exists at or after that slot — the `MAX(ScheduledFor)` per instrument is queried once per run from `PriceObservations`.

`ScheduledFor` is the stable identity of a collection attempt. The unique index `UX_PriceObservations_WatchedInstrumentId_ScheduledFor` on `(WatchedInstrumentId, ScheduledFor)` enforces **at most one observation per instrument per slot in SQL**, so a retry or a second overlapping run that reaches the same slot records `SkippedDuplicate` instead of a second row. Two different due slots always produce two observations, even when bid/ask are unchanged.

### Named sampling policies

`PriceCollection:SamplingPolicy` selects one of the supported policies via `SamplingPolicyResolver` (case-insensitive, unknown names fail startup):

| Name | Behaviour |
| --- | --- |
| `per-instrument` (default) | Effective interval = `WatchedInstrument.SamplingIntervalSeconds` unchanged. The cadence configured per instrument is the cadence collected. |
| `provider-budget-clamped` | Effective interval = `max(SamplingIntervalSeconds, ceil(monitoredCount × windowSeconds / providerRateLimit))`. With the eToro defaults (120 requests / 60 s) the floor is `ceil(count / 2)` seconds — identical to `EToroRateLimitPolicies.MinimumSamplingIntervalSeconds`. The provider's shared rolling-window rate gate additionally paces calls inside the process, so the clamp is a conservative steady-state bound, not a quota guarantee. |

Existing instruments keep working under either policy: `SamplingIntervalSeconds` (1–3600, stored on the instrument) remains the demand input; the policy only derives the effective interval used for slot flooring.

### Exchange-aware session policy

`WeeklyScheduleMarketSessionPolicy` evaluates configured windows in the exchange's own IANA time zone via NodaTime, so opening/closing boundaries and daylight-saving transitions are handled by tzdb, not by hard-coded clock hours. Windows may span midnight (an overnight window applies its `Days` to the session-start day). `AlwaysOpen` exchanges skip window evaluation entirely. Session lookup is case-insensitive on the instrument's `Exchange`.

Session outcomes:

| Status | Collection outcome |
| --- | --- |
| `Open` | Provider call proceeds |
| `Closed` | `SkippedMarketClosed` — a recorded skip, not a failure |
| `Unconfigured` | `Failed` with `price_collection.exchange_session_unconfigured` — a configuration problem, distinct from market-closed |

### Outcomes

Every processed instrument produces exactly one `PriceCollectionAttempts` row with an `Outcome` enforced by a SQL check constraint:

| Outcome | Meaning |
| --- | --- |
| `Observed` | Quote returned and persisted as a new `PriceObservations` row |
| `SkippedMarketClosed` | Exchange session closed — no provider call was made |
| `SkippedDuplicate` | Another invocation already persisted this slot (unique index) |
| `Throttled` | Provider returned `market_data.provider_throttled` (rate gate or 429) |
| `Failed` | Any other failure; `ErrorCode` carries `price_collection.*` or `market_data.*` |

Each attempt also records `WatchedInstrumentId`, `ScheduledFor`, `AttemptedAt`, `RecordedAt`, the run's `InvocationId`, and where applicable `Provider`/`ProviderRequestId`. Attempt history is append-only — repeats add new attempt rows; observations stay unique per slot.

### Bounded execution and cancellation

`MaxExecutionTimeSeconds` bounds the whole run: the deadline is checked before each instrument and again after session evaluation, and a linked `CancellationTokenSource.CancelAfter` token is passed to the provider so an in-flight call cannot overrun the bound. Hitting the deadline records `Failed`/`price_collection.deadline_exceeded` for the current instrument and stops the run (`DeadlineReached` on the result). Caller cancellation propagates `OperationCanceledException` unchanged — the host's `stoppingToken` on shutdown cancels promptly.

## Persistence

Migration `20261105194347_PriceCollection` is additive — two new tables, no changes to existing ones.

`PriceObservations` (unique on `WatchedInstrumentId + ScheduledFor`):

| Column | Notes |
| --- | --- |
| `WatchedInstrumentId`, `ScheduledFor` | Attempt identity; FK `Restrict` to `WatchedInstruments` |
| `Provider`, `ProviderInstrumentId`, `ProviderRequestId` | Provenance from `MarketQuote` |
| `Bid`, `Ask` | `decimal(38,18)`, nullable — native precision preserved, one-sided quotes supported |
| `QuoteCurrency` | Native listing unit from the instrument configuration (no FX conversion) |
| `ProviderObservedAt` | Nullable provider `date` — kept separate from `RetrievedAt` |
| `RetrievedAt` | Local `IClock` capture at mapping time (mandatory) |
| `Timeliness` | `Unknown`/`Realtime`/`Delayed`, check-constrained |
| `InvocationId` | Run correlation |

`PriceCollectionAttempts` mirrors the outcome model above. All absolute times are `datetime2(7)`.

## Configuration

Section `PriceCollection` (`PriceCollectionOptions`):

| Key | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `false` | Kill switch. When false the collector registers nothing — no policies, handler or hosted service — and the API behaves exactly as before; collector config is not even validated. |
| `PollIntervalSeconds` | `15` | `PeriodicTimer` cadence between runs. |
| `MaxExecutionTimeSeconds` | `60` | Per-run deadline and linked-token bound. |
| `SamplingPolicy` | `per-instrument` | `per-instrument` or `provider-budget-clamped`. |
| `ProviderRateLimit` | `120` | Requests-per-window used by the clamped policy. Must match the provider group budget (eToro: 120/60 s). |
| `ProviderWindowSeconds` | `60` | Window length used by the clamped policy. |
| `ExchangeSessions` | `{ "XNYS": … }` | Per-exchange session definitions keyed by the instrument's `Exchange` value. |

Session definition shape:

```json
"ExchangeSessions": {
  "XNYS": {
    "TimeZone": "America/New_York",
    "Windows": [
      { "Days": ["Monday","Tuesday","Wednesday","Thursday","Friday"],
        "Open": "09:30", "Close": "16:00" }
    ]
  },
  "ETORO-CRYPTO": { "AlwaysOpen": true }
}
```

- `TimeZone` must be an IANA/tzdb name; invalid values fail startup when the collector is enabled.
- `Open`/`Close` are `HH:mm` local session times; `Close` earlier than `Open` denotes an overnight window. Equal open/close is rejected — use `AlwaysOpen` for continuous markets.
- An instrument whose `Exchange` has no entry fails with `price_collection.exchange_session_unconfigured` (fail closed — nothing is guessed).
- The shipped `XNYS` entry is the generic public NYSE regular-session schedule; add one entry per exchange you monitor. Holidays are not modelled — a session that is open per schedule simply collects.

`PollIntervalSeconds` should be <= the smallest effective interval you care about; shorter polls only make due slots visible sooner because non-due instruments are skipped cheaply.

## Azure development setup

**Host:** the timer runs in the same `TradingEngine.Api` App Service (`app-tradingengine-dev-*`, B1 plan, `DOTNETCORE|10.0`, `alwaysOn` enabled — required for the `BackgroundService` to keep ticking).

**Enable:**

1. Set `enablePriceCollection = true` in `infra/environments/dev.bicepparam`, or deploy with `--parameters enablePriceCollection=true`. The module emits the `PriceCollection__Enabled=true` app setting.
2. Ensure `PriceCollection:ExchangeSessions` covers every monitored instrument's `Exchange`. The defaults in `appsettings.json` ship `XNYS`; extend them through `appsettings.json` or app settings (`PriceCollection__ExchangeSessions__XLON__TimeZone` etc.).
3. Register/keep eToro credentials and `ConnectionStrings__TradingEngine` as documented in [eToro market-data adapter](etoro-market-data-adapter.md) and [Azure SQL development database](azure-sql-development-database.md) — the collector reuses that exact provider registration, rate gate and retry behaviour.
4. Confirm monitored instruments are `MonitoringState = 'Monitored'` with a `SamplingIntervalSeconds` cadence.

**Apply the migration** — additive `20261105194347_PriceCollection`. Migrations never run at app startup; the runtime identity holds only `db_datareader`/`db_datawriter`. Apply it under the migration identity as documented in the [Azure SQL operations runbook](azure-sql-operations-runbook.md): **Actions → Migrate development database → Run workflow** from `main` after merge, which builds `efbundle` and applies it over `Authentication=Active Directory Default` (OIDC). Locally, `dotnet ef database update --project src/TradingEngine.Infrastructure --startup-project src/TradingEngine.Api` targets whatever `ConnectionStrings__TradingEngine` points to (LocalDB by default). The migration idempotency check means re-running is a no-op once applied.

## Verify two successive samples

Read-only query — replace `@instrumentId` with the `WatchedInstruments.Id` of a monitored instrument that has collected at least twice:

```sql
DECLARE @instrumentId UNIQUEIDENTIFIER = '<watched-instrument-id>';

SELECT TOP (2)
    o.WatchedInstrumentId,
    o.ScheduledFor,
    o.Provider,
    o.ProviderInstrumentId,
    o.Bid,
    o.Ask,
    o.QuoteCurrency,
    o.ProviderObservedAt,
    o.RetrievedAt,
    o.Timeliness,
    o.ProviderRequestId,
    o.InvocationId
FROM dbo.PriceObservations AS o
WHERE o.WatchedInstrumentId = @instrumentId
ORDER BY o.ScheduledFor DESC;
```

Consecutive `ScheduledFor` rows differ by the effective sampling interval; identical `Bid`/`Ask` across the two rows is expected when the price is unchanged. Join `PriceCollectionAttempts` on `WatchedInstrumentId` + `ScheduledFor` to audit the attempts behind each observation:

```sql
SELECT a.ScheduledFor, a.Outcome, a.ErrorCode, a.AttemptedAt, a.InvocationId
FROM dbo.PriceCollectionAttempts AS a
WHERE a.WatchedInstrumentId = @instrumentId
ORDER BY a.RecordedAt DESC;
```

## Observability and safety

The hosted service logs one `Information` line per run: `InvocationId` plus observed / market-closed / duplicate / throttled / failed counts and `DeadlineReached`. Per-instrument detail lives in `PriceCollectionAttempts`. Logs never carry credentials, auth headers, raw provider payloads or quote values — only counts, codes, request IDs and timestamps.

## Testing

- `tests/TradingEngine.Application.Tests/PriceCollection` drives `CollectDuePricesHandler` with a `FakeClock`, stub provider and capturing store: due vs disabled instruments, both sampling policies, market-closed/unconfigured sessions, throttling, deadline exhaustion, cancellation, repeat delivery and overlapping invocations. `WeeklyScheduleMarketSessionPolicyTests` covers open/close boundaries, overnight windows and the spring-forward/fall-back transitions.
- `tests/TradingEngine.Infrastructure.IntegrationTests/Persistence/PriceCollectionPersistenceTests.cs` runs against real SQL Server via Testcontainers: `decimal(38,18)` precision round-trip, one-sided quotes, nullable `ProviderObservedAt`, unique-slot dedup, two successive slots, check-constraint and FK enforcement.

## Out of scope

Strategy evaluation, alerting/notifications, order execution and any write to the provider remain out of scope. The collector only reads `GetCurrentQuoteAsync`; nothing here can submit an order.
