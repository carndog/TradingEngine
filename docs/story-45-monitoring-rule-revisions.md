# Story 45 — Monitoring-rule revision timelines

Implements [issue #45](https://github.com/carndog/TradingEngine/issues/45): business-effective revision history for monitoring-rule chart-analysis definitions, end to end from domain mechanics through the HTTP API.

## Outcome

A monitoring rule owns a continuous, half-open revision timeline. From the rule's coverage origin — fixed at creation — every instant resolves to exactly one committed `ChartAnalysisDefinition`. Draft definitions are first-class working proposals outside the timeline until committed by `apply` (from now) or `schedule` (over an explicit future range). The timeline guarantees atomic insertions: boundaries split where needed, covered periods are replaced, and bounded splits inside a single period create a continuation revision preserving the original definition's remainder. The coverage origin, begun periods and history can never be rewritten.

## What was built

### Domain (`TradingEngine.Domain`)

- `RevisionTimeline<TDefinition>` reworked to a continuous model:
  - `Create(initialRevisionId, definition, createdAt, createdBy)` starts a timeline committed open-ended from `createdAt` — the coverage origin.
  - `Restore(revisions)` rebuilds persisted state and re-validates contiguity and numbering.
  - `ApplyNow(draftId, continuationId, now)` and `Schedule(draftId, effectiveFrom, effectiveTo?, continuationId, now)` perform atomic insertions over `[from, to)`; null `to` is open-ended and replaces all covered future revisions. Bounded splits preserve covered-revision identity on the left remainder and mint a continuation revision (caller-supplied id) on the right.
  - Guards: `Backdated` (start < now), `UncoveredStart` (start before the coverage origin), `PeriodBegun` (start on a begun boundary or edits to begun periods), `InvalidPeriod`, `DuplicateId`, `IdRequired` (missing continuation id when needed).
  - `EditScheduledRevision`, `Reschedule`, `RemoveScheduledRevision` operate on future revisions only; the origin revision rejects reschedule/remove with `CoverageOriginProtected`. Reschedule/remove reconcile the predecessor's end boundary so coverage stays contiguous.
- `MonitoringRule.Create` now requires the initial revision id + definition so a rule is covered from `CreatedAt`; `CoverageOrigin` is exposed.
- New `ErrorType` members: `PreconditionRequired` (428) and `PreconditionFailed` (412).

### Application (`TradingEngine.Application`)

- Vertical slices under `MonitoringRules/`: `Drafts` (create/edit/delete), `Lifecycle` (apply/schedule/edit-revision/remove-revision), `Read` (timeline, applicable revision).
- `MonitoringRuleCommandSupport.MutateAsync` centralises: required expected-token, `PersistedInstant` validation of caller and clock instants, load → mutate → save → reload.
- Commit/continuation identifiers generated via `Guid.NewGuid()` in handlers, keeping Domain deterministic.

### API (`TradingEngine.Api` / `TradingEngine.Contracts`)

- Route group `/api/watched-instruments/{instrumentId}/monitoring-rule` (see [monitoring-rule-revision-api.md](monitoring-rule-revision-api.md) for the full reference).
- ETag/`If-Match` optimistic concurrency over the SQL `rowversion`; `createdBy` resolved from caller claims.
- `ApiProblemDetails` maps `ErrorType` to HTTP status with stable `code` extensions.

### Persistence (`TradingEngine.Infrastructure`)

- `MonitoringRuleRevisions` rows (committed + drafts) with XML definitions and a `rowversion` token on `MonitoringRules`; `SqlServerMonitoringRuleStore` reconciles the full timeline atomically and rejects stale tokens.
- `SqlServerWatchedInstrumentStore` passes the registration definition into `MonitoringRule.Create`, so instrument registration seeds the initial revision.

## Bugs found and fixed during verification

- **`Insert` accepted pre-origin open-ended inserts.** `UncoveredStart` was only returned when the inserted range overlapped nothing; an open-ended range always overlaps the tail, so an insert before the coverage origin silently rewrote it. `Insert` now rejects `effectiveFrom < origin` directly (`RevisionTimeline.cs`).
- **`RequirePersistable(Instant?)` threw on null.** `Result<T>.Success` rejects null values, so any optional instant left null (no `proposedPeriod`, open-ended `effectiveTo`, no `effectiveFrom` on revision edit) crashed the endpoint with 500. The nullable overload now returns `(Instant?, Error?)` (`MonitoringRuleCommandSupport.cs`, 4 handler call sites).

## Test coverage

| Suite | Tests | Status |
| --- | --- | --- |
| Domain | 218 | passing |
| Application | 27 | passing |
| Architecture | 9 | passing |
| Infrastructure (unit) | 19 | passing |
| API integration (`WebApplicationFactory` + stub store) | 69 | passing — includes 10 new monitoring-rule endpoint tests |
| Infrastructure integration (Testcontainers SQL Server) | — | compile-clean; require Docker to run |

Domain test files were rewritten for the continuous semantics: all `new()` timeline constructions became `Empty()`/`Create()`/`Restore()`, all `ApplyNow`/`Schedule` call sites gained `continuationId`/`effectiveTo` parameters, and gap-dependent tests were rewritten as bounded inserts or origin-protection cases.

## Files of note

- `src/TradingEngine.Domain/Revisions/RevisionTimeline.cs`
- `src/TradingEngine.Domain/MonitoringRules/MonitoringRule.cs`, `MonitoringRuleErrors.cs`
- `src/TradingEngine.Application/MonitoringRules/**`, `MonitoringRuleCommandSupport.cs`
- `src/TradingEngine.Api/MonitoringRules/MonitoringRuleEndpoints.cs`, `ApiProblemDetails.cs`, `RequestActor.cs`
- `src/TradingEngine.Contracts/MonitoringRules/*`
- `tests/TradingEngine.Api.IntegrationTests/MonitoringRules/MonitoringRuleEndpointTests.cs`

## Follow-ups

- Run the Testcontainers-backed infrastructure integration suite where Docker is available.
- Wire revision endpoints into the deployed Easy Auth environment and record real `createdBy` identities.
