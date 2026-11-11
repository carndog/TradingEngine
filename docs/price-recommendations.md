# Saved-price recommendations and notification dispatch

Issue [#11](https://github.com/carndog/TradingEngine/issues/11) wires the
[chart-analysis evaluation contract](chart-analysis-evaluation.md) into a
read-only monitoring pipeline. The pipeline processes *persisted* price
observations — it never queries the market-data provider and never touches any
execution or order pathway. The only side effects are durable recommendation
records and notification emails sent through Azure Communication Services
(ACS) Email.

## Pipeline shape

Two independent hosted services share a SQL transaction boundary but never
share a timer:

1. `RecommendationEvaluationHostedService` (`Recommendations:*`) ticks
   `EvaluateSavedPricesHandler`, which walks each evaluable instrument's saved
   `PriceObservations` in `ScheduledFor` order, selects the committed
   monitoring-rule revision effective at each slot, runs
   `ChartAnalysisEvaluator`, and commits the outcome.
2. `NotificationDispatchHostedService` (`Notifications:*`) ticks
   `DispatchPendingNotificationsHandler`, which claims pending intents,
   renders the email, submits it to ACS Email and records the attempt outcome.

An instrument is *evaluable* when it is a watched instrument that is not
logically deleted and has a monitoring rule. Price-only instruments keep
collecting without ever being scheduled for evaluation, so they cannot
produce notifications.

## Checkpoints and first enablement

`RecommendationCheckpoints` holds one row per instrument:
`(WatchedInstrumentId PK, LastProcessedScheduledFor, EstablishedAt, UpdatedAt)`.

- **First enablement.** When no checkpoint exists, the handler reads the
  instrument's latest saved observation and seeds the checkpoint at that
  `ScheduledFor`. Existing history is therefore the baseline and can never
  generate a flood of emails — only observations saved *after* enablement are
  evaluated.
- **Resume.** Each run reads the checkpoint, reloads the observation at the
  checkpoint slot as the evaluator's `Previous`, and lists saved observations
  after it in `ScheduledFor` order.
- **Per-slot commit.** Every slot updates the checkpoint inside the same SQL
  transaction as its evidence: a successful `NoAction` advances the checkpoint
  with no other write; a `Triggered` result additionally inserts the
  notification intent and its match rows in that transaction. A failed
  evaluation returns before any commit, so the slot remains pending for the
  next run.
- **Concurrency.** The checkpoint update is a compare-and-swap guarded by
  `WHERE LastProcessedScheduledFor = @expected`, which serialises workers on
  the checkpoint row's update lock. A stale worker receives
  `CheckpointConflict` and stops that instrument for the run.

## Durable intents and deduplication

`NotificationIntents` carries the trigger evidence — instrument, slot,
previous slot, the *exact* `MonitoringRuleRevisionId` that was evaluated,
price basis, quote currency, current bid/ask and status — and
`NotificationIntentMatches` carries the ordered triggered ranges.

`UX_NotificationIntents_WatchedInstrumentId_ScheduledFor` enforces one intent
per `(instrument, slot)`: a restart, a replayed slot or a racing worker cannot
insert a second recommendation for the same trigger. A genuine later re-entry
lands on a different slot and produces a new intent. When a commit finds the
intent already present while the checkpoint is still behind — only possible
through manual repair — the checkpoint advance is committed anyway
(`AlreadyRecorded`), which self-heals rather than stalling the instrument.

## Dispatch, claims and ambiguous sends

`NotificationAttempts` records every dispatch attempt and resolution:
`Submitted`, `Failed`, `Ambiguous`, `ResolvedSucceeded`, `ResolvedRejected`,
`Unresolved` and `Suppressed`, with the ACS operation id and a classified
`ErrorKind` (never a raw error payload).

- **Claims.** A worker claims an intent with a token and lease
  (`ClaimToken`/`ClaimedUntil`); only that token may record the outcome.
  Expired claims become dispatchable again, so a crashed worker's intents
  recover without manual work.
- **Operation id.** The ACS send uses the *intent id* as the client-supplied
  operation id, so resubmission after an ambiguous outcome cannot create a
  second ACS operation for the same intent.
- **Ambiguous outcomes.** A timeout, transport error or 5xx after submission
  marks the intent `Ambiguous` instead of resending. The next dispatch probes
  `EmailSendOperation` for the recorded operation id: `Succeeded` resolves to
  `Sent`, `Rejected` resolves to `Failed`, still in-flight or a failed probe
  stays `Ambiguous`, and only a genuinely unknown operation is resent (with
  the same operation id). Explicit client rejections (4xx) fail terminally;
  408/429 retry as `Pending`.
- **Suppression.** After `MaxDispatchAttempts` the intent is marked `Failed`
  with a `Suppressed` attempt row, so a poison intent cannot loop forever.

The email renders the instrument symbol and exchange, the observed price at
the configured basis with its quote currency, each evaluated range (lower –
upper with level and condition), the UTC observation time and the reason, and
states that no order was placed.

## Configuration

| Key | Purpose |
| --- | --- |
| `Recommendations:Enabled` | Evaluator on/off (default `false`). |
| `Recommendations:PollIntervalSeconds` | Run cadence. |
| `Recommendations:MaxObservationsPerInstrument` | Backlog cap per run. |
| `Recommendations:MaxExecutionTimeSeconds` | Run deadline; unprocessed slots resume next run. |
| `Recommendations:PriceBasis` | `Bid` or `Ask` — the side the zones are evaluated against. |
| `Notifications:Enabled` | Dispatcher on/off (default `false`). |
| `Notifications:PollIntervalSeconds` | Run cadence. |
| `Notifications:MaxIntentsPerRun` | Batch cap per run. |
| `Notifications:ClaimLeaseSeconds` | Lease length before an abandoned claim expires. |
| `Notifications:MaxDispatchAttempts` | Attempts before an intent is suppressed. |
| `Notifications:Email:ConnectionString` | ACS connection string — secret, user-secrets/deploy-time only. |
| `Notifications:Email:SenderAddress` | Sender on the ACS email domain. |
| `Notifications:Email:RecipientAddresses` | Recipient array — deploy-time only. |

When `Notifications:Enabled` is true the email settings are validated at
startup; a missing connection string, sender or recipient list fails fast
rather than dispatching half-configured.

## Telemetry

`RecommendationTelemetry` (meter `TradingEngine.Api.Recommendations`) emits
`tradingengine.recommendations.evaluations{outcome}`,
`tradingengine.recommendations.intents_created`,
`tradingengine.notifications.dispatch_attempts{outcome}` and
`tradingengine.notifications.failures`. `PriceCollectionTelemetry` (meter
`TradingEngine.Api.PriceCollection`) emits
`tradingengine.price_collection.failures`. Both hosted services wrap each run
in an activity (`recommendations.evaluate.run`, `notifications.dispatch.run`)
so SQL and log telemetry correlate under one trace per run, and run summaries
log invocation ids and counts.

Bicep alert rules (`enablePipelineFailureAlerts`) watch the two failure
counters via scheduled queries over `customMetrics` with a configurable
threshold. Telemetry deliberately carries only instrument ids, counts,
outcome kinds and provider operation ids — credentials, recipient addresses,
rule XML and zone contents are never logged; the existing sensitive-data
processors scrub connection-string material from exceptions.

## Migration

`20261111214840_NotificationIntents` creates `RecommendationCheckpoints`,
`NotificationIntents`, `NotificationIntentMatches` and
`NotificationAttempts` with the unique index, status/basis/condition check
constraints and `datetime2(7)` conversions described above. It is additive
only: no existing table is altered and no data is rewritten.

## Boundary

The pipeline is generic public infrastructure. Zone contents, recipient
addresses, ACS credentials and any live deployment values remain private under
[PUBLIC_SCOPE.md](../PUBLIC_SCOPE.md); tests and examples use only synthetic
fixtures.
