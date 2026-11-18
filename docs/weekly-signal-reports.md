# Weekly signal reports

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
the disabled-by-default host are added in the next slices.

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
