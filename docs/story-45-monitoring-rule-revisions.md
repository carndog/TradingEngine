# Story 45 — Monitoring-rule revision timelines

Implements [issue #45](https://github.com/carndog/TradingEngine/issues/45): business-effective revision history for monitoring-rule chart-analysis definitions, end to end from domain mechanics through the HTTP API.

## Outcome

A monitoring rule owns a continuous, half-open revision timeline. From the rule's coverage origin — fixed at creation — every instant resolves to exactly one committed `ChartAnalysisDefinition`. Draft definitions are first-class working proposals outside the timeline until committed by `apply` (from now) or `schedule` (over an explicit future range). The timeline guarantees atomic insertions: boundaries split where needed, covered periods are replaced, and bounded splits inside a single period create a continuation revision preserving the original definition's remainder. The coverage origin, begun periods and history can never be rewritten.

## What was built

### Domain (`TradingEngine.Domain`)

- `RevisionTimeline<TDefinition>` reworked to a continuous model:
  - `Create(initialRevisionId, definition, createdAt, createdBy)` starts a timeline committed open-ended from `createdAt` — the coverage origin.
  - `Restore(revisions)` rebuilds persisted state and re-validates contiguity and numbering.
  - `ApplyNow(draftId, effectiveTo?, continuationId, now)` and `Schedule(draftId, effectiveFrom, effectiveTo?, continuationId, now)` perform atomic insertions over `[from, to)`; null `to` is open-ended and replaces all covered future revisions. Bounded splits preserve covered-revision identity on the left remainder and mint a continuation revision (caller-supplied id) on the right. A bounded `ApplyNow` inserts `[now, to)` in one operation — the continuation keeps the definition applicable at `to`, including the definition of a later future revision when `to` crosses one.
  - Guards: `Backdated` (start < now), `UncoveredStart` (start before the coverage origin), `PeriodBegun` (start on a begun boundary or edits to begun periods), `InvalidPeriod`, `DuplicateId`, `IdRequired` (missing continuation id when needed), `NoChangeRequested` (amendment supplying neither definition nor start).
  - `EditScheduledRevision`, `Reschedule`, `RemoveScheduledRevision` operate on future revisions only; the origin revision rejects reschedule/remove with `CoverageOriginProtected`. `AmendScheduledRevision` validates the target and the whole requested change before mutating, so a combined definition edit plus an invalid reschedule leaves the aggregate untouched. Reschedule/remove reconcile the predecessor's end boundary so coverage stays contiguous.
- `MonitoringRule.Create` now requires the initial revision id + definition so a rule is covered from `CreatedAt`; `CoverageOrigin` is exposed. `Restore` is the deliberate exception to that creation invariant for persisted and migrated state — it rebuilds whatever contiguous committed chain and drafts were stored (including a draft-only rule) without enforcing a non-empty chain.
- `MonitoringRule.ChangeReasonMaxLength` (512) matches the `MonitoringRuleRevisions.ChangeReason` column and is validated before persistence on draft create/edit and revision amendment, so an over-long reason returns `monitoring_rule.change_reason_too_long` instead of surfacing as a SQL exception.
- New `ErrorType` members: `PreconditionRequired` (428) and `PreconditionFailed` (412).

### Application (`TradingEngine.Application`)

- Vertical slices under `MonitoringRules/`: `Drafts` (create/edit/delete), `Lifecycle` (apply/schedule/edit-revision/remove-revision), `Read` (timeline, applicable revision).
- `MonitoringRuleCommandSupport.MutateAsync` centralises: required expected-token, `PersistedInstant` validation of caller and clock instants, load → mutate → save → reload.
- Commit/continuation identifiers generated via `Guid.NewGuid()` in handlers, keeping Domain deterministic — `RevisionTimeline` itself creates no identifiers (the initial commit passes `Guid.Empty` because a committed-from-origin insert never needs a continuation).

### API (`TradingEngine.Api` / `TradingEngine.Contracts`)

- Route group `/api/watched-instruments/{instrumentId}/monitoring-rule` (see [monitoring-rule-revision-api.md](monitoring-rule-revision-api.md) for the full reference).
- Write-request timestamps are `string` fields parsed by `RequestInstants` straight into nanosecond `Instant`s — System.Text.Json never binds them to `DateTimeOffset`, so fractional precision cannot be discarded before `PersistedInstant` validates it. Persisted-boundary values must be exact `datetime2(7)` values; the `at` query keeps full nanosecond precision for comparisons.
- `POST .../drafts/{draftId}/apply` accepts an optional `{ "effectiveTo" }` body for bounded apply-from-now; the start is captured once from `IClock`.
- `EasyAuthPrincipalMiddleware` decodes the platform `X-MS-CLIENT-PRINCIPAL` header on `/api` routes when `Authentication:EasyAuth:TrustPlatformHeaders` is enabled (set by Bicep under `configureEntraAuth`); `RequestActor` records the principal's object/subject identifier and otherwise falls back to `unverified-local-caller`. A caller-supplied header is never trusted locally.
- ETag/`If-Match` optimistic concurrency over the SQL `rowversion`.
- `ApiProblemDetails` maps `ErrorType` to HTTP status with stable `code` extensions, plus explicit `401` problems for missing/malformed trusted principals.

### Persistence (`TradingEngine.Infrastructure`)

- `MonitoringRuleRevisions` rows (committed + drafts) with XML definitions and a `rowversion` token on `MonitoringRules`; `SqlServerMonitoringRuleStore` reconciles the full timeline atomically and rejects stale tokens.
- `SqlServerWatchedInstrumentStore` passes the registration definition into `MonitoringRule.Create`, so instrument registration seeds the initial revision.

## Bugs found and fixed during verification

- **`Insert` accepted pre-origin open-ended inserts.** `UncoveredStart` was only returned when the inserted range overlapped nothing; an open-ended range always overlaps the tail, so an insert before the coverage origin silently rewrote it. `Insert` now rejects `effectiveFrom < origin` directly (`RevisionTimeline.cs`).
- **`RequirePersistable(Instant?)` threw on null.** `Result<T>.Success` rejects null values, so any optional instant left null (no `proposedPeriod`, open-ended `effectiveTo`, no `effectiveFrom` on revision edit) crashed the endpoint with 500. The nullable overload now returns `(Instant?, Error?)` (`MonitoringRuleCommandSupport.cs`, 4 handler call sites).

Review-driven corrections on this branch:

- **Timestamp binding discarded precision.** `DateTimeOffset` DTO properties let System.Text.Json truncate sub-tick fractions before `PersistedInstant` could reject them. All timestamp request fields are now strings parsed losslessly by `RequestInstants`; unrepresentable persisted instants fail `monitoring_rule.instant_not_persistable` and never reach the store.
- **`ApplyNow` could not be bounded.** The optional `effectiveTo` is now threaded request → command → handler → `MonitoringRule.ApplyNow(draftId, effectiveTo, continuationId, now)`.
- **`createdBy` never saw the deployed identity.** `RequestActor` previously read claims that nothing populated. `EasyAuthPrincipalMiddleware` now decodes the platform principal behind an explicit trust flag; locally the header is ignored.
- **Revision edits were non-atomic and under-validated.** Sequential edit-then-reschedule applied the definition even when the reschedule failed, an unknown id with an empty body returned 200, and a 513+ `changeReason` became a SQL exception. `AmendScheduledRevision` validates intent (`no_change_requested`), target and reschedule before mutating, and reason length is checked at 512.
- **The API test stub shared its live aggregate.** `StubMonitoringRuleStore` now captures revision state on save and restores a detached aggregate on every load, so in-memory mutations cannot leak into stored state and tokens only rotate after a successful save.

## Test coverage

| Suite | Tests | Status |
| --- | --- | --- |
| Domain | 238 | passing |
| Application | 27 | passing |
| Architecture | 9 | passing |
| Infrastructure (unit) | 19 | passing |
| API integration (`WebApplicationFactory` + detached stub store) | 119 | passing — lifecycle, timestamp, concurrency, read and EasyAuth identity fixtures |
| Infrastructure integration (Testcontainers SQL Server) | 59 | passing — includes generated-continuation row identity/boundaries after reload, multi-period bounded and open-ended replacement deletion, bounded apply and exact-start immutability |

Domain test files were rewritten for the continuous semantics: all `new()` timeline constructions became `Empty()`/`Create()`/`Restore()`, all `ApplyNow`/`Schedule` call sites gained `continuationId`/`effectiveTo` parameters, and gap-dependent tests were rewritten as bounded inserts or origin-protection cases.

## Files of note

- `src/TradingEngine.Domain/Revisions/RevisionTimeline.cs`
- `src/TradingEngine.Domain/MonitoringRules/MonitoringRule.cs`, `MonitoringRuleErrors.cs`
- `src/TradingEngine.Application/MonitoringRules/**`, `MonitoringRuleCommandSupport.cs`
- `src/TradingEngine.Api/MonitoringRules/MonitoringRuleEndpoints.cs`, `RequestInstants.cs`, `ApiProblemDetails.cs`, `RequestActor.cs`
- `src/TradingEngine.Api/Authentication/EasyAuthPrincipalMiddleware.cs`, `EasyAuthClientPrincipal.cs`, `EasyAuthOptions.cs`
- `src/TradingEngine.Contracts/MonitoringRules/*`
- `tests/TradingEngine.Api.IntegrationTests/MonitoringRules/*EndpointTests.cs`, `StubMonitoringRuleStore.cs`, `MonitoringRuleApiHost.cs`
- `tests/TradingEngine.Api.IntegrationTests/Authentication/EasyAuthPrincipalMiddlewareTests.cs`
- `tests/TradingEngine.Infrastructure.IntegrationTests/Persistence/*Tests.cs`

## Follow-ups

- Deploy to the Easy Auth environment and smoke-test the real `X-MS-CLIENT-PRINCIPAL` claim mapping end to end — the middleware and tests are local-only so far, and the claim order (`oid` → `nameidentifier` → `sub`) should be confirmed against a live sign-in. This check has not been performed yet.
