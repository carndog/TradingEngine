# Exchange calendars and request-time eligibility

Story #106 extends `IMarketSessionPolicy` with an `IExchangeTradingCalendar` daily-schedule port. `CalendarMarketSessionPolicy` combines the configured weekly hours with a versioned exchange calendar. All dates are exchange-local NodaTime `LocalDate` values derived from the supplied `Instant`.

## Included coverage

| Exchange | Time zone | Verified dates, inclusive | Regular-session collection |
| --- | --- | --- | --- |
| XLON | Europe/London | 2026-11-16 through 2027-12-31 | Weekdays 08:00–16:30; published half days 08:00–12:30 |
| XNAS | America/New_York | 2026-01-01 through 2026-12-31 | Weekdays 09:30–16:00; published half days 09:30–13:00 |
| Explicit AlwaysOpen session | Existing configuration | Calendar-independent | Existing continuous collection |

Sources were verified on 2026-11-16. Nasdaq's official calendar currently publishes 2026. **XNAS collection is blocked outside this verified coverage until a verified replacement is deployed.** NYSE dates are not silently substituted for Nasdaq dates. The provider emits a warning within 60 calendar days of coverage ending, at most once per exchange/version/local date while the process remains running. The collection-status response also exposes the coverage notice.

Official sources:

- [LSE business days](https://www.londonstockexchange.com/en/resources/markets-products/business-days).
- [LSE Millennium Exchange and TRADEcho business parameters](https://www.londonstockexchange.com/content/dam/lse/en_us/documents/technical-library/millennium-exchange-and-tradecho-business-parameters.xlsx), `Trading Cycles` and `Trading Service Breakdown` sheets inspected for the regular session and closing auction boundary.
- [Nasdaq trading calendar](https://www.nasdaqtrader.com/trader.aspx?id=Calendar).
- [Nasdaq Thanksgiving 2026 session notice](https://www.nasdaqtrader.com/TraderNews.aspx?id=ETA2026-69).

The policy preserves the existing regular-session collector: open-inclusive, close-exclusive. For London SETS, the 16:30 normal/12:30 half-day cutoff is the start of the closing-auction process, not the end of every LSE trading service. The collector does not add closing-auction or post-close sessions. Nasdaq extended-hours trading is likewise excluded. AlwaysOpen identifies a configured session; a broker name or ticker is not an asset-class classifier.

## Batch and suspension behaviour

`CollectDuePricesHandler` checks eligibility before preparing instruments and again immediately before each batch at the provider's configured `MaxBatchSize`. One `PriceCollectionRequestGuard` checks each distinct exchange once per fresh-clock evaluation, together with the run deadline and each pending identity's slot expiry. It shares the decision only within that evaluation. Eligible instruments from different exchanges may share a transport request.

The eToro adapter carries the guard through identity searches, discovered-identity sub-batches, rates requests and HTTP retries. It invokes the guard before rate pacing and again after the awaited rate gate, immediately before constructing the outbound HTTP request. It rebuilds the requested provider IDs after removing newly blocked identities. A wholly blocked batch makes no HTTP call. Rate-gate reservations can remain conservatively consumed if eligibility changes during its wait; diagnostics count only actual HTTP attempts.

A paused process uses the current clock on resumption. It does not rely on a midnight callback or cached `IsOpen` flag. An expired run or slot cannot send a new quote request, including for AlwaysOpen instruments. The following poll recalculates due slots from durable observations/attempts. Returned quotes retain the existing slot-expiry checks; a valid in-session observation is not discarded solely because its response arrives after closing. Current quotes never backfill old sampling slots.

The default single-quote provider fallback checks the guard before each call. Any new adapter that waits or retries internally must honour the supplied guard at each actual request boundary, as the eToro implementation does.

Calendar changes and network dispatch cannot be one atomic operation. The policy is evaluated immediately before dispatch; it does not cancel an HTTP request already in flight merely because the exchange subsequently closes.

## Configuration and maintenance

The reviewed JSON dataset is embedded in `TradingEngine.Infrastructure/MarketSessions/exchange-calendars.json` and travels with the deployed application. Infrastructure redeployment cannot erase this packaged default. No new Azure setting is required for this delivery.

`TradingCalendars:DefinitionsJson` can replace the complete dataset through the normal configuration providers. It is a JSON array with the same schema as the packaged file, not a partial merge. Include every calendar the deployment requires. For Azure environment configuration, the key is `TradingCalendars__DefinitionsJson`; persist such an override through the deployment's established configuration mechanism rather than a one-off portal edit. App-setting changes require the platform's normal restart; reloadable file providers are also supported.

For each definition supply `exchange`, `version`, `timeZone`, inclusive `coverageStart` and `coverageEnd`, `verifiedOn`, HTTPS `sources`, `exceptions` and optional `overrides`. A day entry has an ISO `date`, a `reason`, and either `closed: true` or ordered, non-overlapping `windows` with `open`/`close` in `HH:mm`. Replacement windows belong entirely to that exchange-local date; overnight replacement windows are rejected. Unknown fields and null required metadata are rejected.

Explicit overrides replace the published exception for that date. A closure override replaces all windows and is classified as `exceptional_closure`; an explicit replacement-session override supplies that date's entire schedule. Duplicates within the exceptions list or within the overrides list are rejected. A day cannot simultaneously declare closure and windows. Validate time zones, coverage, duplicate exchanges, dates and window ordering before the snapshot is accepted.

To maintain the calendar:

1. Check the relevant exchange publication and notices; verify the service-specific session boundaries.
2. Extend or replace only the verified coverage, update source links, verification date and version, and add applicable exceptions/overrides.
3. Run the calendar, batch and HTTP tests, then the full solution build/tests.
4. Deploy the reviewed dataset/configuration and verify the logged exchange versions and collection-status reasons.

Reload builds and validates a complete replacement snapshot before swapping it. There is no derived day cache to invalidate; every subsequent lookup uses the new snapshot, including an override added during the same date. Invalid initial data fails resolution. Invalid reloads retain the last valid snapshot only within its verified coverage and expose a refresh-failure notice; expired or missing coverage never falls back to assumed-open weekday hours.

XLON and XNAS automatically require calendar coverage. Other non-continuous sessions can opt in with `PriceCollection:ExchangeSessions:<exchange>:CalendarRequired`. AlwaysOpen takes precedence over calendar gating while preserving all other collection safeguards.

## Outcomes and deployment verification

Known closures keep `SkippedMarketClosed`, with `price_collection.holiday`, `price_collection.exceptional_closure`, `price_collection.outside_replacement_session` or `price_collection.session_closed`. Missing/expired coverage uses `price_collection.calendar_unavailable` and the distinct `calendar-unavailable` collection status. Existing per-slot outcome deduplication and append-only history remain in place. Calendar versions appear in diagnostic load logs and session decisions. No schema migration or history repair is needed.

After an approved development deployment, verify the application revision, logged packaged/override calendar versions and representative authenticated collection statuses. Confirm eligible instruments still produce observations across ordinary slots, with AlwaysOpen collection continuing independently. Prove holiday, early-close, restart and suspension behaviour with injected-clock synthetic tests; never change the deployed clock or disable live session gating to manufacture evidence. This story does not add order execution or change notification delivery semantics.
