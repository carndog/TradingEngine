# Weekly signal reports

This runbook describes the complete reporting feature. The host/API and deployment
settings require their corresponding implementation to be merged before use.

## Durable report evidence

Weekly reporting uses its own schedule, report, item and attempt tables. It never
inserts a fake instrument or a digest into the per-instrument notification tables.
The report table's unique subscription/cutoff key identifies one logical weekly
report. The subscription key must include deployment identity.

Generation first claims a report with a fixed cutoff, period and recipient set.
A restart reclaims an expired generation lease without changing those inputs.
Composition, item persistence, delivery readiness and schedule advancement share
one serializable SQL transaction and the same scoped context as the signal reader.
A failed composition or item save exposes no partial report to delivery. The
claim remains recoverable after its lease expires; errors are stored as sanitised
codes. A finished snapshot cannot be overwritten by a retry.

The snapshot retains every selected signal's baseline/latest observation identity,
times, derived values and status facts, together with the rendered message and
recipient set. Detail reads paginate saved items rather than recomputing prices.
Empty periods are recorded and advance the schedule without sending. A catch-up
report records the count and first cutoff of skipped older periods.

Delivery implements the shared dispatcher port. It persists the provider operation
ID and a submission-start attempt before calling ACS. Ambiguous/crashed submissions
are probed using that ID; they are not blindly resent. Known retryable failures
consume a bounded send budget. Leases and claim tokens reject stale workers.
One logical report is guaranteed by the application, not exactly-once inbox delivery.

Disabling a subscription suppresses report dispatch. Re-enabling schedules the next
occurrence while retaining pending reports for reconciliation. Keep the deployment
and subscription identity stable when disabling. Report scheduling/composition and
the host are disabled by default. A configured identity keeps the host running while
disabled so it can persist the paused state; an empty deployment identity registers
no report host. Allow a successful disabled tick before removing identity or stopping
the application. Re-enabling resumes already-claimed work and pending submissions,
but does not create unclaimed historical reports.

Apply `SignalTracking` followed by `WeeklyReportSnapshots` through the existing
controlled migration workflow before deploying report functionality. These are
additive migrations. Down migrations delete the corresponding report/audit evidence;
use forward fixes after real reports exist. No new Azure service is needed.

## Schedule and composition

The default schedule is Sunday 18:00 Europe/London. Local weekly boundaries keep
that wall-clock time across GMT/BST (167/169-hour transition weeks). If a configured
time is skipped by a clock change it moves forward by the gap; an overlapping time
uses its earlier occurrence. First enablement uses the next strictly future cutoff.
After downtime only the latest due cutoff is generated, with the number of older
missed periods recorded. A still-generating report is recovered with its original
cutoff before a new period is claimed.

Composition reads tracked signals in 100-row batches at the fixed cutoff, then
signals archived in `(period start, cutoff]`. All tracked signals remain included
even without new signals. Multiple matches and later re-entries remain separate.
The default snapshot bound is 10,000 rows: exceeding it fails generation visibly
without sending an incomplete report. Operators must investigate or deliberately
increase the bounded configuration before retrying.

Emails include plain text and encoded HTML. `MaxEmailRows` (100) and `MaxEmailBytes`
(128,000 combined UTF-8 subject/body bytes) limit rendered size; these are application
bounds, not a claim about provider limits. The footer states shown/total rows and
links to paginated, authenticated saved evidence. Preview links recompute preview
evidence and do not masquerade as saved reports. Price changes retain their sign,
use canonical units, and are rounded only in email display. Last-known, missing and
unknown-freshness quotes remain explicitly labelled. Range position never infers
profit, loss, an order or a short position.

## Authenticated inspection

All routes use the existing `/api` Easy Auth and request limits. A preview needs no
email credentials, report-store call or enabled scheduler:

- `GET /api/weekly-reports/preview?cutoff=2026-03-29T17:00:00Z&page=1&pageSize=25`
  composes a read-only preview at the supplied cutoff. Omit cutoff to use the injected
  current time. The preview covers the preceding seven local days, returns its fixed
  UTC cutoff for subsequent pages, and never persists, claims, advances or sends.
  Paging a preview re-reads source evidence; use a saved report for immutable audit.
- `GET /api/weekly-reports?maximum=25` lists at most 100 recent run summaries for the
  configured deployment/subscription.
- `GET /api/weekly-reports/{id}?page=1&pageSize=25` reads immutable saved evidence,
  generation/error state and delivery outcome. Page size is at most 100. Recipient
  addresses are intentionally absent from API responses.

Generation failure leaves `Generating` with `generation_failed` and a recoverable
lease. Other ready reports continue to reconcile. Counts are emitted through
`TradingEngine.Api.WeeklyReports`: `tradingengine.weekly_reports.generated`,
`.failures` and `.deliveries` (bounded outcome tag). Logs contain counts and fixed
failure messages, never report bodies, recipients or source prices. Immediate alerts
use their existing independent schedule and remain unchanged.

## Durable deployment configuration

Set `WEEKLY_REPORT_SETTINGS` as a private JSON environment secret in both
`development` and `development-infrastructure-preview`. The infrastructure workflow
passes it to the secure `weeklyReportSettings` Bicep parameter and emits indexed
recipient app settings. Missing configuration means disabled. Portal-only app
settings can be replaced by infrastructure deployment; use this durable path.
Do not paste private values into a PR, workflow log or this document.

The object supports camel-case versions of the `WeeklyReports` settings:

| Property | Default / requirement |
| --- | --- |
| `enabled` | `false`; enable deliberately after migration and preview |
| `deploymentId` | Stable private identity, required when enabled; keep when paused |
| `subscriptionId` | `operator`; keep stable across restarts |
| `timeZone`, `isoDayOfWeek`, `localTime` | `Europe/London`, `7` (Sunday), `18:00` |
| `baseUrl` | HTTPS application origin, required when enabled |
| `recipientAddresses` | Private array, 1–50 unique addresses when enabled |
| `pollIntervalSeconds` | `30` (1–3,600) |
| `generationLeaseSeconds`, `claimLeaseSeconds` | `600`, `120` (30–3,600) |
| `maxDispatchAttempts`, `maxReportsPerRun` | `5` (1–20), `10` (1–100) |
| `maxSnapshotRows` | `10000` (1–100,000); overflow is a visible generation failure |
| `maxEmailRows`, `maxEmailBytes` | `100` (1–1,000), `128000` (4,096–1,000,000) |

The existing `ACS_EMAIL_CONNECTION_STRING` and `ACS_EMAIL_SENDER_ADDRESS` secrets
are required for weekly sending even if immediate alerts are disabled. Weekly
recipients are independent of `ACS_EMAIL_RECIPIENT_ADDRESSES`. The deployment
preflight rejects malformed/unbounded settings without printing values; runtime
validation remains authoritative. Azure what-if summaries redact all weekly-setting
paths and never print their values. Infrastructure PRs targeting `main` run Bicep
build/lint, synthetic configuration checks and the authenticated Azure what-if
before merging the deployment changes.

## Controlled rollout and recovery

1. Review each coherent PR against `main` and verify its diff and checks. Start
   dependent work after its prerequisite is merged. Keep weekly reporting disabled.
   Before merging the first audit-dependent query or report runtime, use
   the existing `DEPLOYMENT_HOLD` and controlled migration workflow as needed so
   application requests cannot precede `SignalTracking` / `WeeklyReportSnapshots`.
2. Apply the additive migrations from the reviewed commit via the existing database
   migration workflow. Confirm both migration history entries. Keep the migration
   order and do not roll down tables containing evidence. Deploy the reviewed build
   with weekly reporting disabled and verify the normal health/version endpoints.
3. Configure the private origin, recipient set and stable identity with
   `enabled: false`. Inspect the authenticated preview; verify signed percentages,
   original trigger prices, quote timestamps/freshness and total counts. Preview
   does not alter schedules or send. Check an archive/restore through
   `GET /api/signals/{id}/tracking`, followed by `PUT` of `{"archived": true}` (or
   `false`) with the returned `ETag` in `If-Match`; stale tokens must return 412.
4. In an isolated environment with synthetic signals and a controlled recipient,
   choose a near-future local weekly occurrence and enable through the same secret
   and infrastructure workflow. Confirm first enablement schedules that occurrence
   without historical sends. Confirm one received report, saved content/counts,
   duplicate-tick behaviour and restart reconciliation. Record evidence privately.
5. Apply the agreed private weekly schedule only after that acceptance check.
   Schedule changes reset the next unclaimed occurrence to the next future one;
   pending generation/submission retains its original evidence. Retain issue #130
   for acceptance tracking until reviewed rollout evidence exists.

To pause, deploy `enabled: false` while retaining the same identity and allow the
host to persist disabled state. In-flight external submissions may already have
been accepted; disabling cannot recall mail. Resume with the same identity to
reconcile pending work and schedule the next future occurrence. Do not change
identity to retry a failed report, delete evidence, clear operation IDs or reset
attempt counts as an ad-hoc recovery action.

For `Generating` plus `generation_failed`, inspect configuration, database health,
source validity and the snapshot bound. A corrected configuration recovers the
same report after lease expiry; no partial snapshot was made deliverable. For
`Submitted`/`Ambiguous`, preserve the operation ID and allow probes. `Sent` means
provider success, not proof of inbox delivery or reading. Terminal/exhausted
`Failed` requires investigation and an explicit audited repair design if resending
is justified. Pending reports retain their original recipient set even after
configuration changes; pause before making recipient changes that must prohibit
delivery to an old recipient and review pending evidence privately.

Monitor the weekly generated/failure/delivery metrics and recent report summaries;
verify a completed/empty report after each due cutoff. Retention/deletion of saved
reports is not automated in this version. Database growth and archive policy remain
operator decisions; never silently discard saved reporting evidence.

## Latest saved price lookup

Each signal page uses one SQL query with a correlated `TOP(1)` lookup for each
requested instrument. The query projects only the quote evidence needed by the
report. The instrument identity in the inner projection preserves `CROSS APPLY`
translation; a SQL translation regression test guards against ranking the whole
price history with `ROW_NUMBER` before filtering instruments.

`PriceObservations.EffectiveObservedAt` is a persisted computed column equal to
`COALESCE(ProviderObservedAt, RetrievedAt)`. The covering index
`IX_PriceObservations_Instrument_LatestObserved` supports the instrument equality,
observed-time upper bound and descending latest-price order. Scheduled and
retrieval timestamps must still be at or before the cutoff. Provider timestamps,
retrieval fallback, equal-time ordering and missing bid/ask values retain the same
meaning; the reader never substitutes an older quote for a missing price side.

Apply the additive `SignalQuoteLookup` migration before deploying this reader,
using the existing deployment hold and controlled migration workflow. Populating
the computed column and building its index may take schema locks on existing price
history; allow the migration to finish before resuming the app. No original price,
signal or report evidence is rewritten or deleted. The index adds storage and
maintenance work to price inserts.

A report whose generation previously failed keeps its identity, cutoff and
recipient snapshot. Once the fixed build is running, the worker retries that
report after its existing generation lease expires. Verify the preview, completed
report status and received email before treating recovery as complete; changing
the weekly schedule or creating another report identity is unnecessary.
