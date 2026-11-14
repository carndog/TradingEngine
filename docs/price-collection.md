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
| `provider-budget-clamped` | Effective interval = `max(SamplingIntervalSeconds, ceil(monitoredCount × windowSeconds / providerRateLimit))`. With the eToro defaults (120 requests / 60 s) the floor is `ceil(count / 2)` seconds — identical to `EToroRateLimitPolicies.MinimumSamplingIntervalSeconds`. The provider's shared rolling-window rate gate additionally paces calls inside the process and honours provider-directed cooldowns — a 429 carrying `Retry-After`/`RateLimit-Reset` registers a cooldown until the directed instant, so every subsequent call on the shared gate declines locally (zero HTTP attempts) until it expires or the remaining budget cannot outlast it. The clamp is therefore a conservative steady-state bound, not a quota guarantee. |

Existing instruments keep working under either policy: `SamplingIntervalSeconds` (1–3600, stored on the instrument) remains the demand input; the policy only derives the effective interval used for slot flooring.

### Exchange-aware session policy

`WeeklyScheduleMarketSessionPolicy` evaluates configured windows in the exchange's own IANA time zone via NodaTime, so opening/closing boundaries and daylight-saving transitions are handled by tzdb, not by hard-coded clock hours. Windows may span midnight (an overnight window applies its `Days` to the session-start day). `AlwaysOpen` exchanges skip window evaluation entirely. Session lookup is case-insensitive on the instrument's `Exchange`.

Session outcomes:

| Status | Collection outcome |
| --- | --- |
| `Open` | Provider call proceeds |
| `Closed` | `SkippedMarketClosed` — a recorded skip, not a failure |
| `Unconfigured` | `Failed` with `price_collection.exchange_session_unconfigured` — a configuration problem, distinct from market-closed |

### Per-listing native quote-unit configuration

A monitored instrument collects prices in the listing's *native* quote unit — the unit the provider actually quotes in (for example `GBX` for pence-priced listings, distinct from any display preference). The authoritative source is `INativeQuoteUnitMap`, resolved per `(Exchange, Symbol)` from two inputs in a fixed order:

1. `ListingConfigurations` — a persisted per-listing row confirmed by an administrator (or by provider metadata where a provider supplies it) through `PUT /api/watched-instruments/{id}/listing`. It carries the provider identity (`Provider`, `ProviderInstrumentId`, `ProviderSymbol`, `ProviderExchangeName`, `DisplayName`), the confirmed `NativeQuoteCurrency`, a `UnitSource` provenance (`Administrator`/`ProviderMetadata`), and a `rowversion` concurrency token guarded by `If-Match`.
2. `EToro:MarketData:QuoteCurrencyMap` / `QuoteCurrencyMapJson` — the deployment-configured map that predates the persisted model.

Resolution outcomes are deterministic:

| Sources | Result |
| --- | --- |
| Persisted row only, or persisted agreeing with deployment | Confirmed native unit from the persisted row |
| Deployment map only | Deployment-configured unit (`UnitSource` reported as `Deployment`) |
| Persisted row and deployment map disagree | `market_data.quote_currency_source_conflict` — an explicit conflict; nothing is collected or relabelled |
| Neither | `market_data.quote_currency_unconfigured` |

Registration and the `PUT …/listing` confirmation resolve the listing through `IMarketDataProvider.ResolveListingAsync` server-side, distinguishing `resolved`, `not-found`, `ambiguous`, `unsupported-venue` and `unavailable`. An instrument may remain `Configured` in any of these states, but a transition to `Monitored` — at registration or through settings — is rejected unless the session is configured and the native unit resolves unambiguously to the instrument's `QuoteCurrency`.

The collector performs the same composite resolution **before every quote request**: an unconfigured unit, a source conflict, or a unit mismatch records a `Failed` attempt with the `market_data.*` code above rather than collecting under a guessed or mismatched currency. Persisted rows are read from SQL on every resolution, so a newly confirmed listing is visible to the running collector with no redeployment or restart. Once a listing is confirmed, its unit cannot be relabelled — an update requesting a different `NativeQuoteCurrency` for the same `(Exchange, Symbol)` fails with `instrument_listing.unit_conflict`.

Settings responses expose both sections: `listing` (resolution state, provider identity, unit state, deployment presence, concurrency token) and `collection` (`not-monitored`, `collector-disabled`, `unsupported-venue`, `native-unit-unconfirmed`, `native-unit-conflict`, `session-closed`, `awaiting-first-observation`, `collecting`, with last-observation and last-attempt details).

### Outcomes

Every processed instrument produces exactly one `PriceCollectionAttempts` row with an `Outcome` enforced by a SQL check constraint:

| Outcome | Meaning |
| --- | --- |
| `Observed` | Quote returned and persisted as a new `PriceObservations` row |
| `SkippedMarketClosed` | Exchange session closed — no provider call was made |
| `SkippedDuplicate` | Another invocation already persisted this slot (unique index) |
| `Throttled` | Provider returned `market_data.provider_throttled` (rate gate, provider-directed cooldown after a 429, or the 429 response itself) |
| `PriceUnavailable` | Provider returned a quote with neither bid nor ask after the bounded retry |
| `Failed` | Any other failure; `ErrorCode` carries `price_collection.*` or `market_data.*` |

Each attempt also records `WatchedInstrumentId`, `ScheduledFor`, `AttemptedAt`, `RecordedAt`, the run's `InvocationId`, and where applicable `Provider`/`ProviderRequestId`.

Attempts are append-only, but retry semantics are defined **per scheduled slot**: the latest stored attempt per instrument is read once per run, and a repeat poll inside the same slot does not write again when nothing changed. `SkippedMarketClosed` and `exchange_session_unconfigured` record once per slot, `Throttled` and `PriceUnavailable` additionally suppress the provider call entirely inside the same slot (one bounded retry sequence per slot), while `Failed` provider/storage attempts are retried on the next poll and append as they occur. The next eligible slot always collects fresh.

`Failed` `ErrorCode` values:

| Code | Meaning |
| --- | --- |
| `price_collection.exchange_session_unconfigured` | No session entry for the instrument's exchange |
| `price_collection.provider_timeout` | Provider call timed out inside the run budget |
| `price_collection.provider_error` | Provider call threw unexpectedly |
| `price_collection.deadline_exceeded` | Run budget elapsed before or during the instrument's work |
| `price_collection.unsupported_quote` | Quote value cannot be persisted without loss (for example a scale finer than `decimal(38,18)`) |
| `price_collection.storage_error` | The persistence write itself failed |
| `market_data.*` | Provider-reported failure, preserved verbatim |

The observation and its `Observed` attempt are inserted in one transaction: if either write fails, neither survives, so there is never an observation without its audit row or an audit row without its observation. A unique-index collision on `(WatchedInstrumentId, ScheduledFor)` is the expected overlap signal — it rolls back both rows, is recorded separately as `SkippedDuplicate`, and is never reported as a provider or storage failure.

### Bounded execution and cancellation

`MaxExecutionTimeSeconds` is one budget for the whole run: a `CancellationTokenSource` armed on the injected `TimeProvider` is created **before the first SQL read** and the linked token is carried through the instrument read, the dedup lookups, every provider call and the observation write. A stalled read or write cannot outlive the budget — cancellation inside it returns a run with `DeadlineReached` (before the instrument loop) or records `Failed`/`price_collection.deadline_exceeded` for the in-flight instrument and stops. Attempt rows are written on the caller's own token so the audit record still lands after the budget fires. Caller cancellation propagates `OperationCanceledException` unchanged — the host's `stoppingToken` on shutdown cancels promptly.

## Persistence

Migration `20261114133340_ListingConfigurations` adds the listing table below; earlier migration `20261105194347_PriceCollection` added the two collection tables. Both are additive — no changes to existing rows.

`ListingConfigurations` (unique on `Exchange + Symbol`, `rowversion` concurrency token):

| Column | Notes |
| --- | --- |
| `Exchange`, `Symbol` | Logical key matching `WatchedInstruments` normalization (uppercased) |
| `Provider`, `ProviderInstrumentId`, `ProviderSymbol`, `ProviderExchangeName`, `DisplayName` | Provider listing identity resolved at confirmation — the collector never re-searches |
| `NativeQuoteCurrency` | Confirmed native quote unit, 3–10 letters/digits, uppercased |
| `UnitSource` | `Administrator` or `ProviderMetadata`, check-constrained |
| `ConfirmedBy`, `ConfirmedAt`, `LastChangedAt` | Provenance; `LastChangedAt >= ConfirmedAt` check-constrained |

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
| `MaxExecutionTimeSeconds` | `60` | Single run budget armed on `TimeProvider`, spanning reads, provider calls and the observation write. |
| `UnavailableRetryAttempts` | `1` | Extra provider calls within the same run when a quote has neither bid nor ask. `0` records `PriceUnavailable` after the first call; the retry outcome per slot is then suppressed until the next slot. |
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

1. Configure the deployment secrets below (they keep the private settings in place across redeployments).
2. Set `enablePriceCollection = true` in `infra/environments/dev.bicepparam`, or deploy with `--parameters enablePriceCollection=true`. The module emits the `PriceCollection__Enabled=true` app setting.
3. Confirm monitored instruments are `MonitoringState = 'Monitored'` with a `SamplingIntervalSeconds` cadence.

### Deployment-time settings

The `appsettings` deployment **replaces the whole app-settings collection** on every infrastructure deploy — any `EToro__*` or `PriceCollection__ExchangeSessions__*` value set out of band would be removed by the next redeployment. These settings therefore live in GitHub environment secrets on `development` (and `development-infrastructure-preview`), passed as Bicep parameters so they are part of the declared desired state:

| GitHub environment secret | Bicep parameter | Emitted app settings |
| --- | --- | --- |
| `ETORO_MARKET_DATA_API_KEY` | `etoroMarketDataApiKey` (`@secure`) | `EToro__MarketData__ApiKey` |
| `ETORO_MARKET_DATA_USER_KEY` | `etoroMarketDataUserKey` (`@secure`) | `EToro__MarketData__UserKey` |
| `ETORO_MARKET_DATA_EXCHANGE_MAP` | `marketDataExchangeMap` (`@secure` JSON object) | `EToro__MarketData__ExchangeMap__{exchange}__{index}` |
| `ETORO_MARKET_DATA_QUOTE_CURRENCY_MAP` | `marketDataQuoteCurrencyMap` (`@secure` JSON object) | `EToro__MarketData__QuoteCurrencyMapJson` (single setting; value is the JSON object) |
| `PRICE_COLLECTION_EXCHANGE_SESSIONS` | `priceCollectionExchangeSessions` (`@secure` JSON object) | `PriceCollection__ExchangeSessions__{exchange}__…` |

All five parameters are `@secure()` in both `infra/main.bicep` and `infra/modules/app-service.bicep`, so deployment output and what-if previews carry unevaluated `parameters(...)` references rather than resolved keys or values. The what-if job summary additionally prints property *paths only* and redacts any `EToro__MarketData__*`/`PriceCollection__ExchangeSessions__*` path segment; the deploy step runs with `--output none`, so resolved parameter values never reach CI logs.

The map/session secrets hold JSON objects emitted by the template — for example `ETORO_MARKET_DATA_EXCHANGE_MAP={"XNAS":["<provider exchange name>"]}` (string arrays flattened into indexed settings, matching `EToro:MarketData:ExchangeMap`), `ETORO_MARKET_DATA_QUOTE_CURRENCY_MAP={"XNAS":{"MSFT":"USD"}}` (exchange → symbol → native currency, emitted verbatim as the `EToro__MarketData__QuoteCurrencyMapJson` setting *value* and bound by the adapter — not flattened — so symbol keys containing periods survive Linux App Service name mangling; see [eToro market-data adapter](etoro-market-data-adapter.md#quote-currency-verification)), and `PRICE_COLLECTION_EXCHANGE_SESSIONS={"XNAS":{"timeZone":"America/New_York","alwaysOpen":false,"windows":[{"days":["Monday","Tuesday","Wednesday","Thursday","Friday"],"open":"09:30","close":"16:00"}]}}` (flattened). Keys must match the `Exchange` values on `WatchedInstruments`.

#### Quote-currency map rollout

The `QuoteCurrencyMapJson` transport was introduced because dotted symbols cannot ride in setting *names* on Linux. Both the `Deploy development` (app) and `Deploy development infrastructure` workflows start independently on merge — the app deploy can also be held (`DEPLOYMENT_HOLD`) or fail — so the template emits the map in **both** shapes at once:

- `EToro__MarketData__QuoteCurrencyMapJson` — the whole map as one setting *value*, read by builds that bind `QuoteCurrencyMapJson`.
- `EToro__MarketData__QuoteCurrencyMap__{exchange}__{symbol}` — the legacy discrete-key shape, emitted only for symbols **without** `.` (a dotted name would arrive mangled, and could collide with a literal-underscore sibling such as `DEMO_X`).

A build predating `QuoteCurrencyMapJson` therefore keeps every usable discrete key — collection for non-dotted symbols continues unchanged whether the infrastructure deploys first, the app deployment is held, or the app deployment fails outright. A build with `QuoteCurrencyMapJson` reads the JSON value for every symbol, dotted or not; discrete keys still merge on top per symbol. Either order converges — no sequencing step is required. Once the JSON-reading build is verified, the flattened emission can be removed in a follow-up change (older builds would then lose non-dotted keys too, so only do that after rollback is no longer needed).

#### Post-deployment verification (read-only)

After the next eligible session opens, confirm a newly scheduled slot produced both an `Observed` attempt and its matching saved observation — the Prices tab reads `PriceObservations` only. Match the two tables on **both** `WatchedInstrumentId` and `ScheduledFor`: an attempt and a price from different slots do not count as success, and earlier `Failed` retries for the same slot are ignored by the `Outcome` filter.

```sql
DECLARE @since DATETIME2 = '<session-open-utc>';

SELECT w.Symbol, w.Exchange,
       a.ScheduledFor AS MatchedSlotUtc,
       a.AttemptedAt,
       o.RetrievedAt,
       o.Bid,
       o.Ask,
       o.QuoteCurrency
FROM dbo.PriceCollectionAttempts AS a
JOIN dbo.PriceObservations AS o
  ON o.WatchedInstrumentId = a.WatchedInstrumentId
 AND o.ScheduledFor = a.ScheduledFor
JOIN dbo.WatchedInstruments AS w ON w.Id = a.WatchedInstrumentId
WHERE a.Outcome = 'Observed'
  AND a.ScheduledFor >= @since
ORDER BY w.Exchange, w.Symbol, a.ScheduledFor DESC;
```

Each returned `MatchedSlotUtc` is a slot whose collection attempt succeeded *and* saved a price for that same slot; per instrument, at least one row must appear for the affected period and the newest row must be visible under **Prices → Saved prices**. Historical `Failed` rows remain untouched.

**Secure XNAS configuration:** create the five secrets on the GitHub `development` environment (values come from the private deployment context, never committed). `ETORO_MARKET_DATA_API_KEY`/`USER_KEY` are the read-only market-data credentials; `ETORO_MARKET_DATA_EXCHANGE_MAP` maps `XNAS` to the provider exchange name(s) discovered through the search endpoint; `ETORO_MARKET_DATA_QUOTE_CURRENCY_MAP` maps each XNAS symbol to its native listing currency; `PRICE_COLLECTION_EXCHANGE_SESSIONS` carries the XNAS regular session (America/New_York 09:30–16:00 weekdays). Once the secrets exist, every `Deploy development infrastructure` run — including what-if previews — keeps the settings in place; removing a secret removes the setting on the next deploy, by design.

**Apply the migrations** — additive `20261105194347_PriceCollection` and `20261114133340_ListingConfigurations`. Migrations never run at app startup; the runtime identity holds only `db_datareader`/`db_datawriter`. Apply them under the migration identity as documented in the [Azure SQL operations runbook](azure-sql-operations-runbook.md): **Actions → Migrate development database → Run workflow** from `main`, which builds `efbundle` and applies it over `Authentication=Active Directory Default` (OIDC). Locally, `dotnet ef database update --project src/TradingEngine.Infrastructure --startup-project src/TradingEngine.Api` targets whatever `ConnectionStrings__TradingEngine` points to (LocalDB by default). The migration idempotency check means re-running is a no-op once applied.

The `ListingConfigurations` table must exist before the new build serves traffic — the composite quote-unit map, registration, settings and confirmation paths all query it — so the rollout follows the coordinated sequence: set `DEPLOYMENT_HOLD=true` **before** merging, merge and record the merged revision, apply and verify the migration at that revision, then clear `DEPLOYMENT_HOLD` and manually run **Deploy development** for the same recorded revision. Clearing the hold alone does not trigger a deployment — it only stops skipping the job, so the manual `workflow_dispatch` run is the step that ships the build. See the [coordinated cutover procedure](azure-sql-operations-runbook.md#10-coordinated-schemaapplication-cutover) for the full hold/quiesce/verify checklist.

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

## Reading saved observations

`GET /api/watched-instruments/{id}/price-observations?pageSize=25&before=<UTC ISO-8601>` returns the latest saved observation and a newest-first page of persisted observations for one instrument. It reads `PriceObservations` only: it never calls the market-data provider, never triggers collection and never writes. It uses the existing Easy Auth, fetch-site and read rate-limit pipeline.

- `latest` is the single observation with the greatest `ScheduledFor`; a missing bid or ask stays `null` and is never filled from an older row. It is independent of the requested page.
- `items` are ordered by `ScheduledFor` descending. Paging is keyset: pass the response `nextBefore` as `before` to read strictly older rows, so collections that land between requests cannot repeat or skip rows. `nextBefore` is `null` on the final page.
- `pageSize` defaults to 25 at the API (the portal requests 10, 25 or 50) and must be between 1 and 100. `before` must be UTC, ending in `Z` or `+00:00` (the form `nextBefore` is returned in, URL-encoded), and carry at most seven fractional-second digits within the stored range; invalid input returns `400` with `price_observation.page_size_out_of_range` or `price_observation.cursor_invalid`.
- An unknown or deleted instrument returns `404` with `price_observation.instrument_not_found`; an existing instrument with no observations returns `200` with `latest: null` and empty `items`. Configured (paused) instruments keep readable history.
- Bid and ask are JSON numbers with stored precision and `quoteCurrency` is the native unit (for example `GBX`); neither is converted. Missing slots are not interpolated. `timeliness` is `realtime`, `delayed` or `unknown` as reported.

The portal shows this on the instrument detail page under **Prices** as "Saved prices": a compact latest summary (bid and ask, scheduled and retrieved minute, status) tied to the newest observation even while browsing older pages, then a newest-first table with Scheduled (UTC), Bid, Ask and Status columns. **Show details** adds Retrieved (UTC), Provider observed (UTC) and Provider. Pages hold 10, 25 or 50 rows (default 10); changing the size or pressing Refresh returns to the first page. Visible times show only date, hours and minutes; the API values and paging cursors keep full precision.

### Price display settings

`PriceDisplayDecimalPlaces` (0–8, default 2) and `PriceDisplayUnit` (`major` default, or `native`) are per-instrument presentation preferences stored on `WatchedInstruments` and edited through `GET`/`PUT /api/watched-instruments/{id}/settings` with the usual `If-Match` concurrency. They are separate from monitoring-rule `PriceScale` and collection settings and do not create a rule revision. Requests that omit either field keep the stored value. The `PriceDisplayPreferences` migration adds both columns with defaults, so existing instruments show 2 decimal places in the major unit; stored observations are never rewritten.

Formatting rounds with `MidpointRounding.AwayFromZero` to exactly the configured places and adds thousands separators. `major` converts a fixed subunit to its major unit before rounding (GBX ÷ 100 → GBP); `native` shows the stored unit. Other units show as stored. Each row converts from its own stored unit; if that yields a different unit from the instrument heading, a Unit column appears, the row is flagged and the Bid/Ask headings drop their unit. A missing side shows as `—`.

To apply the intended defaults after deployment, open each instrument's **Settings** tab, set **Price decimal places** (for example BTC = 0, XRP = 2) and, for GBX instruments, choose **Display unit**, then **Save settings**.

## Observability and safety

The hosted service logs one `Information` line per run: `InvocationId` plus observed / market-closed / duplicate / throttled / failed counts and `DeadlineReached`. Per-instrument detail lives in `PriceCollectionAttempts`. Logs never carry credentials, auth headers, raw provider payloads or quote values — only counts, codes, request IDs and timestamps.

## Testing

- `tests/TradingEngine.Application.Tests/PriceCollection` drives `CollectDuePricesHandler` with a `FakeClock`, a manual `TimeProvider`, stub provider and capturing store: due vs disabled instruments, both sampling policies, market-closed/unconfigured sessions, throttling and same-slot throttle backoff, both-null quotes retried then `PriceUnavailable`, one-sided quotes, same-slot outcome dedup with next-slot recovery, stalled reads/writes and stalled audit writes bounded by the fake-time budget (deadline item returned, no unbounded SQL write started), storage failures isolated per instrument, cancellation, repeat delivery and overlapping invocations. `WeeklyScheduleMarketSessionPolicyTests` covers open/close boundaries, overnight windows and the spring-forward/fall-back transitions.
- `tests/TradingEngine.Infrastructure.IntegrationTests/Persistence/PriceCollectionPersistenceTests.cs` runs against real SQL Server via Testcontainers: `decimal(38,18)` precision round-trip, one-sided quotes, nullable `ProviderObservedAt`, unique-slot dedup, two successive slots, atomic observation+attempt rollback under a forced audit-write failure, concurrent writers racing the same slot (one observation, one `Observed`, one `SkippedDuplicate`, then the losing context collecting another instrument), quotes with scale finer than 18 rejected without rounding, latest-attempt lookup, check-constraint and FK enforcement, a `SaveChangesAsync` cancelled mid-write leaving no partial rows and a reusable context (the change tracker is cleared so a later save cannot resubmit stale entries), and unique violations on indexes other than the scheduled-slot index surfacing as storage errors rather than `SkippedDuplicate`.
- `tests/TradingEngine.Infrastructure.IntegrationTests/Persistence/PriceObservationHistoryPersistenceTests.cs` covers the saved-observation read against real SQL Server: newest-first ordering with equal prices, one-sided latest observations without backfill, full precision, native units and distinct timestamps, empty, unknown, deleted and configured instruments, isolation across identical slots, bounded keyset pages, insertion between pages without repetition or omission, and no persistence mutation. API and client tests cover validation, `404` vs empty, `401` in trusted-header mode, no provider calls, and UI loading, empty, error/retry, sign-in, paging, refresh and stale-response suppression.

## Out of scope

Strategy evaluation, alerting/notifications, order execution and any write to the provider remain out of scope. The collector only reads `GetCurrentQuoteAsync`; nothing here can submit an order.
