# Chart-condition evaluation

**Status:** Evaluator implemented by `ChartAnalysisEvaluator` in `TradingEngine.Domain`. Orchestration, alert composition, durable storage and delivery are proposed in issue [#11](https://github.com/carndog/TradingEngine/issues/11) and are not implemented.

**Related issues:** [#10](https://github.com/carndog/TradingEngine/issues/10), [#11](https://github.com/carndog/TradingEngine/issues/11)

## Purpose

`ChartAnalysisEvaluator` determines which configured chart conditions occurred between two price observations under an explicitly selected monitoring-rule revision. It is a pure domain function: it performs no database, XML, network, notification or system-clock access and holds no mutable state, so live monitoring and historical replay use the same code path.

The evaluator does not decide what to do with a triggered condition. It produces typed evidence — the exact revision evaluated, the observation instants, the price basis, the quote context and the matched conditions — that later Application and Infrastructure work may persist and dispatch.

## Implemented types

All types live in `TradingEngine.Domain.MonitoringRules`.

| Type | Shape | Role |
| --- | --- | --- |
| `ChartAnalysisEvaluator` | `sealed class` | Stateless evaluator. `Evaluate(EvaluationInput)` returns `Result<EvaluationResult>`. |
| `EvaluationInput` | record | `MonitoringRuleRevisionId`, `Definition` (a validated `ChartAnalysisDefinition`), optional `Previous` observation, required `Current` observation and the `PriceBasis` to compare on. |
| `PriceObservation` | record | `ObservedAt` instant, nullable `Bid` and `Ask`, `QuoteCurrency`. `Create` requires a quote currency, at least one quote side and positive prices. |
| `PriceBasis` | enum | `Bid` or `Ask`. |
| `EvaluationResult` | record | Revision identifier, `ObservedAt`, `PreviousObservedAt`, `Outcome`, `Reason`, `PriceBasis`, `QuoteCurrency`, current `Bid`/`Ask` and ordered `Matches`. `Matches` is a defensive read-only copy. |
| `EvaluationOutcome` | enum | `Triggered` or `NoAction`. |
| `EvaluationReason` | enum | `ConditionsTriggered`, `FirstObservation`, `NoConfiguredZones`, `NoConditionTriggered`. |
| `TriggeredCondition` | record | `ZoneId`, `Condition`, `ActionId`, the zone `Lower`/`Level`/`Upper` evaluated and the basis-side `PreviousPrice`/`CurrentPrice`. |
| `ChartEvaluationErrors` | static class | `chart_evaluation.*` error catalogue. |

`ChartConditionAlertComposer`, `ChartConditionAlert`, `SpreadSnapshot`, `EvaluationContext`, `IEvaluationStore`, `IAlertStore`, `IEmailSender` and `AlertEmailRenderer` remain proposed integration types owned by issue #11; they are not part of this implementation.

## Input selection

Application owns choosing which revision to evaluate. The agreed rule: select the committed revision effective at the current observation instant and pass its identifier and validated `ChartAnalysisDefinition` in the `EvaluationInput`. The evaluator never queries for the active revision.

`PriceObservation.ObservedAt` is the instant that defines chronology. The intended live mapping is the collected observation's `ScheduledFor` slot — the same slot identity SQL enforces — so live evaluation and replayed evaluations compare the same ordered pair. `Instant` values arrive fully populated; the evaluator reads no clock.

## Price basis

`PriceBasis` makes the compared quote side explicit. `Ask` is the buy-side quote; `Bid` is the sell-side quote. The basis applies uniformly to every zone and condition in the evaluation: a `buy-zone` condition does not imply an ask comparison, and zone levels keep the basis under which they were configured. The evaluator never substitutes the other quote side.

- If the basis side is missing on `Current` — or on `Previous` when one is supplied — evaluation fails with `chart_evaluation.quote_side_missing`.
- A missing opposite side is allowed: evaluation proceeds on the basis side and the result preserves whichever quote sides were observed.

## Condition semantics

Conditions follow the canonical contract in [Chart-analysis definition XML](chart-analysis-definition-xml.md#condition-semantics). Comparisons are exact `decimal` operations with no display rounding.

| Condition | Zone kind | Transition | Fires when |
| --- | --- | --- | --- |
| `buy-zone` | Support | Entering from above | `previous > upper` and `level < current <= upper` |
| `support-loss` | Support | Leaving below | `previous >= lower` and `current < lower` |
| `breakout` | Resistance | Leaving above | `previous <= upper` and `current > upper` |

In transition terms: a price below `lower` is below the zone, `(level, upper]` is the buy-zone band, `[lower, upper]` is within the zone, and above `upper` is above. Boundary equality is inclusive on the previous side and on `buy-zone`'s entry bound: a previous observation exactly at `lower` or `upper` still counts as being on that boundary and can cross on the next sample — `previous == lower` followed by `current < lower` fires `support-loss`, and `previous == upper` followed by `current > upper` fires `breakout` — while a current observation exactly at `lower` or `upper` does not itself constitute a loss or breakout. `buy-zone` fires when the current price lands exactly on `upper`.

Each zone contributes at most one match per evaluation because the three conditions partition the transition space. A price that jumps across a whole zone in one step fires only the condition for the observed endpoints — for example `above -> below` a support zone fires `support-loss`, never `buy-zone` — because no unobserved intermediate path is inferred.

## Outcomes and failure separation

A successful `Result<EvaluationResult>` is one of:

| Outcome | Reason | Meaning |
| --- | --- | --- |
| `Triggered` | `ConditionsTriggered` | One or more matches, ordered globally across all zones by `lower` then `id` (ordinal). This is an evaluation ordering, not the canonical XML document order — the XML format groups support zones before resistance zones separately. |
| `NoAction` | `NoConfiguredZones` | The supplied definition configures no zones — a valid price-only definition with empty support and resistance collections. Nothing can trigger, so the result carries no matches but still carries the revision and observation evidence. |
| `NoAction` | `FirstObservation` | `Previous` is null; the observation establishes a baseline and cannot produce a crossing. `NoConfiguredZones` takes precedence when both apply: with no zones configured there is nothing to evaluate the baseline against. |
| `NoAction` | `NoConditionTriggered` | Both prices evaluated; nothing crossed. `NoAction` carries the same evidence fields as `Triggered`, with empty `Matches`. |

Unusable input is a failed `Result`, never a `NoAction` — so a later store cannot advance a checkpoint past work that did not evaluate:

| Code | Returned when |
| --- | --- |
| `chart_evaluation.revision_id_required` | `MonitoringRuleRevisionId` is `Guid.Empty`. |
| `chart_evaluation.price_basis_undefined` | `PriceBasis` is not a defined member. |
| `chart_evaluation.observations_out_of_order` | `Previous.ObservedAt` equals or follows `Current.ObservedAt` — duplicate slots and inverted chronology share this failure. |
| `chart_evaluation.quote_currency_mismatch` | `Previous` and `Current` quote currencies differ (ordinal comparison); mixing native units is rejected rather than reconciled. |
| `chart_evaluation.quote_side_missing` | The selected basis side is absent on a supplied observation. |
| `chart_evaluation.quote_currency_required` | `PriceObservation.Create` was given a missing quote currency. |
| `chart_evaluation.quote_side_required` | `PriceObservation.Create` was given neither bid nor ask. |
| `chart_evaluation.price_not_positive` | `PriceObservation.Create` was given a non-positive bid or ask. |

Null `EvaluationInput`, `Definition` or `Current` is a programming defect and throws `ArgumentNullException` rather than returning a `Result`.

## Deliberate behaviours

- **First observation.** `Previous: null` returns `FirstObservation`; no crossing can be claimed without a baseline.
- **Price-only definitions.** A definition with no configured zones is valid input. Evaluation returns `NoConfiguredZones` with full evidence, taking precedence over `FirstObservation` when there is also no previous observation.
- **Repetition.** Remaining within a zone emits nothing — `within -> within`, `above -> above` and repeated identical prices are `NoConditionTriggered`. A later genuine re-entry (for example `above -> within` once more) triggers again, because only the supplied pair is compared and no state is held between calls.
- **Sample gaps.** Elapsed time between the two observations is not inspected: there is no maximum gap and no interpolation of the price path. Gap detection is an orchestration concern, not a chart-condition concern.
- **Revision changes.** The evaluator sees exactly one revision. The previous observed price remains the baseline even when the supplied revision differs from the one applied to earlier observations; the result identifies the revision that was evaluated. A caller that wants a fresh baseline across a revision boundary supplies `Previous: null`.
- **Chronology.** `Previous` must be strictly earlier than `Current`. Duplicate timestamps cannot silently widen a transition, and inverted input is rejected rather than reordered.
- **Determinism.** Identical inputs produce identical results: matches are sorted globally by `(lower, id)` independent of declaration order, and the evaluator keeps no mutable state.

## Spread and alert context

Spread is contextual evidence for later alerts, not a filter: no spread threshold exists and none is applied to chart-condition detection. `EvaluationResult` preserves `QuoteCurrency`, `CurrentBid` and `CurrentAsk` so the proposed #11 composer can build a `SpreadSnapshot` — including the proposed `(Ask - Bid) / Ask * 100` percentage-of-purchase-price convention — without re-fetching quotes. A missing quote side stays `null`; unavailable is not zero.

## Future integration contract

Issue #11 owns what happens to these results: persisting triggered alerts and pending notification intents (not every evaluation or `NoAction`), saving alert intent and processing checkpoint atomically, dispatching notifications separately from durable work, deduplicating intents with stable identity, recovering pending work after restart and treating ambiguous provider sends without promising exactly-once delivery. The separation this document describes — failed `Result` versus successful `NoAction` — is what lets that orchestration record progress honestly.

## Boundary

The evaluator is generic public infrastructure: it mechanically applies the documented condition contract to caller-supplied definitions. Real zone levels, instrument selections, meaningful parameters, generated alerts and private research remain private under [PUBLIC_SCOPE.md](../PUBLIC_SCOPE.md); tests and examples here use only synthetic fixtures.
