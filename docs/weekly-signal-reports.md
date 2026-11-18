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
