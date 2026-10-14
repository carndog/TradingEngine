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

Every failure response now carries a `traceId` extension in its problem
JSON (including the generic `server.internal_error` body written by the
exception handler). Quote it, then correlate:

1. In Application Insights → Logs, find the request by timestamp and
   `resultCode == 500`; its `operation_Id` equals the `traceId` suffix.

   ```kusto
   union requests, dependencies, exceptions
   | where operation_Id == "<operation-id>"
   | order by timestamp asc
   ```
2. Classify: a `dependencies` SQL call that failed or timed out means SQL
   connectivity/resume; an `exceptions` row gives the sanitized type
   (`customDimensions["exception.original_type"]`); a missing SQL
   dependency with a schema error suggests a pending migration; no
   application rows at all means the request never reached the app
   (platform/startup — check container logs via `az webapp log tail`).

The first-call 500 that resolves on retry is consistent with the
serverless database resuming from auto-pause — the resume can exceed the
SQL connect timeout on the very first request. A later success is **not**
proof of a cold start either way; confirm against the dependency row
before treating the cause as resolved. Record `unresolved` when no
operation rows exist for the failing window.

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
