# Signal price comparison

The first slice of [story #130](https://github.com/carndog/TradingEngine/issues/130)
implements one pure Domain operation: compare a recorded signal's original price
and range with a later saved quote. It introduces no endpoint, migration,
scheduler, email delivery or change to immediate alerts.

## Inputs and responsibility

`SignalPriceComparisonCalculator.Compare` accepts a `SignalPriceBaseline` and an
optional existing `PriceObservation`. Supply the baseline from the original
notification evidence: its trigger time, selected price basis, quote currency,
and the match's recorded `CurrentPrice`, `Lower` and `Upper`. Do not use the
current rule, a range midpoint, or a later email delivery timestamp as the
baseline. A trigger may lie outside its original range.

The caller is responsible for selecting the same instrument's latest eligible
saved observation at or before the report cutoff, excluding future or
post-cutoff retrievals. The calculator neither loads prices nor reads a clock.
The later query/report slices own observation selection, quote timestamps and
age, calendar-aware freshness, revision status and tracking/archive state.

## Meaning of the result

An available result contains the selected latest price, `PercentageChange` and
`RangePosition`. The percentage is `(latest - trigger) / trigger * 100`, using
the original signal's basis throughout: Bid selects Bid and Ask selects Ask.
It is signed price movement, not trading profit or a direction-adjusted return.

| Latest price versus original bounds | Range position |
| --- | --- |
| Below Lower | Below |
| Equal to Lower through equal to Upper | Within |
| Above Upper | Above |

This range classification describes location only. It does not re-evaluate a
buy-zone crossing, declare a signal successful/failed, or close a position.

Arithmetic uses decimal throughout, with no display rounding. Multiplication
precedes division when safe to preserve small representable percentage changes;
large differences use division first to avoid unnecessary intermediate overflow.
A percentage outside decimal's range is unavailable. Formatting/rounding is the
presentation layer's responsibility; normal decimal precision still applies.

## Unavailable comparisons

An unavailable result has `IsAvailable = false`, null numeric/range fields and
an explicit `UnavailableReason`. It must not be presented as a zero movement.

- Undefined basis, non-positive trigger price, non-positive/inverted/equal
  original bounds, or missing baseline currency invalidate the baseline.
- A missing quote or an observation at/before the trigger is `NoLaterObservation`.
- Currency/native-unit identifiers must match exactly. GBP and GBX are distinct;
  this operation does not convert units or normalise currency identifiers.
- A missing required side is `MissingRequiredQuoteSide`; the other side is never
  substituted. Latest observations retain their existing positive-price validation.
- Arithmetic overflow is `PercentageOutOfRange`, rather than an exception that
  could interrupt the later batch report.

Display-unit preferences never enter this calculation. The caller must supply
both prices and bounds in compatible canonical stored units. No fee, FX,
dividend or corporate-action adjustment is implied. All tests use synthetic
prices and fixed timestamps.
