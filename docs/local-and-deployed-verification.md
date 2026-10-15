# Local and deployed verification

Concise checks for the administration UI, the session endpoint and the
deployment guards. Run local checks after `dotnet run
--project src/TradingEngine.Api`; run deployed checks against the Web App
hostname after **both** the infrastructure and application workflows have
completed for the commit under test (see
[Observability](observability.md#post-deployment-verification-checklist)).
Never commit real hostnames, account details or raw log output — substitute
the `<placeholders>` shown.

## Session presentation

The header renders one shared `AuthStatus` component backed by
`GET /api/session`, not a static link. Expected states:

| Context | `/api/session` result | Header shows |
| --- | --- | --- |
| Local run (Easy Auth absent) | `200 {"mode":"development"}` | `Local development` |
| Deployed, allowlisted session | `200 {"mode":"entra","displayName":"…"}` | `Signed in as …` + `Sign out` |
| Deployed, expired/unsigned session | `401` | `Sign in` linking to `/.auth/login/aad?post_login_redirect_uri=<current same-origin path>` |
| Deployed, signed-in but not allowlisted | `403` | `Access denied` + `Sign out` |
| Endpoint unreachable or malformed | network/5xx/parse failure | `Session check unavailable` + `Retry` |

The session response deliberately carries only `mode` and `displayName` —
no tokens, no claim list, no object identifier.

Sign-in links go directly to `/.auth/login/aad` with a same-origin
`post_login_redirect_uri` (the client rejects anything that is not a
site-relative `/path`). Sign-out goes to
`/.auth/logout?post_logout_redirect_uri=/signin.html`.

## Local verification (development mode)

1. `GET http://localhost:5068/health` and `/version` return 200.
2. Open `http://localhost:5068/` — the catalogue loads **without** any
   Microsoft sign-in and the header shows `Local development`. No `Sign in`
   link is offered in development mode.
3. `GET http://localhost:5068/api/session` returns
   `{"mode":"development","displayName":null}`.
4. Exercise a catalogue load and a synthetic settings save (`If-Match`
   header as usual). Unsaved edits on the editors and the settings tab
   prompt a browser confirmation before any full-page navigation.
5. `/signin.html` remains reachable and renders the provider link, but
   `/.auth/login/aad` locally returns 404 (the platform route does not
   exist) — expected; sign-in is a deployed-only flow.

The local API talks to whatever `ConnectionStrings:TradingEngine` points
at — **the same shared Azure Dev database the deployed app uses** unless
you deliberately configured otherwise. It is not automatically a separate
database: check the actual target before running migrations or writing
fixtures, and prefer synthetic `XTEST` instruments so cleanup stays scoped
(`tools/Remove-TradingEngineTestRun.ps1`, see
[rider-local-azure-dev.md](rider-local-azure-dev.md)).

### Optional isolated developer/test database

If you want write-safe isolation, point `ConnectionStrings:TradingEngine`
at a database you own (local SQL Server, a disposable Testcontainers
instance or a second Azure database), apply the EF migrations there once
with `dotnet ef database update` or the migration bundle, and keep the
shared Azure Dev database for read-oriented runs. This is optional — no
setup is required just to read the app locally. Automated SQL tests do not
touch shared databases at all: `TradingEngine.Infrastructure.IntegrationTests`
runs against disposable Testcontainers SQL Server containers.

## Deployed verification (owner session)

In a private browser window:

1. `GET https://<host>/api/session` anonymously → `401` (Easy Auth
   `unauthenticatedClientAction: Return401`).
2. Open `https://<host>/` → Easy Auth redirects or returns 401; sign in via
   `/signin.html` → `/.auth/login/aad` as an allowlisted owner → the
   catalogue loads and the header shows `Signed in as …` with `Sign out`.
3. Search and page the catalogue; navigate into an instrument and back —
   filters and page state are retained via the query string.
4. Change a settings value on the Settings tab and save → success notice;
   refresh the page → the change persists and the header still shows the
   signed-in label. A personal Microsoft account that is already in the
   allowlist uses the same Entra sign-in path — no second account system
   exists or is added.
5. Failure/retry: stop the Web App briefly (or throttle via DevTools) and
   reload — the page shows immediate `Loading instruments…`, a
   still-working message after ~2 s, then a bounded failure with `Retry`.
   Previously loaded data stays visible during a refresh instead of
   flashing an empty state.
6. Simulate expiry (revoke the session in a second window, then act) — a
   `401` surfaces `Session expired` with a `Sign in again` link that
   returns to the same page; a `403` surfaces `Access denied`. Failed
   writes are never replayed automatically after re-authentication; dirty
   forms warn before the full-page sign-in navigation discards them.

### Anonymous/protected endpoint sweep

| Request | Expected |
| --- | --- |
| `GET /health` anonymous | `200 {"status":"Healthy"}` |
| `GET /version` anonymous | `200` with the deployed commit SHA |
| `GET /health/database` without `X-Database-Probe-Key` | `404` |
| `GET /api/watched-instruments` anonymous | `401` |
| `GET /` or `/_framework/…` anonymous | `401` (platform, before the app) |
| `GET /signin.html`, `/css/app.css`, `/favicon.ico` anonymous | `200` |
| `POST /api/…` anonymous | `401` |

## Investigating a reported HTTP 500

Every failure response produced by the app carries a `traceId` extension
in its problem JSON, including the generic `server.internal_error` body
written by the exception handler. When a W3C `Activity` is in flight the
value is its full traceparent (`00-<trace-id>-<span-id>-01`); otherwise it
falls back to `HttpContext.TraceIdentifier`, an opaque per-request ID that
is only searchable in app request logs — not in Application Insights.
Responses produced before the app runs (Easy Auth `401`s, platform-level
`403`s, container/startup failures) are platform responses with no app
`traceId` at all.

To correlate a W3C `traceId`, use the **second** segment — the trace id —
which equals Application Insights `operation_Id`:

```kusto
union requests, dependencies, exceptions
| where operation_Id == "<second segment of 00-<trace-id>-<span-id>-01>"
| order by timestamp asc
```

Then classify what you find, carefully:

- A `dependencies` SQL row that failed or timed out is evidence for SQL
  connectivity or resume-from-pause — nothing weaker than that row
  proves it.
- An `exceptions` row gives the sanitized type
  (`customDimensions["exception.original_type"]`).
- A missing SQL dependency together with a schema error suggests a
  pending migration.
- No application rows at all means the request never reached the app
  (platform/startup — check container logs via `az webapp log tail`);
  absence of telemetry cannot prove whether the request arrived, so do
  not treat it as evidence for any specific cause.

The reported initial-delayed-500 observation remains **unresolved**
unless logs for that window show the failing request and dependency. A
later success, a successful SQL call, or a missing telemetry row each
prove nothing about the original cause on their own. Record the incident
as `unresolved` when no operation rows exist for the failing window.

## Diagnosing a stale `/version`

The deploy workflow asserts both `commit` and `version` equal the pushed
SHA and deliberately fails rather than retrying forever. If it keeps
seeing the previous SHA:

1. Confirm `Deploy to App Service` actually ran for the intended run —
   `az webapp log deployment show`, Deployment Center → Logs.
2. Check whether the new build is failing to start and the platform kept
   the old site contents: `az webapp log tail` / Log stream for startup
   errors (missing connection string, missing startup DLL). The explicit
   `appCommandLine` (`dotnet TradingEngine.Api.dll`) in
   `infra/modules/app-service.bicep` pins the entry point; inspect the
   actual publish output (`dotnet publish … -o ./artifacts/api`) if the
   name is ever in doubt.
3. Confirm the App Service restarted after the zip deploy
   (`alwaysOn: true`, `/health` health check) — a zip deployed to a
   running site that never restarted serves the old build.

Do not relax the assertion or extend retries to mask an old deployment.

## SQL retry policy note

`EnableRetryOnFailure` is **not** enabled on the EF Core SQL Server
provider. `SqlServerMonitoringRuleStore.SaveAsync` runs an explicit
multi-step transaction (`BeginTransactionAsync` + two `SaveChangesAsync`
passes + `CommitAsync`); enabling the retrying execution strategy would
either silently not retry those operations or require wrapping them in
`CreateExecutionStrategy().ExecuteAsync` with verified ambiguous-commit
behaviour — that trade-off needs its own reviewed change and transaction
tests. Transient-failure handling stays at the HTTP boundary: the client
retries idempotent `GET`s at most twice (≈0.4 s/1.2 s backoff, capped at
5 s and honouring `Retry-After`) inside a 45 s overall bound, and never
retries writes or 400/401/403/409/412/428 responses.

## Watched-instrument logical deletion rollout

`20261014224418_WatchedInstrumentLogicalDeletion` adds a nullable
`DeletedAt` column to `WatchedInstruments` and replaces the unconditional
unique index `UX_WatchedInstruments_Exchange_Symbol_QuoteCurrency` with a
filtered unique index (`[DeletedAt] IS NULL`). It is non-destructive:
existing rows and revision history are untouched.

Sequencing:

1. Apply the migration to the target database **before** deploying the
   build that reads/writes `DeletedAt`
   (`dotnet ef database update` or the deployment migration step). The
   old build is unaffected by the extra nullable column, so schema-first
   is safe.
2. Deploy the new build.
3. Verify: `GET /api/watched-instruments` summaries now include
   `concurrencyToken`; `POST /api/watched-instruments/bulk-delete`
   accepts `{"items":[{"instrumentId":"…","concurrencyToken":"…"}]}`.

Rollback limitations — a code-only rollback to a **pre-deletion build is
not safe**: the old build ignores `DeletedAt`, so logically deleted
instruments reappear in the catalogue, resume monitoring eligibility and
accept edits, and the unconditional-read paths treat them as active.
Rolling back the schema instead (dropping `DeletedAt`) **erases the
deletion state** and recreates the unconditional unique index, which
**fails** if any deleted instrument shares a business key with an active
re-registration. Never purge deleted rows or their revision history as an
automatic rollback action — that history is the audit trail.

Preferred recovery: roll **forward** with a fix, or deploy a
deletion-aware build. If instruments were already re-exposed by a
code-only rollback, stop monitoring operations first and follow an
approved recovery plan before any data repair. The upgrade sequence is
always schema-first, then code; the reverse order is never safe.

Manual acceptance (synthetic records only, disposable/local database):

1. Register two synthetic instruments (e.g. `XTEST`/`AAA`, `XTEST`/`BBB`).
2. Open the catalogue: each row shows a leading checkbox; the header
   checkbox shows the indeterminate state when only some rows are ticked
   and reports `N selected on this page`.
3. Tick both rows → `Delete selected` → confirm. Both disappear from
   the list and `GET /api/watched-instruments/{id}` for each returns
   404 while their detail history rows remain in the database.
4. Re-register `XTEST`/`AAA` → succeeds with a new id.
5. POST `bulk-delete` with the **old** id and its stale token → returns
   `alreadyDeletedIds` (safe no-op); the new active row is unaffected.
6. POST `bulk-delete` with one valid item plus one unknown id → 404 with
   `instrumentIds`, and the valid item is **not** deleted (all-or-none).
7. Change the search text or page while items are ticked → selection
   clears and no hidden ids are submitted.
