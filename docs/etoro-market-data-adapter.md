# eToro market-data adapter

**Status:** Implemented, read-only.  
**Scope:** Retrieves current bid/ask quotes for watched instruments through the eToro Public API. No order placement, no WebSocket subscriptions, no write endpoints.

## Architecture

`TradingEngine.Application` owns the broker-neutral port and its types:

| Type | Role |
| --- | --- |
| `IMarketDataProvider` (`Ports`) | `GetCurrentQuoteAsync(MarketInstrumentIdentity, CancellationToken) -> Result<MarketQuote>` |
| `MarketInstrumentIdentity` | Exchange, symbol and quote currency the caller wants priced |
| `MarketQuote` | Provider name, provider instrument ID, nullable `Bid`/`Ask` decimals, `ProviderObservedAt`, `RetrievedAt`, `MarketQuoteTimeliness`, `ProviderRequestId` |
| `MarketDataErrors` | `market_data.*` error catalogue |

`TradingEngine.Infrastructure.MarketData` owns the eToro adapter, its HTTP DTOs, authentication, mapping, caching and provider-specific policies. Domain is untouched: `SamplingIntervalSeconds` remains a plain per-instrument cadence, and provider rate-limit mapping lives entirely in Infrastructure.

Two instants are kept distinct: `ProviderObservedAt` is the provider's `date` field (nullable — it can be absent or delayed), while `RetrievedAt` is the local `IClock` capture when the quote was mapped.

## Verified provider contract

Sources (accessed 2026-11-04):

- eToro API reference, spec `v1.410.0` — `https://api-portal.etoro.com/api-reference/` (endpoint pages for `GET /api/v2/market-data/rates`, `GET /api/v1/market-data/search`, `GET /api/v1/market-data/exchanges`, rate-limit page).
- eToro "Find Instrument ID" guide — resolution via `internalSymbolFull`, exact-match verification, instrument-ID caching guidance.
- `etoro-builders/etoro-cursor-plugin` (official eToro GitHub material) — required headers, credential pair exclusivity, lowercase-`d` `instrumentId` in the search response.

### Requests

| Concern | Verified behaviour |
| --- | --- |
| Rates | `GET {base}/api/v2/market-data/rates?instrumentIds={csv}` — max 1000 IDs. 200 = all found, 206 = subset, 404 = none. |
| Resolution | `GET {base}/api/v1/market-data/search?internalSymbolFull={symbol}&fields={projection}` — `fields` is required; filters matching indexed instrument fields are applied. Response `{ page, pageSize, totalItems, items[] }`; a no-match search is 200 with empty `items`. |
| Headers | `x-request-id` (unique UUID, required; missing yields gateway 422 `RequestIdRequired`), plus exactly one auth method. |
| Auth | `x-api-key` + `x-user-key` pair **or** `Authorization: Bearer`; sending both is rejected. 401 uses gateway envelope `{ errorCode, errorMessage }`. |

### Response shapes used

- `rates`: `{ results: [{ instrumentId, bid, ask, date, quoteType }] }` in request order. `bid`/`ask` are nullable doubles; `date` is a nullable UTC `date-time`; `quoteType` is `realtime` or `delayed`.
- `search items[]`: `instrumentId` (lowercase `d` — the documented exception), `internalSymbolFull`, `internalExchangeId`, `internalExchangeName`, `exchangeID`.

### Read-only scope

The OpenAPI security requirement for the rates endpoint lists `etoro-public:market-data:read` (read scopes such as `etoro-public:real:read`/`demo:read` also work). The adapter sends only `GET` requests to market-data paths. Create the eToro API key with the market-data read permission only; scope enforcement is at key creation, not in code.

## Configuration

Section `EToro:MarketData` (`EToroMarketDataOptions`):

| Key | Default | Meaning |
| --- | --- | --- |
| `BaseUrl` | `https://public-api.etoro.com` | API host; the adapter appends relative paths. |
| `ApiKey` | none | eToro public API key. **Never commit.** |
| `UserKey` | none | eToro user key paired with the API key. **Never commit.** |
| `RequestTimeoutSeconds` | 5 | Per-attempt request timeout. |
| `MaxExecutionTimeSeconds` | 15 | Whole-call deadline covering retries and pacing. |
| `MaxRetryAttempts` | 2 | Retries after the first attempt on transient failures. |
| `ResolutionCacheSeconds` | 86400 | TTL for the resolved exchange+symbol+currency to `instrumentId` mapping. |
| `MaxSearchPages` | 5 | Upper bound on instrument-search pages read per resolution. |
| `ExchangeMap` | `{}` | Per-exchange allow-lists used to disambiguate search results (below). |
| `QuoteCurrencyMap` | `{}` | Per-exchange+symbol map of the provider listing's native quote currency/unit (below). |
| `QuoteCurrencyMapJson` | none | Same exchange → symbol → native-currency map expressed as one JSON object value. Used where setting names cannot carry symbol keys verbatim (Linux App Service rewrites `.` to `_` in names); see *Quote-currency verification*. Merged with `QuoteCurrencyMap`; discrete dictionary keys win per symbol. |

Set credentials through user-secrets or environment variables:

```powershell
dotnet user-secrets set "EToro:MarketData:ApiKey" "<api-key>" --project src/TradingEngine.Api
dotnet user-secrets set "EToro:MarketData:UserKey" "<user-key>" --project src/TradingEngine.Api
```

Missing credentials throw `InvalidOperationException` on first use; registration itself is safe without them.

## Instrument mapping

Resolution is explicit and verified, never guessed:

1. `internalSymbolFull` exact-match (ordinal, case-insensitive) against our `Symbol`.
2. Exchange filtering through `ExchangeMap`: each configured value either matches `internalExchangeName` (case-insensitive) or, if it parses as an integer, `internalExchangeId` or `exchangeID`.

```json
"ExchangeMap": {
  "XNAS": ["<etoro internal exchange name for Nasdaq>"],
  "XLON": ["<etoro internal exchange name for LSE>", "5"],
  "ETORO": ["<etoro internal exchange name for crypto instruments>"]
}
```

- No `ExchangeMap` entry for the instrument's exchange → `market_data.exchange_mapping_unconfigured`.
- Zero surviving candidates → `market_data.instrument_mapping_unresolved`.
- More than one surviving candidate → `market_data.instrument_mapping_ambiguous` (candidate IDs are attached as an error extension).

Search results are paginated (`pageNumber` query parameter). The adapter keeps fetching pages — inside the same quota and execution budget — until the distinct `instrumentId` count reaches `totalItems`, bounded by `MaxSearchPages`. A page returning no items or no new instrument IDs while `totalItems` remains uncovered, or exhaustion of the page bound, is a non-progressing/incomplete result → `market_data.search_pagination_incomplete`. A unique match is declared and cached only after completeness is established.

Resolved IDs are cached in-memory for `ResolutionCacheSeconds` (eToro IDs are documented as immutable). Positive results only; failures are not cached.

### Quote-currency verification

Provider responses carry no quote-currency field, so the listing's native currency/unit is established through explicit configuration rather than inferred from symbol suffixes. Every quote and readiness check resolves the unit through `INativeQuoteUnitMap` — a composite that consults the persisted `ListingConfigurations` row for the `(Exchange, Symbol)` first and falls back to the deployment map below; a disagreement between the two fails closed with `market_data.quote_currency_source_conflict` (see [price collection — per-listing native quote-unit configuration](price-collection.md#per-listing-native-quote-unit-configuration)). Administrators confirm new listings through the portal (`PUT /api/watched-instruments/{id}/listing`) without touching deployment settings; the deployment map remains the source for listings configured before the persisted model existed.

```json
"QuoteCurrencyMap": {
  "XNAS": { "MSFT": "USD" },
  "XLON": { "LLOY": "GBX" },
  "ETORO": { "BTC": "USD" }
}
```

Two configuration shapes feed the same map:

- `QuoteCurrencyMap` binds from discrete keys (`EToro:MarketData:QuoteCurrencyMap:{exchange}:{symbol}` in JSON files, user-secrets or `__`-separated environment variables). This remains the local-development shape.
- `QuoteCurrencyMapJson` is a single string value holding the whole map as a JSON object (`{"XTEST":{"DEMO.X":"GBX","DEMO_X":"USD"}}`). It exists because **Linux App Service rewrites `.` to `_` inside app-setting *names*** when surfacing them as environment variables — a flattened `…__XTEST__DEMO.X` name arrives as `…__XTEST__DEMO_X` and binds the wrong symbol key, while a JSON *value* carries each symbol identity verbatim, including dots. Setting *values* are transported losslessly on every platform.

Both shapes merge at bind time into one case-insensitive lookup; a discrete `QuoteCurrencyMap` key wins over `QuoteCurrencyMapJson` for the same exchange+symbol. Malformed `QuoteCurrencyMapJson` fails options evaluation at startup. Symbol lookup stays an exact match on the instrument's `Symbol` — no punctuation aliasing — so `DEMO.X` can never borrow `DEMO_X`'s configured currency, and the original symbol is sent to provider search unchanged.

Every request — including ones served by the resolution cache — is checked before any provider call:

- No `QuoteCurrencyMap` entry for `exchange` + `symbol` (and no persisted `ListingConfigurations` row) → `market_data.quote_currency_unconfigured`.
- Persisted row and deployment map disagree → `market_data.quote_currency_source_conflict` (`persistedCurrency`/`deploymentCurrency` attached as error extensions).
- Resolved native currency differs from the requested `QuoteCurrency` → `market_data.quote_currency_conflict` (`requestedCurrency`/`providerCurrency` attached as error extensions).

Only when the configured native currency equals the requested one does the call proceed; the `MarketQuote` currency label then describes the provider's actual listing units. No FX conversion is ever performed — a conflicting request fails rather than relabel prices.

### Unresolved mapping assumptions

- **Quote currency is not provided by the API.** Neither the search nor the rates payload carries a quote-currency field, so correctness depends entirely on `QuoteCurrencyMap` being configured accurately — see **Quote-currency verification** above. eToro symbol suffixes encode listing variants (e.g. `MSFT.EUR`), so `internalSymbolFull` exact-matching plus the configured native currency are the effective currency disambiguator.
- **`internalExchangeName` values are undocumented.** Names must be observed (e.g. by calling `/market-data/search` or `/market-data/exchanges` with a real key) before configuring `ExchangeMap`. Numeric matching against `internalExchangeId`/`exchangeID` is supported as the more stable alternative.
- **`exchangeID` vs `internalExchangeId`** semantics are not documented; numeric map entries match either field.
- **Search pagination semantics are partially documented.** `pageNumber`/`pageSize` parameters and the `page`/`pageSize`/`totalItems` response fields are documented, but whether `totalItems` counts filtered or unfiltered results, and whether pages are stable across requests, is not. The adapter treats a page that adds no new `instrumentId`s while `totalItems` is uncovered as non-progressing and fails rather than guessing.

## Rate limits, retries and sampling

Endpoint-specific (verified): all market-data endpoints share one **120 requests / 60 seconds** rolling-window quota per user key, across the 11 endpoints listed in `EToroRateLimitPolicies.MarketDataSharedEndpoints` — the rates call and every search resolution draw from the same budget. The provider emits `RateLimit-Limit`, `RateLimit-Remaining`, `RateLimit-Reset` (seconds) and `RateLimit-Policy` (`120;w=60`); 429 carries `Retry-After` (seconds).

> The general rate-limits page also advertises a looser "60/minute for most GETs" rule; the endpoint-level spec is the authoritative, stricter-in-group policy enforced here.

Enforcement and bounding:

- `ProviderRateGate` keeps a sliding-window log per quota group and delays requests until the shared window permits; a slot that cannot open before the call deadline yields `market_data.provider_throttled` with `scope = local_pacing`. Enforcement is per adapter instance in this process — multiple processes share the server-side user-key quota without coordination.
- On HTTP 429 the adapter waits `Retry-After`, else `RateLimit-Reset` seconds, else exponential backoff — **never earlier than the provider permits**. If the required wait would pass the execution deadline, the call stops and returns `provider_throttled` (`scope = provider_429`) instead of retrying.
- Transient statuses (429, 408, 5xx) and `HttpRequestException` retry up to `MaxRetryAttempts` with capped exponential backoff. Per-attempt timeout is `RequestTimeoutSeconds`; the whole call is bounded by `MaxExecutionTimeSeconds`. 5xx/network failures exhausted within the budget throw `EToroMarketDataException`; attempt timeouts surface as `TimeoutException`; caller cancellation propagates `OperationCanceledException`.
- 401/403 rejects the credential pair and throws `EToroMarketDataException` — a configuration problem, not a per-instrument outcome. Malformed JSON throws `InvalidDataException`.

### Reconciliation with `SamplingIntervalSeconds`

`WatchedInstrument.SamplingIntervalSeconds` (1–3600) stays the per-instrument demand. With one rates call per sample, `EToroRateLimitPolicies.MinimumSamplingIntervalSeconds(instrumentCount)` returns `ceiling(instrumentCount / 2)`, the smallest interval that keeps steady-state demand inside the 120/60s group budget (each resolution consumes at most one extra search call, then caches). It is advisory for #9's collector configuration — no Domain or persistence change.

## Observability

Every attempt gets a fresh `x-request-id` GUID; the successful attempt's ID is carried on `MarketQuote.ProviderRequestId` and included in `market_data.*` error extensions where relevant. Logs record request IDs, URIs (symbol and instrument-ID only), statuses and `RateLimit-Remaining` — never credentials, auth headers or raw response bodies; gateway `errorCode` values are parsed for safe diagnostics.

## Testing

`TradingEngine.Infrastructure.Tests/MarketData` covers mapping precision, missing/malformed fields, unconfigured/unresolved/ambiguous mappings, 206/404, auth failure, 429 honouring of `Retry-After`, bounded retries, per-attempt timeout, caller cancellation, cache behaviour, gate windowing, sampling-policy arithmetic and real-DI resolution — all through a scripted `HttpMessageHandler`, `FakeClock` and a recording `TimeProvider` without network access, real sleeps or credentials.

## Local smoke test

`tools/TradingEngine.MarketDataSmoke` is a manually invoked console runner. It builds the production `AddEtoroMarketData` registration, resolves the real `IMarketDataProvider` (typed `HttpClient`, real `ProviderRateGate`/`EToroInstrumentIdCache`/system clock) and issues one quote request — search + rates `GET`s only. No database access, no order endpoints, nothing written to disk. It is part of the solution build but is **not** a test project: `dotnet test` and CI never execute it, and there is no way for it to run unattended.

Configuration comes from environment variables only — no secrets file, nothing committed:

```powershell
$env:EToro__MarketData__ApiKey = "<api-key>"
$env:EToro__MarketData__UserKey = "<user-key>"
$env:EToro__MarketData__ExchangeMap__XNAS__0 = "<observed internal exchange name>"
$env:EToro__MarketData__QuoteCurrencyMap__XNAS__MSFT = "USD"
dotnet run --project tools/TradingEngine.MarketDataSmoke -- XNAS MSFT USD
```

To exercise the deployed transport shape instead of discrete keys, set the whole map as one JSON value — `EToro__MarketData__QuoteCurrencyMapJson='{"XNAS":{"MSFT":"USD"}}'` — which binds identically and keeps dotted symbols intact on any OS.

```bash
EToro__MarketData__ApiKey=<api-key> \
EToro__MarketData__UserKey=<user-key> \
EToro__MarketData__ExchangeMap__XNAS__0="<observed internal exchange name>" \
EToro__MarketData__QuoteCurrencyMap__XNAS__MSFT=USD \
dotnet run --project tools/TradingEngine.MarketDataSmoke -- XNAS MSFT USD
```

Arguments are `exchange symbol quoteCurrency` in the repository's own identity terms. `exchange` must have an `ExchangeMap` entry or the run ends with `market_data.exchange_mapping_unconfigured`, and `exchange`+`symbol` must have a `QuoteCurrencyMap` entry matching the requested currency or it ends with `market_data.quote_currency_unconfigured`/`market_data.quote_currency_conflict` — both before any request is sent. Missing credentials exit before any request is sent.

Output prints `SUCCESS` with the requested identity, `Provider`, `ProviderInstrumentId`, `Bid`/`Ask`, `Timeliness`, `ProviderObservedAt` vs `RetrievedAt` and `ProviderRequestId`; or `FAILURE` with the `market_data.*` error code, description and error extensions (or the exception type for credential/transport failures). Exit codes: `0` quote retrieved, `1` provider error, `2` usage or configuration problem. Credential values are never printed; do not paste real output payloads into source control.

Before trusting a new `ExchangeMap` entry, verify the resolved listing manually:

- `ProviderInstrumentId` should identify the intended **listing** — `internalSymbolFull` suffixes encode listing/currency variants (e.g. `MSFT.EUR`), so cross-check the symbol on eToro's portal or a manual `search` call.
- `Bid`/`Ask` are provider-native doubles in the instrument's **quote currency**; the API does not state the currency, so confirm it against eToro's UI for the instrument.
- `Timeliness` of `delayed` means the credential's entitlement receives delayed quotes for that instrument — the value itself is still usable but stale.
- All caveats under **Unresolved mapping assumptions** apply — quote currency is never provider-verified, `internalExchangeName` values are undocumented, and `exchangeID` vs `internalExchangeId` semantics are unclear.

> **Live verification status.** A manual smoke run of an earlier revision of this branch (commit `fb59138`, before the review fixes) was performed locally by Jason with read-only credentials: the search and rates calls returned HTTP 200 and the runner printed `SUCCESS` for the queried instrument. That run validated the request/response plumbing and authentication shape against the real `public-api.etoro.com`, but it does **not** cover the subsequently added pagination, quote-currency verification or deadline-enforcement changes, and no Azure-hosted or deployment-context verification has been performed.
