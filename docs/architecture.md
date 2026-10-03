# Solution boundaries and dependency rules

The solution uses Hexagonal Architecture pragmatically. Domain and Application form the reusable core. HTTP is a driving adapter; persistence and external integrations will be driven adapters in Infrastructure. The API project is the composition root.

```mermaid
flowchart TD
    API[TradingEngine.Api] --> Application[TradingEngine.Application]
    API --> Infrastructure[TradingEngine.Infrastructure]
    API --> Contracts[TradingEngine.Contracts]
    Infrastructure --> Application
    Infrastructure --> Domain[TradingEngine.Domain]
    Application --> Domain
```

## Project responsibilities

| Project | Responsibility | Allowed production-project references |
| --- | --- | --- |
| `TradingEngine.Domain` | Entities, value objects, enums and invariants | None |
| `TradingEngine.Application` | Use-case orchestration and use-case-specific ports | Domain |
| `TradingEngine.Infrastructure` | Driven adapters implemented by later stories | Application, Domain |
| `TradingEngine.Contracts` | Stable HTTP and message DTOs | None |
| `TradingEngine.Api` | HTTP driving adapter and composition root | Application, Infrastructure, Contracts |

Contracts are transport-facing types rather than domain types. Domain and Application do not reference Contracts, so transport versioning cannot leak into the core.

## Application conventions

- Organise use cases as small vertical slices under a capability folder, for example `WatchedInstruments/Register`.
- Give each use case a command and a handler. Add a use-case-specific port only when the use case needs an external capability.
- Do not add a generic repository or mediator abstraction without a demonstrated need.
- Application reads the current time through NodaTime `IClock`. It passes the resulting `Instant` into Domain methods explicitly, and into store ports wherever a lookup is time-sensitive.
- Infrastructure implements the ports. The current adapters are the SQL Server stores for watched instruments and monitoring rules.

## Domain conventions

- Aggregates expose ordinary .NET primitives rather than per-field wrapper types.
- Absolute timestamps are NodaTime `Instant` values.
- Domain methods never read a system clock.
- State transitions reject invalid or meaningless changes and reject timestamps earlier than the latest recorded change.
- Expected validation and state-transition rejections return `Result` outcomes rather than throwing.
- Sampling intervals are plain seconds. Provider-specific rate-limit mappings belong outside Domain.

## Expected failures and Results

`TradingEngine.Domain.Results` provides a small package-free outcome model used by Domain and Application: `Result`, `Result<T>`, `Error` and `ErrorType`. No Result library or mediator package is introduced.

- Return a failed `Result` for expected outcomes: invalid user or domain data, invalid state transitions, duplicate or conflicting changes and missing expected resources.
- Throw for unexpected outcomes: SQL, network and runtime failures, `OperationCanceledException`, corrupt persisted XML or database state, missing configuration, and programming defects such as null arguments or impossible internal states.
- A failed `Result` carries exactly one `Error`. A successful `Result<T>` exposes a non-null `Value`; reading `Value` on a failure is programmer misuse and throws `InvalidOperationException`.
- An `Error` has a stable machine-readable `Code`, a public-safe `Description` and an `ErrorType` classification. Codes use lowercase dotted snake_case segments, for example `watched_instrument.symbol_required` or `chart_analysis.duplicate_zone_id`.
- `ErrorType` currently supports `Validation`, `Conflict` and `NotFound`. A later API adapter will map them to Problem Details: `Validation` to 400, `Conflict` to 409 and `NotFound` to 404. Unexpected failures and cancellation remain exception-based and are never converted into Results.
- Cohesive catalogues such as `WatchedInstrumentErrors` and `ChartAnalysisErrors` hold the complete `Error` definitions for their area. Per-type rule enums are not used.
- At the Infrastructure XML boundary, a failed Domain `Result` during deserialization is translated into `InvalidDataException` carrying the stable error code. Malformed or corrupt persistence XML is never converted into an Application validation `Result`, because clients do not submit persistence XML.

## Revision timelines

`TradingEngine.Domain.Revisions` provides reusable business-effective revision mechanics that aggregates compose rather than inherit:

- `Revision<TDefinition>` wraps a typed policy payload with a stable `Guid` identity, an optional business `RevisionNumber`, creation metadata (`CreatedAt`, `CreatedBy`), an optional `ChangeReason`, an optional committed `EffectivePeriod` and an optional `RevisionProposal` carrying tentative draft dates. `TDefinition` is a plain payload; it does not implement a revision contract.
- `TDefinition` must be deeply immutable. The timeline protects the revision shell (identity, number, period, proposal) but holds the payload by reference and does not clone or serialize it, so a payload exposing a mutable member would let callers rewrite history through `EffectiveAt(...).Definition` or through a definition reference they still hold. Payload types must therefore expose only read-only scalars and genuinely read-only collections: a private defensive copy of each input collection wrapped in a read-only wrapper (`Array.AsReadOnly(source.ToArray())`). Copying prevents later changes through the caller's original collection; the wrapper prevents indexed replacement, `Add`, `Remove` or `Clear` through the exposed `IReadOnlyList<T>` after casting to `IList<T>`. Exposing a bare array behind `IReadOnlyList<T>` is not sufficient, because arrays report `IsReadOnly` while still permitting indexed assignment.
- `EffectivePeriod` is an immutable half-open interval `[EffectiveFrom, EffectiveTo)` of NodaTime `Instant` values. A null `EffectiveTo` is open-ended. A revision without a committed period is a Draft.
- `RevisionTimeline<TDefinition>` owns temporal validation, scheduling, splitting, rescheduling, removal and effective-at retrieval. Drafts live outside the committed timeline: they do not reserve periods, do not take effect automatically and are excluded from `EffectiveAt`. Several drafts may carry overlapping proposals.
- `ApplyNow` and `Schedule` commit a draft with an effective start at or after the captured `now`. A start inside an existing period splits that revision: the original keeps its identity and definition and ends at the new start; the successor inherits the original end boundary, so later scheduled revisions keep their periods. A start exactly equal to an existing start is rejected. Backdated starts and zero-length or inverted periods are rejected.
- A committed definition is immutable exactly when `now >= EffectiveFrom`, including at the start boundary. Entirely future revisions may be edited, rescheduled or removed; rescheduling and removal reconcile the adjacent predecessor's end boundary so replacements meet without gaps. Nothing that already applied before the captured `now` changes.
- Applicability and editability are derived from the dates and the captured instant. No `Superseded` state is stored and no background job flips lifecycle states.
- Business revision numbers are the effective rank of committed revisions (1-based, in `EffectiveFrom` order). Inserting or rescheduling a future revision renumbers only future revisions; a revision whose period has begun keeps its number because nothing can be placed before it. Drafts have no number. The `Guid` is the identity; the number is display and audit metadata and is distinct from any XML schema version or SQL `rowversion`.
- Expected failures return `Result` values using the `RevisionErrors` catalogue (`revision.*` codes). Null definitions are programmer errors and throw.

Application obtains the current instant through `IClock` and passes the same captured `Instant` into each Domain operation. Domain never reads a clock.

## Monitoring-rule definitions

- `MonitoringRule` is the owning aggregate for per-instrument monitoring-rule revisions. It holds its own identity and `WatchedInstrumentId` scope and composes a `RevisionTimeline<ChartAnalysisDefinition>`; `MonitoringRuleErrors` holds its aggregate-specific errors.
- `MonitoringRule.Restore` rebuilds a persisted aggregate without replaying operations and re-validates the committed chain; corrupt or incomplete revision state is rejected instead of repaired.
- `IMonitoringRuleStore` persists the whole aggregate. Reads return a `MonitoringRuleSnapshot` pairing the restored rule with a `rowversion` concurrency token; saves apply the full mutation atomically and reject stale tokens. See [Current configuration persistence](current-configuration-persistence.md).
- `ChartAnalysisDefinition` remains the validated policy payload. Its invariants are enforced by its `Result`-returning factory, so every definition placed on the timeline is complete and valid by construction. `ChartAnalysisDefinition.SupportZones`, `ChartAnalysisDefinition.ResistanceZones` and `ChartZone.Conditions` are read-only wrappers over private defensive copies, and every other member is a read-only scalar or value object, so a definition cannot be changed after construction and the timeline does not re-validate it at commit.
- Its variable chart-analysis definition is stored as canonical XML on each revision row and mapped to a validated Domain model by an Infrastructure adapter.
- The XML persistence schema is independent of versioned HTTP and message contracts. Clients send transport DTOs and do not construct persistence XML.
- Price observations, signals, risk decisions and orders are separate records rather than mutable state inside the definition.
- A definition whose effective period has begun is immutable. Changing future behaviour is done by scheduling a split at an instant at or after now; the historical definition is never rewritten.
- Generic schema and synthetic examples are public-safe. Real definitions, meaningful parameters and strategy or execution logic remain private.

See [Chart-analysis definition XML](chart-analysis-definition-xml.md) for the canonical contract.

## Instrument identification

`WatchedInstrument` is a straightforward aggregate with ordinary properties:

| Property | Meaning |
| --- | --- |
| `Id` | Stable internal `Guid` identity. Must not be `Guid.Empty`. |
| `Symbol` | Instrument ticker or symbol. Trimmed, uppercased, at most 64 characters, no whitespace. |
| `Exchange` | Exchange or venue code where the instrument is quoted. Required, trimmed, uppercased, at most 20 characters; ASCII letters, digits, periods, hyphens and underscores. |
| `QuoteCurrency` | Currency the instrument is quoted in. Required, trimmed, uppercased, 3 to 10 ASCII letters or digits, covering fiat codes such as `GBP` and `USD` and crypto quote codes such as `USDT` and `USDC`. |
| `MonitoringState` | `Configured` or `Monitored`. Drives the start/stop monitoring transitions. |
| `SamplingIntervalSeconds` | Price-sampling cadence in seconds, from 1 through 3600 inclusive. |
| `CreatedAt` | `Instant` the instrument was registered. |
| `LastChangedAt` | `Instant` of the latest recorded change. Change timestamps earlier than this are rejected. |

- `Id` is the stable internal identity. The future persistence business key is `Exchange` + `Symbol` + `QuoteCurrency`.
- Conceptual examples: a stock as `LLOY` / `XLON` / `GBP`; crypto as `BTC` / `ETORO` / `USD` or `XRP` / `ETORO` / `USD`.
- Broker- and provider-specific instrument identifiers are external mappings owned by outbound adapters and Infrastructure, not by Domain.
- Supported exchanges and currencies may initially be controlled by Application configuration or reference data rather than dedicated persistence.

## Enforcement

`TradingEngine.Architecture.Tests` checks the production project-reference allow-list, the Domain assembly dependencies, forbidden Domain package references and the absence of `IClock` from Domain types. These tests are intentionally narrow and complement compiler-enforced project references.

The forbidden Domain dependency set includes outer Trading Engine layers, ASP.NET Core, Azure SDKs, EF Core and broker-specific packages. NodaTime is the sole intentional third-party Domain dependency in this foundation.

## Build identity

Every project receives a non-sensitive `CommitSha` assembly metadata value. Local builds use `local`; GitHub Actions builds automatically use `GITHUB_SHA`. A manual build can provide `-p:SourceRevisionId=<commit>` explicitly. `/version` returns only the API application name, assembly version and this commit identifier.
