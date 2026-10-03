# Current configuration persistence

The current watched-instrument configuration is stored in SQL Server through EF Core. Two adapters cover the surface: `SqlServerWatchedInstrumentStore` implements `IWatchedInstrumentStore` for registration and instant-based configuration reads, and `SqlServerMonitoringRuleStore` implements `IMonitoringRuleStore` for the revisioned `MonitoringRule` aggregate.

Registration writes a `WatchedInstrument`, its `MonitoringRule`, and the rule's initial committed revision in a single `SaveChangesAsync` call, so a failure leaves no partial configuration behind. Configuration reads resolve the instrument row plus the monitoring rule and return the definition committed at the caller-supplied instant.

## Schema

The model consists of the watched-instrument table, the monitoring-rule aggregate tables, and the retained legacy definition table.

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

`MonitoringRules`:

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | `uniqueidentifier` | Primary key, application-assigned. |
| `WatchedInstrumentId` | `uniqueidentifier` | Required foreign key to `WatchedInstruments.Id` (restrictive delete: instrument deletion fails while the rule exists); unique index `UX_MonitoringRules_WatchedInstrumentId` enforces at most one rule per instrument. |
| `CreatedAt` | `datetime2(7)` | Required, UTC. |
| `RowVersion` | `rowversion` | Optimistic-concurrency token for the whole aggregate. |

`MonitoringRuleRevisions`:

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | `uniqueidentifier` | Primary key, application-assigned; stable revision identifier. |
| `MonitoringRuleId` | `uniqueidentifier` | Required foreign key to `MonitoringRules.Id` (restrictive delete: rule deletion fails while revisions exist). |
| `RevisionNumber` | `int` | `NULL` for drafts; positive for committed revisions. |
| `CreatedAt` | `datetime2(7)` | Required, UTC. |
| `CreatedBy` | `nvarchar(128)` | Required. |
| `ChangeReason` | `nvarchar(512)` | Optional. |
| `DefinitionXml` | `xml` | Required; the canonical XML produced by `ChartAnalysisDefinitionXmlSerializer`. |
| `EffectiveFrom` | `datetime2(7)` | `NULL` for drafts; required for committed revisions. |
| `EffectiveTo` | `datetime2(7)` | Optional; `NULL` marks the open-ended tail revision. |
| `ProposedFrom` | `datetime2(7)` | Optional draft proposal start. |
| `ProposedTo` | `datetime2(7)` | Optional draft proposal end. |

Check constraint `CK_MonitoringRuleRevisions_Numbering` requires `EffectiveFrom` and `RevisionNumber` to be `NULL` together (drafts) or both set with a positive number (committed). `CK_MonitoringRuleRevisions_Period` requires drafts to have both effective boundaries `NULL` and committed periods to be open-ended (`EffectiveTo` `NULL`) or satisfy `EffectiveTo > EffectiveFrom`, so a draft cannot carry an effective end without a start. Filtered unique indexes `UX_MonitoringRuleRevisions_EffectiveFrom` `(MonitoringRuleId, EffectiveFrom)` and `UX_MonitoringRuleRevisions_RevisionNumber` `(MonitoringRuleId, RevisionNumber)` apply only to committed rows, so drafts never collide with committed identity while committed starts and numbers stay unique per rule.

Committed effective periods form one contiguous chain ordered by start, each revision's `EffectiveTo` equal to the next revision's `EffectiveFrom` and the last revision open-ended; `RevisionNumber` equals the one-based position in that chain. The schema enforces the per-row pieces of that shape; the chain invariants are asserted by the domain restoration validation on every load.

`ChartAnalysisDefinitions` (legacy):

| Column | Type | Notes |
| --- | --- | --- |
| `WatchedInstrumentId` | `uniqueidentifier` | Primary key and foreign key to `WatchedInstruments.Id` (cascade delete). |
| `DefinitionXml` | `xml` | Required; the canonical XML produced by `ChartAnalysisDefinitionXmlSerializer`. |

The legacy single-definition table is retained as a read-only archive. Registrations no longer write it; the revision history is the only authoritative configuration source. The `MonitoringRuleRevisionHistory` migration copies each legacy row into an initial committed revision (see [Migration cutover](#migration-cutover)).

## Timestamps

`NodaTime.Instant` values are mapped to `datetime2(7)` columns through explicit value converters; reads apply `DateTime.SpecifyKind(..., DateTimeKind.Utc)` before `Instant.FromDateTimeUtc` so round-tripped values remain UTC instants.

`Instant` carries nanosecond precision while `datetime2(7)` resolves to 100 ns ticks. Before any monitoring-rule write, `SqlInstant.RequireSupported` verifies that every persisted instant lies inside the `DateTime` range and lands exactly on a 100 ns tick; out-of-range or sub-tick values are rejected with `ArgumentOutOfRangeException` instead of being silently truncated. Effective-period boundaries therefore always round-trip byte-for-byte, and in-memory half-open-interval lookups agree with the stored boundaries.

## Atomic rule saves and concurrency

`IMonitoringRuleStore.GetAsync` returns a `MonitoringRuleSnapshot`: the restored aggregate plus the `MonitoringRules.RowVersion` token captured at load. `SaveAsync` writes the full desired revision state inside one transaction against that token; any conflict or failure aborts the transaction and rolls back every staged change.

The save proceeds in three steps because committed revision numbers and effective starts are unique per rule and rows may move between values in one mutation:

1. Deletes of removed revisions, temporary renumbering of rows whose `RevisionNumber` changes (a +1,000,000 staging offset), and an unconditional `MonitoringRules` update that validates the loaded `rowversion`. A stale token fails here before any revision write.
2. Moved committed starts (`Reschedule`) are applied once the vacating revisions no longer hold their old numbers.
3. Added revisions are inserted and final scalar values (numbers, ends, XML, proposals) applied to surviving rows.

A failed transaction can only leave the untouched pre-save state: deleted rows, temporary numbers and intermediate starts are all rolled back. The store never retries, repairs or rewrites persisted data.

## Expected and unexpected failures

The stores translate expected outcomes into the `Result` foundation:

- Duplicate instrument `Id` returns `watched_instrument.duplicate_id` as a `Conflict` error.
- Duplicate `(Exchange, Symbol, QuoteCurrency)` returns `watched_instrument.duplicate_business_key` as a `Conflict` error.
- A missing instrument configuration returns `watched_instrument.configuration_not_found` as a `NotFound` error.
- A missing monitoring rule returns `monitoring_rule.not_found` as a `NotFound` error.
- Adding a rule for an instrument that already has one returns `monitoring_rule.already_exists` as a `Conflict` error.
- Saving against a stale `rowversion` token, or a concurrent change detected mid-save, returns `monitoring_rule.concurrent_change` as a `Conflict` error.
- A configuration read for which no committed revision applies at the requested instant returns `monitoring_rule.no_applicable_revision` as a `NotFound` error.
- A persisted revision `Id` collision returns `revision.duplicate_id`; a committed effective-start collision returns `revision.start_conflict`; both are `Conflict` errors.

Unexpected database failures, cancellation, rejected timestamp precision or range, and corrupt persisted data (for example unreadable XML, a broken committed sequence, or an instrument that fails domain restoration) are not converted into `Result` values; they surface as exceptions.

## Migration cutover

The `MonitoringRuleRevisionHistory` migration creates both monitoring-rule tables and then backfills existing instruments inside the same migration. It records a single cutover instant (`SYSUTCDATETIME()`) and inserts, per instrument with a legacy definition, one `MonitoringRules` row (keeping the instrument's `CreatedAt`) and one committed revision with `RevisionNumber` 1, `EffectiveFrom` set to the cutover instant, `CreatedBy` set to `migration-20261003150626`, and a change reason describing the cutover.

History therefore begins at the cutover instant: reads at or after it resolve the migrated definition, reads before it return `monitoring_rule.no_applicable_revision`, and no historical applicability is inferred from earlier instrument timestamps. The legacy `ChartAnalysisDefinitions` rows are left untouched as an archive.

The migration's `Down` drops `MonitoringRuleRevisions` and `MonitoringRules`, discarding all revision history; once the feature has been used, a rollback is not lossless and the dropped data cannot be reconstructed from the legacy table. Do not run the destructive `Down` path against a shared or Azure database.

## Monitoring-rule usage

`IMonitoringRuleStore` exposes `GetAsync`, `AddAsync` and `SaveAsync`. Loads restore the whole aggregate (committed revisions and drafts with proposals) through `MonitoringRule.Restore`, which re-validates the committed chain before the aggregate is used. Writes persist the aggregate state captured in the snapshot under its concurrency token.

## Prerequisites

- .NET 10 SDK (see `global.json`).
- The repository-local EF Core tool manifest. Restore it once from the repository root:

```bash
dotnet tool restore
```

- A reachable SQL Server instance for running migrations or the integration tests. The integration tests use Testcontainers and therefore require a running Docker engine.

## Connection string

The runtime connection string is supplied by the composition root through `AddTradingEngineInfrastructure(connectionString)`. The conventional configuration key is `ConnectionStrings:TradingEngine`, settable through the `ConnectionStrings__TradingEngine` environment variable. In the deployed development environment, Bicep sets that app setting to a passwordless `Authentication=Active Directory Default` connection string so the Web App connects through its system-assigned managed identity; see [Azure SQL development database](azure-sql-development-database.md). The design-time factory `TradingEngineDbContextFactory` reads the same environment variable and falls back to a localdb placeholder so that `dotnet ef` commands work without a live server.

## Migrations

Migrations live in `src/TradingEngine.Infrastructure/Persistence/Migrations` alongside the model snapshot. Common commands, run from the repository root:

```bash
dotnet ef migrations add <Name> --project src/TradingEngine.Infrastructure --startup-project src/TradingEngine.Infrastructure
dotnet ef database update --project src/TradingEngine.Infrastructure --startup-project src/TradingEngine.Infrastructure
dotnet ef migrations bundle --project src/TradingEngine.Infrastructure --startup-project src/TradingEngine.Infrastructure --output efbundle.exe
```

The documented command produces a single-file, framework-dependent bundle; run it against a target server with `efbundle.exe --connection "<connection string>"`. A self-contained bundle additionally requires `--self-contained` and an appropriate target runtime (for example `--target-runtime win-x64`).

Migrations never run at application startup. Against the development Azure SQL database they are applied by the manually triggered `Migrate development database` workflow, which builds the bundle on the runner and executes it as a dedicated migration identity over OIDC; see the [operations runbook](azure-sql-operations-runbook.md).

## Testing

`tests/TradingEngine.Infrastructure.IntegrationTests` runs the stores against a real SQL Server instance in a Testcontainers container. The fixtures apply all migrations at startup and then verify the round-trip, conflict and not-found outcomes, the `xml` column types, canonical XML storage, atomicity of failed writes, cancellation propagation and that unexpected failures throw.

The monitoring-rule fixtures additionally cover draft edit/delete, immediate and scheduled commits, reschedules, removals and in-timeline renumbering under the unique indexes, rollback when a save fails mid-transaction, stale-token and competing-writer concurrency, corrupt-row rejection, sub-tick instant rejection, applicability reads at and before the first committed boundary, and the migration upgrade path from the legacy schema (backfill, provenance and cutover read semantics). Direct-SQL constraint fixtures verify that the check constraints and restrictive deletes hold without domain validation in the way: drafts with and without proposals, committed rows with missing or non-positive numbers, equal or inverted boundaries, valid closed and open-ended periods, and blocked deletions that preserve history.

```bash
dotnet test tests/TradingEngine.Infrastructure.IntegrationTests --configuration Release
```

The SQL integration tests require Docker. They fail when Docker is unavailable; they are not silently skipped.
