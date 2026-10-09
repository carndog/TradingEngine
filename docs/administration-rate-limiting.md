# Administration API rate and concurrency limits

Issue #76 protects the administration API with ASP.NET Core's built-in rate-limiting middleware (`AddRateLimiter`/`UseRateLimiter`, `System.Threading.RateLimiting`). All throttling lives in the API composition root (`src/TradingEngine.Api`); Domain rules, Application handlers, revision semantics and EF Core optimistic concurrency are unchanged.

## Request flow and middleware ordering

For a request under `/api`:

1. `EasyAuthPrincipalMiddleware` (inside a `UseWhen` branch for `/api`) runs first. When `Authentication:EasyAuth:TrustPlatformHeaders` is `true` it decodes the platform-injected `X-MS-CLIENT-PRINCIPAL` header into `HttpContext.User`, rejecting a missing, malformed or identifier-less principal with 401 **before** any limiter budget is consumed. When the setting is absent or `false` (local development) it does nothing and `HttpContext.User` stays empty.
2. `RateLimitingMiddleware` (`app.UseRateLimiter()`) runs after both `UseWhen` branches, so the caller partition always reflects the trusted principal — never a client-supplied header.
3. The endpoint handler and its store run only when the limiter granted the request. A rejected request never reaches the handler or the database, and acquired permits are released when the request completes — on success, handler exception or cancellation.

`/health/database` keeps its own earlier branch: `DatabaseProbeKeyMiddleware` returns 404 for a missing or wrong key **before** `UseRateLimiter` runs, so key-guessing traffic consumes neither the probe budget nor a concurrency permit.

## Limiter structure

`ApiRateLimiterOptionsSetup` builds `RateLimiterOptions.GlobalLimiter` as `PartitionedRateLimiter.CreateChained(...)` of exactly two stages, wrapped by `MeteredRequestLimiter` for telemetry:

1. **Shared instance concurrency ceiling** — a constant-key partition (`"all"`) whose `ConcurrencyLimiter` is created per partition by `IConcurrencyLimiterFactory` from the validated options, bounding simultaneous handler/database work across administration and probe traffic. Ownership stays inside the partition: when the runtime evicts an idle partition it disposes that limiter, and the next request recreates a fresh one rather than reusing a disposed singleton.
2. **One fixed-window budget** (`RequestBudgetLimiter`) — partitioned by `(policy, caller)`:

   - `database-probe` — keyed `/health/database` requests share a single instance-wide window; the caller field is empty so all keyed probes draw from `DatabaseProbePermitLimit` together.
   - `admin-read` / `admin-write` — `GET`/`HEAD` requests draw from the read budget; every other method draws from the stricter write budget. The caller key is `EasyAuthClientPrincipal.StableIdentifier` (object identifier / subject claim) when the Easy Auth middleware populated `HttpContext.User`; otherwise a single shared `"unverified"` partition applies. That fallback is the explicit bounded local policy: locally every caller shares one budget, and arbitrary identity headers cannot mint a partition or bypass the limit. Because the fallback is a single bounded partition rather than an exemption, a missing or untrusted identity in a deployed instance can never obtain an unlimited allowance.

### Why the order matters

The middleware calls `AttemptAcquire` and, when that lease is not acquired, retries through `AcquireAsync` so queued limiters can wait. A `FixedWindowRateLimiter` lease does **not** refund its permit when disposed, so the non-refundable budget check must be the *last* stage: if the budget ran first, a downstream concurrency rejection would leave the charged permit orphaned, and the retry would charge the same HTTP request a second permit. With the concurrency check first, a rejection at either stage consumes zero caller budget — concurrency failures never reach the budget, and an exhausted fixed window consumes nothing on a failed attempt. Only a fully admitted request is charged.

The global limiter is the default for every endpoint that does not opt out, so new administration endpoints — and even unmatched `/api` paths — are protected automatically. Waiting queues default to disabled (`QueueLimit = 0`) and are bounded to 16 when enabled; `AcquireAsync` waits honour `RequestAborted`, so a queued request either gains a permit or is cancelled — it is never double-charged.

## Endpoint coverage

| Endpoint | Limiting | Notes |
| --- | --- | --- |
| `GET /health` | Opted out (`DisableRateLimiting`) | Liveness probe; stays reliable under any load and never shares an administration caller's budget. Never touches SQL. |
| `GET /version` | Opted out | Deployment verification (`/version` is curl'd by the deploy workflow); anonymous, static content. |
| `GET /auth-check` | Opted out | Easy Auth smoke check; already requires the owner allowlist when deployed. |
| `GET /health/database` | Global limiter — `database-probe` partition (shared fixed window, `DatabaseProbePermitLimit` per instance) + shared concurrency ceiling | Key check runs first (404 without `X-Database-Probe-Key`); probe traffic never consumes an administration caller's budget. |
| `POST /api/watched-instruments`, `GET /api/watched-instruments/{id}`, all `/api/watched-instruments/{id}/monitoring-rule*` endpoints | Global limiter — per-caller read/write budget + shared concurrency ceiling | Default protection for current and future administration endpoints. |

## Configuration

Bound from the `RateLimiting` configuration section into `ApiRateLimitOptions` with data-annotation validation at startup (`ValidateOnStart`); invalid values — non-positive limits, a queue over 16, or `WritePermitLimit > ReadPermitLimit` — fail host startup.

| Setting | Default | Meaning |
| --- | --- | --- |
| `RateLimiting:WindowSeconds` | `60` | Fixed-window length for the per-caller budgets and the probe budget. |
| `RateLimiting:ReadPermitLimit` | `120` | `GET`/`HEAD` requests per caller per window. |
| `RateLimiting:WritePermitLimit` | `30` | Other methods per caller per window; must not exceed `ReadPermitLimit`. |
| `RateLimiting:ConcurrencyPermitLimit` | `8` | Simultaneous in-flight administration and probe requests per API instance. |
| `RateLimiting:QueueLimit` | `0` | Queued (waiting) requests per limiter; `0` disables queueing, max `16`. |
| `RateLimiting:DatabaseProbePermitLimit` | `10` | Shared probe requests per instance per window. |

The `appsettings.json` values are safe synthetic defaults. Deployed tuning is a per-environment app-setting change (`RateLimiting__*`) informed by measured legitimate traffic; the defaults are deliberately conservative for the single-owner development API.

## Rejection responses

Excess requests receive HTTP 429 with a Problem Details body (`code = rate_limit.exceeded`) before any handler or store executes — written by `RateLimiterOptions.OnRejected`. `Retry-After` is included only when the rejecting limiter reports an estimated retry interval — the fixed-window budgets do; the concurrency limiter does not, so concurrency rejections carry no `Retry-After`.

## Telemetry

`RateLimitingTelemetry` increments a `tradingengine.api.rate_limiter.requests` counter on the `TradingEngine.Api.RateLimiting` meter once per request outcome — never per internal acquisition attempt. Admissions are counted by `MeteredRequestLimiter` when a request is granted (exactly once, whether the first `AttemptAcquire` or the queued `AcquireAsync` succeeds); rejections are counted once in `OnRejected`; queued acquisitions aborted by the caller are counted as `cancelled`. Tags are low-cardinality:

| Tag | Values |
| --- | --- |
| `policy` | `admin-read`, `admin-write`, `database-probe` |
| `outcome` | `accepted`, `rejected`, `cancelled` |
| `limit` | `rate`, `concurrency`, `none` |

`outcome=rejected` carries the `limit` that rejected it (`rate` when the lease reported a `Retry-After`, otherwise `concurrency`); `accepted` and `cancelled` requests report `limit=none`. No caller identity, token, payload, path or query string is recorded. The meter is registered on the OpenTelemetry pipeline whenever `APPLICATIONINSIGHTS_CONNECTION_STRING` is configured, so counts appear in Application Insights `customMetrics`:

```kusto
customMetrics
| where name == "tradingengine.api.rate_limiter.requests"
| extend policy = tostring(customDimensions["policy"]),
         outcome = tostring(customDimensions["outcome"]),
         limit = tostring(customDimensions["limit"])
| summarize total = sum(value) by policy, outcome, limit
| order by policy asc, outcome asc, limit asc
```

Locally (no connection string) the counter is inert; rejected requests still return 429 and appear as `resultCode = 429` request telemetry when export is configured.

## Limitations

- **Per-instance state.** The limiter state and concurrency budget exist inside one API process. They reset on restart and are not shared between instances; with N instances the effective ceilings are N times the configured per-caller and concurrency values. A deployment-wide quota would need shared or edge enforcement (e.g. API Management/Front Door) — out of scope for #76 and not claimed here.
- **Fixed-window burst edge.** A caller can use a full budget at the end of one window and again at the start of the next; the shared concurrency ceiling still bounds simultaneous handler/database work.
- Rate limiting is an additional resource-protection control; it does not replace Easy Auth, the owner allowlist or network-level DDoS protection.

## Local verification (bounded, Rider-friendly)

The integration suite `AdministrationRateLimitingTests` (in `tests/TradingEngine.Api.IntegrationTests/RateLimiting`) covers read/write budgets, caller isolation, burst rejection, deterministic recovery via scripted leases, the shared concurrency ceiling, idle partition eviction and recreation, permit release on success/exception/cancellation, single-count telemetry including queued admission/cancellation, probe budget separation and configuration validation. It runs entirely on in-memory test hosts — no database needed:

```
dotnet test tests/TradingEngine.Api.IntegrationTests --filter FullyQualifiedName~RateLimiting
```

Manual smoke against a locally running API — fully isolated, no Azure Dev database required:

1. Launch the API with a synthetic connection string it can accept but cannot reach, plus a small budget, e.g. on the Rider run configuration:

   ```
   ConnectionStrings__TradingEngine=Server=localhost;Database=TradingEngineRateLimitSmoke;Trusted_Connection=True;Encrypt=False;Connect Timeout=1
   RateLimiting__ReadPermitLimit=3
   RateLimiting__WritePermitLimit=3
   RateLimiting__WindowSeconds=60
   ```

   All four `RateLimiting__*` values together keep `WritePermitLimit <= ReadPermitLimit` — the startup validation rejects a write budget above the read budget. With an unreachable store the administration endpoints respond 500, which is fine for this check: the limiter runs before the handler, so permit accounting and 429s are still observable without touching any real database.

2. `GET http://localhost:5068/api/watched-instruments/{guid}` four times — expect three responses then `429` with `code = rate_limit.exceeded` and a `Retry-After` header. Every response counts as an accepted read regardless of its status.
3. `GET /health`, `/version`, `/auth-check` — always available regardless of the exhausted budget.
4. `GET /health/database` without `X-Database-Probe-Key` — still 404, and consumes no budget.

Do not run burst or concurrency tests against the Azure Dev database profile — the local walkthrough stays within the default budgets, and heavy verification belongs to the isolated in-memory test hosts.
