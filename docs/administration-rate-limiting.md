# Administration API rate and concurrency limits

Issue #76 protects the administration API with ASP.NET Core's built-in rate-limiting middleware (`UseRateLimiter`, `System.Threading.RateLimiting`). All throttling lives in the API composition root (`src/TradingEngine.Api`); Domain rules, Application handlers, revision semantics and EF Core optimistic concurrency are unchanged.

## Request flow and middleware ordering

For a request under `/api`:

1. `EasyAuthPrincipalMiddleware` (inside a `UseWhen` branch for `/api`) runs first. When `Authentication:EasyAuth:TrustPlatformHeaders` is `true` it decodes the platform-injected `X-MS-CLIENT-PRINCIPAL` header into `HttpContext.User`, rejecting a missing, malformed or identifier-less principal with 401 **before** any limiter budget is consumed. When the setting is absent or `false` (local development) it does nothing and `HttpContext.User` stays empty.
2. `RateLimitingMiddleware` (`app.UseRateLimiter()`) runs after the Easy Auth branch, so the caller partition always reflects the trusted principal — never a client-supplied header.
3. The endpoint handler and its store run only when every limiter in the chain acquired a permit. A rejected request never reaches the handler or the database.

`/health/database` keeps its own earlier branch: `DatabaseProbeKeyMiddleware` returns 404 for a missing or wrong key before the limiter is evaluated, so key-guessing traffic cannot consume probe budget.

## Limiter structure

`ApiRateLimiterOptionsSetup` builds `RateLimiterOptions.GlobalLimiter` as a chain of two partitions:

- **Per-caller rate limit** — fixed-window limiter partitioned by `(caller, read|write)`. `GET`/`HEAD` requests draw from the read budget; every other method draws from the stricter write budget. The caller key is `EasyAuthClientPrincipal.StableIdentifier` (object identifier / subject claim) when the Easy Auth middleware populated `HttpContext.User`; otherwise a single shared `"unverified"` partition applies. That fallback is the explicit bounded local policy: locally every caller shares one budget, and arbitrary identity headers cannot mint a partition or bypass the limit. Because the fallback is a single bounded partition rather than an exemption, a missing or untrusted identity in a deployed instance can never obtain an unlimited allowance.
- **API-instance concurrency ceiling** — one `ConcurrencyLimiter` shared by all requests that use the global limiter, bounding simultaneous handler/database work regardless of how many distinct callers arrive.

The global limiter is the default for every endpoint that does not opt out, so new administration endpoints are protected automatically. Waiting queues default to disabled (`QueueLimit = 0`) and are bounded to 16 when enabled. Concurrency permits are held only while the request executes and are released on success, rejection, handler exception and cancellation.

## Endpoint coverage

| Endpoint | Policy | Notes |
| --- | --- | --- |
| `GET /health` | Opted out (`DisableRateLimiting`) | Liveness probe; stays reliable under any load and never shares an administration caller's budget. Never touches SQL. |
| `GET /version` | Opted out | Deployment verification (`/version` is curl'd by the deploy workflow); anonymous, static content. |
| `GET /auth-check` | Opted out | Easy Auth smoke check; already requires the owner allowlist when deployed. |
| `GET /health/database` | `database-probe` — shared fixed window, `DatabaseProbePermitLimit` per instance | Key check runs first (404 without `X-Database-Probe-Key`); the shared bounded budget keeps readiness traffic from flooding SQL. |
| `POST /api/watched-instruments`, `GET /api/watched-instruments/{id}`, all `/api/watched-instruments/{id}/monitoring-rule*` endpoints | Global limiter — per-caller read/write budget + shared concurrency ceiling | Default protection for current and future administration endpoints. |

## Configuration

Bound from the `RateLimiting` configuration section into `ApiRateLimitOptions` with data-annotation validation at startup (`ValidateOnStart`); invalid values — non-positive limits, a queue over 16, or `WritePermitLimit > ReadPermitLimit` — fail host startup.

| Setting | Default | Meaning |
| --- | --- | --- |
| `RateLimiting:WindowSeconds` | `60` | Fixed-window length for the per-caller budgets and the probe policy. |
| `RateLimiting:ReadPermitLimit` | `120` | `GET`/`HEAD` requests per caller per window. |
| `RateLimiting:WritePermitLimit` | `30` | Other methods per caller per window; must not exceed `ReadPermitLimit`. |
| `RateLimiting:ConcurrencyPermitLimit` | `8` | Simultaneous in-flight administration requests per API instance. |
| `RateLimiting:QueueLimit` | `0` | Queued (waiting) requests per limiter; `0` disables queueing, max `16`. |
| `RateLimiting:DatabaseProbePermitLimit` | `10` | Shared probe requests per instance per window. |

The `appsettings.json` values are safe synthetic defaults. Deployed tuning is a per-environment app-setting change (`RateLimiting__*`) informed by measured legitimate traffic; the defaults are deliberately conservative for the single-owner development API.

## Rejection responses

Excess requests receive HTTP 429 with a Problem Details body (`code = rate_limit.exceeded`) before any handler or store executes. `Retry-After` is included only when the rejecting limiter reports an estimated retry interval — the fixed-window budgets do; the concurrency limiter does not, so concurrency rejections carry no `Retry-After`.

## Telemetry

`RateLimitingTelemetry` increments a `tradingengine.api.rate_limiter.requests` counter on the `TradingEngine.Api.RateLimiting` meter for every limiter decision, tagged only with `policy` (`admin-read`, `admin-write`, `api-concurrency`, `database-probe`) and `outcome` (`accepted`/`rejected`). No caller identity, token, payload or query string is recorded. The meter is registered on the OpenTelemetry pipeline whenever `APPLICATIONINSIGHTS_CONNECTION_STRING` is configured, so counts appear in Application Insights `customMetrics`:

```kusto
customMetrics
| where name == "tradingengine.api.rate_limiter.requests"
| summarize sum(value) by tostring(customDimensions["policy"]), tostring(customDimensions["outcome"])
| order by customDimensions["policy"]
```

Locally (no connection string) the counter is inert; rejected requests still return 429 and appear as `resultCode = 429` request telemetry when export is configured.

## Limitations

- **Per-instance state.** The limiter state and concurrency budget exist inside one API process. They reset on restart and are not shared between instances; with N instances the effective ceilings are N times the configured per-caller and concurrency values. A deployment-wide quota would need shared or edge enforcement (e.g. API Management/Front Door) — out of scope for #76 and not claimed here.
- Rate limiting is an additional resource-protection control; it does not replace Easy Auth, the owner allowlist or network-level DDoS protection.

## Local verification (bounded, Rider-friendly)

The integration suite `AdministrationRateLimitingTests` (in `tests/TradingEngine.Api.IntegrationTests/RateLimiting`) covers read/write budgets, caller isolation, burst rejection and recovery, the shared concurrency ceiling, permit release on success/exception/cancellation, probe behaviour and configuration validation. Run it with:

```
dotnet test tests/TradingEngine.Api.IntegrationTests --filter FullyQualifiedName~RateLimiting
```

Manual smoke against a locally running API (`dotnet run --project src/TradingEngine.Api`, synthetic connection string or user secrets per `docs/rider-local-azure-dev.md`):

1. Set a small budget via environment variables on the run configuration, e.g. `RateLimiting__ReadPermitLimit=3`, `RateLimiting__WindowSeconds=60`.
2. `GET http://localhost:5068/api/watched-instruments/{guid}` four times — expect three non-429 responses then `429` with `code = rate_limit.exceeded` and a `Retry-After` header. Unknown instrument ids still consume budget; a 404 counts as an accepted read.
3. `GET /health`, `/version`, `/auth-check` — always available regardless of the exhausted budget.
4. `GET /health/database` without `X-Database-Probe-Key` — still 404.

Do not run burst or concurrency tests against the Azure Dev database profile — the local walkthrough stays within the default budgets, and heavy verification belongs to the isolated in-memory test hosts.
