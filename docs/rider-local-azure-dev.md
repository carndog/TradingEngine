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
missing, so configure it once per machine. Either option survives Rider
restarts; neither file is committed.

Option A — user secrets (recommended):

```
cd src/TradingEngine.Api
dotnet user-secrets init          # once per machine/repo
dotnet user-secrets set "ConnectionStrings:TradingEngine" "Server=tcp:<sql-server-name>.database.windows.net,1433;Database=sqldb-tradingengine-dev;Authentication=Active Directory Default;Encrypt=True;"
```

Option B — Rider run configuration: add the environment variable
`ConnectionStrings__TradingEngine` (double underscore) with the same value to
the `http` launch profile in Run > Edit Configurations.

Use the exact connection string format documented in
`docs/azure-sql-development-database.md` — no credentials appear in it.

Start the API (Rider `http` profile or `dotnet run --project
src/TradingEngine.Api`) and check `GET http://localhost:5068/health` and
`/version`. `/health/database` stays anonymous-only behind the probe key and
is a deployed check.

## HTTP walkthrough

- `src/TradingEngine.Api/TradingEngine.Api.http` — local walkthrough, no
  token required. Run "All Requests in File" top to bottom. The registration
  request clears stale variables, generates a fresh `runId`, a unique
  synthetic symbol `RUN-<runId>-DEMO`, and ordered future dates; copy the
  logged `runId` from the response output — the cleanup script needs it.
  Dependent requests abort instead of writing when an earlier step failed.
- `src/TradingEngine.Api/TradingEngine.Api.Deployed.http` — deployed smoke
  checks (health, version, auth-check, database probe, anonymous-vs-owner
  registration). Values live in `http-client.private.env.json` (gitignored);
  see `docs/easy-auth-entra-id.md` for token acquisition. Deployed auth is a
  separate concern from the local SQL connection above.

Synthetic instruments are registered `configured` and never monitored, so no
trading-path behaviour is exercised.

## Cleanup

Each run leaves its instrument, monitoring rule and revision rows in Azure
Dev — including interrupted runs where registration succeeded before a later
request failed. `tools/Remove-TradingEngineTestRun.ps1` removes exactly one
run's rows; it validates the Azure Dev server/database names, requires the
explicit `-RunId`, matches only `RUN-<runId>-%` symbols on exchange `XTEST`
that are still `Configured`, previews the selection, and only `-Apply`
deletes inside a transaction in foreign-key order
(MonitoringRuleRevisions -> MonitoringRules -> ChartAnalysisDefinitions ->
WatchedInstruments). Repeating it is a safe no-op. Requires `sqlcmd`
(go-sqlcmd) on PATH and the same `az login` identity.

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
