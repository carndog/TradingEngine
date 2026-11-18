# Recorded signal status

`GET /api/signals` reads existing notification matches without re-evaluating rules,
calling market data or resending alerts. One match ID remains one signal; several
matches in an alert and later re-entries stay separate. Failed/pending original
emails are included. Soft deletion and paused monitoring preserve signal history.

Query parameters: optional `instrumentId`, inclusive `triggeredFrom`, exclusive
`triggeredTo`, `cutoff` (UTC ISO timestamp), `page` (default 1), `pageSize` (1–100,
default 25). Responses include a fixed UTC cutoff and `hasMore`; reuse that cutoff
for subsequent pages. `GET /api/signals/{matchId}` returns one signal or 404.
Both endpoints use the application's existing authenticated API middleware.

Signals must have been recorded by the cutoff. The latest saved quote must have
its slot, retrieval and provider timestamp (if supplied) at/before the cutoff.
Selection orders by provider time, falling back to retrieval time, then slot and
stable ID. It never substitutes the other quote side or backfills an older bid.
Each page uses batched evidence, latest-price and original-revision queries.

The percentage and range use immutable original match evidence. The independent
monitoring/removal flags describe configuration when read; revision activity is
checked at the cutoff. They do not rewrite the original signal. Quote times and
age accompany the derived values. All numeric examples in tests are synthetic.

Freshness allows one configured sampling interval for collection and checks the
most recent expected open slot through the existing exchange-session/calendar
policy. Friday's last expected slot remains valid through a closed weekend or
holiday. A missed open slot is `stale_last_known`; missing calendar coverage is
`unknown_calendar`. Search is bounded to 14 days/20,160 slots; exhaustion is
unknown, never silently fresh. Invalid cadence is also unknown. This describes
saved quote freshness, not an instruction to trade. See
[the calculation contract](signal-price-comparison.md) for percentage semantics.
