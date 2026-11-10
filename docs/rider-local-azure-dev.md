# Local API in Rider against Azure Dev

Run and debug `TradingEngine.Api` locally in Rider while it uses the existing
Azure Dev database. Authentication to Azure SQL is passwordless: the API
connects as your own developer identity via `Active Directory Default`, the
same mechanism the deployed Web App uses with its managed identity
(`docs/azure-sql-development-database.md`).

## Prerequisites

- `az login` as the Entra identity that owns the database session. The
  `Microsoft.Data.SqlClient` provider picks up the Azure CLI token for
  `Authentication=Active Directory Default`; an Azure PowerShell or Visual
  Studio login also works.
- A contained database user for that identity in `sqldb-tradingengine-dev`
  with `db_datareader` + `db_datawriter`. Granting access is a database-admin
  task (contained-user bootstrap in
  `docs/azure-sql-operations-runbook.md`); `db_owner`/`db_ddladmin` are
  intentionally not granted — the app never runs migrations at startup and
  schema changes are applied through the controlled migration path.
- Your client IP allowed through the logical server firewall. The dev server
  already permits development network access; if connections are rejected,
  add your IP under the server's Networking blade (authentication stays the
  real boundary).

## Private connection configuration

The API fails fast at startup when `ConnectionStrings:TradingEngine` is
missing. Neither option below writes to a committed file.

Option A — user secrets (recommended, and the option to use for Rider F5):
configure it once per machine; the value is stored outside the repository,
survives Rider restarts and is picked up by the unchanged `http` launch
profile.

```
cd src/TradingEngine.Api
dotnet user-secrets init          # once per machine/repo
dotnet user-secrets set "ConnectionStrings:TradingEngine" "Server=tcp:<sql-server-name>.database.windows.net,1433;Database=sqldb-tradingengine-dev;Authentication=Active Directory Default;Encrypt=True;"
```

Option B — session-scoped environment variable: Rider shows the `http`
launch profile's environment variables as read-only, so instead set
`ConnectionStrings__TradingEngine` (double underscore) in a PowerShell session
and run the API from the repository root in that same shell:

```powershell
$env:ConnectionStrings__TradingEngine = "Server=tcp:<sql-server-name>.database.windows.net,1433;Database=sqldb-tradingengine-dev;Authentication=Active Directory Default;Encrypt=True;"
dotnet run --project src/TradingEngine.Api --launch-profile http
```

The variable lasts only until that PowerShell window closes; it is not saved
and does not affect Rider F5. While it is set it overrides a user secret of
the same name.

Use the exact connection string format documented in
`docs/azure-sql-development-database.md` — no credentials appear in it.

Start the API (Rider `http` profile or `dotnet run --project
src/TradingEngine.Api`) and check `GET http://localhost:5068/health` and
`/version`. `/health/database` stays anonymous-only behind the probe key and
is a deployed check. Note the local run shares the same Azure Dev database
as the deployed app by default — verify the actual target before any
migration or fixture write; a local/disposable database is an optional
alternative, never a prerequisite. Full local and deployed verification
checklists live in
[docs/local-and-deployed-verification.md](local-and-deployed-verification.md).

## Local telemetry in Grafana

To see a local request and its SQL dependency spans before deploying, run the
`grafana/otel-lgtm` container and set `OTEL_EXPORTER_OTLP_ENDPOINT` and
`OTEL_EXPORTER_OTLP_PROTOCOL` as user secrets (the `http` launch profile's
variables are read-only in Rider); the Docker commands, user-secrets commands
and a read-only Prices walkthrough are in
[Local Grafana from Rider](observability.md#local-grafana-from-rider).

## HTTP walkthrough

- `src/TradingEngine.Api/TradingEngine.Api.http` — local walkthrough, no
  token required. Run "All Requests in File" top to bottom. The registration
  request clears stale variables, generates a fresh `runId`, a unique
  synthetic symbol `RUN-<runId>-DEMO`, and ordered future dates; copy the
  logged `runId` from the response output — the cleanup script needs it.
  Dependent requests abort instead of writing when an earlier step failed.
- `src/TradingEngine.Api/TradingEngine.Api.Deployed.http` — deployed checks
  covering health, version, auth-check, the database probe,
  anonymous-vs-owner registration and the full monitoring-rule lifecycle
  with bearer authentication. Values live in `http-client.private.env.json`
  (gitignored); see `docs/easy-auth-entra-id.md` for token acquisition. Set
  `principalObjectId` there to assert `createdBy` equals the owner's Entra
  object id; without it the file only asserts writes are attributed to a
  real caller rather than the local fallback. Deployed auth is a separate
  concern from the local SQL connection above.

Synthetic instruments are registered `configured` and never monitored, so no
trading-path behaviour is exercised.

## Cleanup

Each run leaves its instrument, monitoring rule and revision rows in Azure
Dev — including interrupted runs where registration succeeded before a later
request failed. `tools/Remove-TradingEngineTestRun.ps1` removes exactly one
run's rows. The approved Azure Dev target lives in private configuration:
copy `tools/azure-dev-target.example.psd1` to
`tools/azure-dev-target.local.psd1` (gitignored) and fill in the real server
FQDN and database name. `-Server`/`-Database` must match it exactly — a
similarly named server or database is rejected before `sqlcmd` runs — and
the SQL script re-verifies the connected server (`@@SERVERNAME`) and
database (`DB_NAME()`) before touching rows. It requires the explicit
`-RunId`, matches only `RUN-<runId>-%` symbols on exchange `XTEST` that are
still `Configured`, previews the selection, and only `-Apply` deletes inside
a transaction in foreign-key order (MonitoringRuleRevisions ->
MonitoringRules -> ChartAnalysisDefinitions -> WatchedInstruments).
Repeating it is a safe no-op. Requires `sqlcmd` (go-sqlcmd) on PATH and the
same `az login` identity.

Preview:

```
./tools/Remove-TradingEngineTestRun.ps1 -Server <sql-server-name>.database.windows.net -Database sqldb-tradingengine-dev -RunId <runId>
```

Delete:

```
./tools/Remove-TradingEngineTestRun.ps1 -Server <sql-server-name>.database.windows.net -Database sqldb-tradingengine-dev -RunId <runId> -Apply
```

Lost the `runId`? Find candidate runs directly:

```sql
SELECT Symbol, CreatedAt FROM WatchedInstruments
WHERE Exchange = 'XTEST' AND Symbol LIKE 'RUN-%' ORDER BY CreatedAt;
```

The run id is the `RUN-<runId>-<suffix>` middle segment. Cleanup is
deliberately a separate step: if it fails, fix the script/connection rather
than the walkthrough — cleanup errors must not conceal a test failure, and a
failed run can be cleaned once the issue is resolved.
