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

Committed effective periods form one contiguous chain ordered by start, each revision's `EffectiveTo` equal to the next revision's `EffectiveFrom` and the last revision open-ended; `RevisionNumber` equals the one-based position in that chain. The schema enforces the per-row pieces of that shape; the chain invariants are asserted by the domain restoration validation on every load, and whole-aggregate mutations are serialised by the `MonitoringRules.RowVersion` token.

### Effective-period boundary semantics

Committed periods are half-open UTC `Instant` intervals `[EffectiveFrom, EffectiveTo)`: the start is inclusive, the end is exclusive and a `NULL` end is open-ended. At a shared boundary instant `T` only the successor applies: `T - 1 ns` resolves the predecessor while `T` and `T + 1 ns` resolve the successor. The predecessor's end is never adjusted by a tick, millisecond or day, and no end-of-day values, tolerances or independently rounded boundaries are used; the smallest storable period is one `datetime2(7)` tick (100 ns). Splits, reschedules and removals preserve the exact shared boundary. No revision applies before the first committed start — that gap is intentional — and empty or draft-only timelines are valid. Draft proposals stay outside the committed chain and may overlap or carry an inverted proposed ordering.

`ChartAnalysisDefinitions` (legacy):

| Column | Type | Notes |
| --- | --- | --- |
| `WatchedInstrumentId` | `uniqueidentifier` | Primary key and foreign key to `WatchedInstruments.Id` (cascade delete). |
| `DefinitionXml` | `xml` | Required; the canonical XML produced by `ChartAnalysisDefinitionXmlSerializer`. |

The legacy single-definition table is retained as a read-only archive. Registrations no longer write it; the revision history is the only authoritative configuration source. The `MonitoringRuleRevisionHistory` migration copies each legacy row into an initial committed revision (see [Migration cutover](#migration-cutover)).

## Timestamps

`NodaTime.Instant` values are mapped to `datetime2(7)` columns through explicit value converters; reads apply `DateTime.SpecifyKind(..., DateTimeKind.Utc)` before `Instant.FromDateTimeUtc` so round-tripped values remain UTC instants.

`Instant` carries nanosecond precision while `datetime2(7)` resolves to 100 ns ticks. The persisted-instant policy rejects out-of-range or sub-tick values with `ArgumentOutOfRangeException` instead of silently truncating or rounding, and it is enforced at two points:

- Application validates every instant destined for storage before a Domain operation mutates the aggregate, through `PersistedInstant.Require`. The values that must satisfy the policy are creation timestamps, committed effective boundaries and draft proposal dates. `RegisterWatchedInstrumentHandler` applies it to the captured `IClock` instant before creating the registration, and revision command paths apply it to the persisted boundary before calling `MonitoringRule` timeline operations.
- The stores re-run the same policy through `MonitoringRuleRowMapping.ValidateInstants` and `PersistedInstant.Require` as a defensive check immediately before writes reach the database.

Instants used only for comparison — a captured `now` for begun-period checks and the `at` argument of effective-at lookups — are passed to Domain unvalidated, so sub-tick lookup instants keep working and begun-period checks always use the exact supplied value. `IClock` is captured once per operation in Application and the same value is used for checks and mutation; when that captured `now` itself becomes a persisted boundary (for example `ApplyNow`), the policy is applied to it first. Because persisted boundaries are never adjusted, they always round-trip byte-for-byte and in-memory half-open-interval lookups agree with the stored values.

## Atomic rule saves and concurrency

`IMonitoringRuleStore.GetAsync` returns a `MonitoringRuleSnapshot`: the restored aggregate plus the `MonitoringRules.RowVersion` token captured at load. `SaveAsync` writes the full desired revision state inside one transaction against that token; any conflict or failure aborts the transaction and rolls back every staged change.

The save proceeds in two `SaveChangesAsync` steps inside one transaction. Every emitted statement must satisfy the period and numbering checks and the filtered unique indexes at its own point in time, so correctness never depends on the order EF emits updates:

1. Deletes of removed revisions plus withdrawal of every surviving row whose committed state changes — its `EffectiveFrom`, `EffectiveTo` and `RevisionNumber` are all set to `NULL` in one update, temporarily draft-shaped and therefore invisible to the check constraints and filtered unique indexes — and an unconditional `MonitoringRules` update that validates the loaded `rowversion`. A stale token fails here before any revision write.
2. Added revisions are inserted and final scalar values — committed start, end and number, plus XML, reason and proposals — are applied to the surviving rows in a single update each.

Because phase one removes or blanks every old committed value, phase two only ever sees final values: a revision may reuse a start vacated by a deleted or rescheduled neighbour, adjacent reschedules may chain through several rows, and no intermediate `EffectiveFrom`/`EffectiveTo` pairing is ever committed to the server.

A failed transaction can only leave the untouched pre-save state: deletes, withdrawals and final updates all roll back together. The store never retries, repairs or rewrites persisted data.

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

### Coordinated application cutover

The backfill runs once inside the migration, so the application version and the schema must change together. Deploying the new application first fails because the tables do not exist; letting the old application keep running after the migration fails differently — it still registers instruments only into the legacy table, which the new read path cannot see, so those registrations become invisible to the new model.

The `Deploy development` workflow is therefore gated by the `DEPLOYMENT_HOLD` repository variable: while it is `true`, pushes to `main` build and test but skip the deploy job. The coordinated procedure — hold deployment before merging, quiesce the Web App, apply the migration through `Migrate development database`, verify the backfill, deploy the matching build and smoke-test before resuming writes — is documented step by step in the [operations runbook](azure-sql-operations-runbook.md#10-coordinated-schemaapplication-cutover).

Reverting the application afterwards is unsafe once the new model has accepted writes: revisions, drafts and rescheduled history exist only in the new tables, while a reverted application would keep writing the legacy table, silently splitting authoritative configuration between two stores. Recovery is fix-forward; the legacy table is preserved untouched so the pre-migration state always remains inspectable.

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

The monitoring-rule fixtures additionally cover draft edit/delete, immediate and scheduled commits, reschedules, removals and in-timeline renumbering under the unique indexes, combined removals and adjacent reschedules in one save (including a start reused after its previous holder moved), rollback when a save fails mid-transaction, stale-token and competing-writer concurrency, corrupt-row rejection, sub-tick instant rejection, exact boundary lookups at `T - 1 ns`, `T` and `T + 1 ns` and inside a 100 ns minimum period through the production read path, applicability reads at and before the first committed boundary, and the migration upgrade path from the legacy schema (backfill, provenance and cutover read semantics). Direct-SQL constraint fixtures verify that the check constraints and restrictive deletes hold without domain validation in the way: drafts with and without proposals, committed rows with missing or non-positive numbers, equal or inverted boundaries, valid closed and open-ended periods, and blocked deletions that preserve history.

```bash
dotnet test tests/TradingEngine.Infrastructure.IntegrationTests --configuration Release
```

The SQL integration tests require Docker. They fail when Docker is unavailable; they are not silently skipped.
