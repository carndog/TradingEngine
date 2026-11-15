using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Infrastructure.MarketData;

public sealed class EToroMarketDataProvider : IMarketDataProvider
{
    internal const string ProviderName = "eToro";

    private const string SearchPath = "api/v1/market-data/search";
    private const string RatesPath = "api/v2/market-data/rates";
    private const string SearchFields =
        "instrumentId,internalSymbolFull,internalExchangeId,internalExchangeName," +
        "exchangeID,internalInstrumentDisplayName";
    private const string RequestIdHeader = "x-request-id";
    private const string ApiKeyHeader = "x-api-key";
    private const string UserKeyHeader = "x-user-key";

    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly System.Threading.Lock _statisticsGate = new();
    private readonly Dictionary<int, long> _rateBatches = [];
    private long _searchRequests;
    private readonly HttpClient _httpClient;
    private readonly EToroMarketDataOptions _options;
    private readonly ProviderRateGate _rateGate;
    private readonly EToroInstrumentIdCache _instrumentIdCache;
    private readonly INativeQuoteUnitMap _quoteUnits;
    private readonly IClock _clock;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EToroMarketDataProvider> _logger;

    public EToroMarketDataProvider(
        HttpClient httpClient,
        IOptions<EToroMarketDataOptions> options,
        ProviderRateGate rateGate,
        EToroInstrumentIdCache instrumentIdCache,
        INativeQuoteUnitMap quoteUnits,
        IClock clock,
        TimeProvider timeProvider,
        ILogger<EToroMarketDataProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.BatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(_options.BatchSize, EToroMarketDataOptions.MaximumBatchSize);
        _rateGate = rateGate ?? throw new ArgumentNullException(nameof(rateGate));
        _instrumentIdCache = instrumentIdCache
            ?? throw new ArgumentNullException(nameof(instrumentIdCache));
        _quoteUnits = quoteUnits ?? throw new ArgumentNullException(nameof(quoteUnits));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int MaxBatchSize => _options.BatchSize;

    public MarketDataRequestStatistics RequestStatistics
    {
        get
        {
            lock (_statisticsGate)
            {
                return new MarketDataRequestStatistics(_searchRequests, new Dictionary<int, long>(_rateBatches));
            }
        }
    }

    private void RecordRequest(string relativeUri, string requestId)
    {
        bool rates = relativeUri.StartsWith(RatesPath, StringComparison.Ordinal);
        int size = rates ? relativeUri.Count(character => character == ',') + 1 : 0;
        lock (_statisticsGate)
        {
            if (rates)
            {
                _rateBatches[size] = _rateBatches.GetValueOrDefault(size) + 1;
            }
            else
            {
                _searchRequests++;
            }
        }

        _logger.LogDebug("eToro {RequestKind} request {RequestId} started with {BatchSize} instrument IDs.",
            rates ? "rates" : "search", requestId, size);
    }

    public async Task<IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>>> GetCurrentQuotesAsync(
        IReadOnlyCollection<MarketInstrumentIdentity> instruments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(instruments.Count, MaxBatchSize);
        cancellationToken.ThrowIfCancellationRequested();

        Dictionary<MarketInstrumentIdentity, Result<MarketQuote>> results = [];
        if (instruments.Count == 0)
        {
            return results;
        }

        RequireCredentials();
        Instant deadline = _clock.GetCurrentInstant()
            .Plus(Duration.FromTimeSpan(MaxExecutionTime()));
        Dictionary<MarketInstrumentIdentity, long> resolved = [];
        List<MarketInstrumentIdentity> undiscovered = [];
        foreach (MarketInstrumentIdentity instrument in instruments.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            Error? error = await ValidateBatchInstrumentAsync(instrument, cancellationToken);
            if (error is not null)
            {
                results[instrument] = error;
            }
            else if (_instrumentIdCache.TryGet(
                instrument.Exchange, instrument.Symbol, instrument.QuoteCurrency, out long instrumentId))
            {
                resolved[instrument] = instrumentId;
            }
            else
            {
                undiscovered.Add(instrument);
            }
        }

        if (resolved.Count > 0)
        {
            Dictionary<MarketInstrumentIdentity, Result<MarketQuote>> quotes =
                await FetchResolvedQuotesAsync(resolved, deadline, cancellationToken);
            foreach (KeyValuePair<MarketInstrumentIdentity, Result<MarketQuote>> quote in quotes)
            {
                results[quote.Key] = quote.Value;
            }
        }

        Error? paused = results.Values
            .Where(result => result.IsFailure && PausesDiscovery(result.Error))
            .Select(result => result.Error)
            .FirstOrDefault();
        foreach (MarketInstrumentIdentity instrument in undiscovered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (paused is not null)
            {
                results[instrument] = paused;
                continue;
            }

            Result<long> identity = await ResolveBatchInstrumentAsync(instrument, deadline, cancellationToken);
            if (identity.IsFailure)
            {
                results[instrument] = BatchPacingError(identity.Error);
            }
            else
            {
                Dictionary<MarketInstrumentIdentity, long> discovered = new() { [instrument] = identity.Value };
                Dictionary<MarketInstrumentIdentity, Result<MarketQuote>> quotes =
                    await FetchResolvedQuotesAsync(discovered, deadline, cancellationToken);
                results[instrument] = quotes[instrument];
            }

            if (results[instrument].IsFailure && PausesDiscovery(results[instrument].Error))
            {
                paused = results[instrument].Error;
            }
        }

        return results;
    }

    private async Task<Dictionary<MarketInstrumentIdentity, Result<MarketQuote>>> FetchResolvedQuotesAsync(
        IReadOnlyDictionary<MarketInstrumentIdentity, long> resolved,
        Instant deadline,
        CancellationToken cancellationToken)
    {
        string ids = string.Join(
            ",", resolved.Values.Distinct().Select(id => id.ToString(CultureInfo.InvariantCulture)));
        EToroProviderResponse response;
        try
        {
            response = await SendWithBoundedRetriesAsync(
                $"{RatesPath}?instrumentIds={ids}", deadline, cancellationToken);
        }
        catch (TimeoutException)
        {
            return BatchFailures(resolved, MarketDataErrors.ProviderTimeout);
        }
        catch (Exception exception) when (exception is EToroMarketDataException
            or InvalidDataException or HttpRequestException)
        {
            return BatchFailures(resolved, MarketDataErrors.ProviderUnavailable);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw ProviderFailure("retrieve instrument rates", response);
        }

        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.PartialContent)
        {
            EToroRatesResponse payload;
            try
            {
                payload = Deserialize<EToroRatesResponse>(response.Body, "rates");
            }
            catch (InvalidDataException)
            {
                return BatchFailures(resolved, MarketDataErrors.ProviderUnavailable);
            }

            Dictionary<long, EToroRate[]> rates = (payload.Results ?? [])
                .Where(rate => rate.InstrumentId.HasValue)
                .GroupBy(rate => rate.InstrumentId!.Value)
                .ToDictionary(group => group.Key, group => group.ToArray());
            Dictionary<MarketInstrumentIdentity, Result<MarketQuote>> results = [];
            foreach (KeyValuePair<MarketInstrumentIdentity, long> entry in resolved)
            {
                if (rates.TryGetValue(entry.Value, out EToroRate[]? matches) is false)
                {
                    results[entry.Key] = MarketDataErrors.PriceUnavailable;
                }
                else if (matches.Length != 1)
                {
                    results[entry.Key] = MarketDataErrors.ProviderUnavailable;
                }
                else
                {
                    results[entry.Key] = MapRate(entry.Key, entry.Value, matches[0], response);
                }
            }

            return results;
        }

        Error error = response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => BatchPacingError(Throttled(response)),
            HttpStatusCode.NotFound => MarketDataErrors.PriceUnavailable,
            _ => MarketDataErrors.ProviderUnavailable
        };
        return BatchFailures(resolved, error);
    }

    private static Dictionary<MarketInstrumentIdentity, Result<MarketQuote>> BatchFailures(
        IReadOnlyDictionary<MarketInstrumentIdentity, long> resolved,
        Error error)
    {
        return resolved.Keys.ToDictionary(identity => identity, _ => Result<MarketQuote>.Failure(error));
    }

    private static Error BatchPacingError(Error error)
    {
        return error.Code == MarketDataErrors.ProviderThrottled.Code
            && error.Extensions?.TryGetValue("scope", out object? scope) == true
            && scope is "local_pacing"
                ? MarketDataErrors.ProviderRequestDeferred
                : error;
    }

    private static bool PausesDiscovery(Error error)
    {
        return error.Code == MarketDataErrors.ProviderRequestDeferred.Code
            || error.Code == MarketDataErrors.ProviderThrottled.Code;
    }

    private async Task<Error?> ValidateBatchInstrumentAsync(
        MarketInstrumentIdentity instrument,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CheckQuoteCurrencyAsync(instrument, cancellationToken);
        }
        catch (Exception exception) when (exception is TimeoutException
            or EToroMarketDataException or InvalidDataException or HttpRequestException)
        {
            return MarketDataErrors.ProviderUnavailable;
        }
    }

    private async Task<Result<long>> ResolveBatchInstrumentAsync(
        MarketInstrumentIdentity instrument,
        Instant deadline,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ResolveInstrumentIdAsync(instrument, deadline, cancellationToken);
        }
        catch (Exception exception) when (exception is TimeoutException
            or EToroMarketDataException or InvalidDataException or HttpRequestException)
        {
            return MarketDataErrors.ProviderUnavailable;
        }
    }

    public async Task<Result<MarketQuote>> GetCurrentQuoteAsync(
        MarketInstrumentIdentity instrument,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        RequireCredentials();

        Error? currencyError = await CheckQuoteCurrencyAsync(instrument, cancellationToken);
        if (currencyError is not null)
        {
            return currencyError;
        }

        Instant deadline = _clock.GetCurrentInstant()
            .Plus(Duration.FromTimeSpan(MaxExecutionTime()));

        Result<long> resolved = await ResolveInstrumentIdAsync(
            instrument,
            deadline,
            cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        return await GetRateQuoteAsync(instrument, resolved.Value, deadline, cancellationToken);
    }

    public async Task<Result<MarketListing>> ResolveListingAsync(
        MarketListingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (CredentialsConfigured() is false)
        {
            return MarketDataErrors.ProviderCredentialsUnconfigured;
        }

        if (_options.ExchangeMap.TryGetValue(query.Exchange, out string[]? mappings) is false
            || mappings.Length == 0)
        {
            return MarketDataErrors.ExchangeMappingUnconfigured
                .WithExtension("exchange", query.Exchange)
                .WithExtension("symbol", query.Symbol);
        }

        Instant deadline = _clock.GetCurrentInstant()
            .Plus(Duration.FromTimeSpan(MaxExecutionTime()));

        Result<List<EToroSearchItem>> search;
        try
        {
            search = await SearchAllPagesAsync(
                query.Exchange,
                query.Symbol,
                deadline,
                cancellationToken);
        }
        catch (TimeoutException exception)
        {
            return Unavailable(query, exception);
        }
        catch (EToroMarketDataException exception)
        {
            return Unavailable(query, exception);
        }
        catch (InvalidDataException exception)
        {
            return Unavailable(query, exception);
        }
        catch (HttpRequestException exception)
        {
            return Unavailable(query, exception);
        }

        if (search.IsFailure)
        {
            return search.Error;
        }

        EToroSearchItem[] matches = ResolveMatches(
            query.Exchange,
            query.Symbol,
            search.Value,
            mappings);
        if (matches.Length == 0)
        {
            return MarketDataErrors.InstrumentMappingUnresolved
                .WithExtension("exchange", query.Exchange)
                .WithExtension("symbol", query.Symbol);
        }

        if (matches.Length > 1)
        {
            return MarketDataErrors.InstrumentMappingAmbiguous
                .WithExtension("exchange", query.Exchange)
                .WithExtension("symbol", query.Symbol)
                .WithExtension(
                    "candidateInstrumentIds",
                    matches.Select(match => match.InstrumentId!.Value).ToArray());
        }

        EToroSearchItem match = matches[0];
        return new MarketListing(
            ProviderName,
            match.InstrumentId!.Value,
            match.InternalSymbolFull ?? query.Symbol,
            match.InternalExchangeName,
            match.InternalExchangeId ?? match.ExchangeId,
            match.InternalInstrumentDisplayName,
            null);
    }

    private async Task<Result<long>> ResolveInstrumentIdAsync(
        MarketInstrumentIdentity instrument,
        Instant deadline,
        CancellationToken cancellationToken)
    {
        if (_instrumentIdCache.TryGet(
                instrument.Exchange,
                instrument.Symbol,
                instrument.QuoteCurrency,
                out long cachedId))
        {
            return cachedId;
        }

        if (_options.ExchangeMap.TryGetValue(instrument.Exchange, out string[]? mappings) is false
            || mappings.Length == 0)
        {
            return MarketDataErrors.ExchangeMappingUnconfigured
                .WithExtension("exchange", instrument.Exchange)
                .WithExtension("symbol", instrument.Symbol);
        }

        Result<List<EToroSearchItem>> search = await SearchAllPagesAsync(
            instrument.Exchange,
            instrument.Symbol,
            deadline,
            cancellationToken);
        if (search.IsFailure)
        {
            return search.Error;
        }

        return ResolveFromSearch(instrument, search.Value, mappings);
    }

    private async Task<Result<List<EToroSearchItem>>> SearchAllPagesAsync(
        string exchange,
        string symbol,
        Instant deadline,
        CancellationToken cancellationToken)
    {
        List<EToroSearchItem> items = [];
        HashSet<long> observedIds = [];
        int totalItems = int.MaxValue;

        for (int page = 1; page <= _options.MaxSearchPages; page++)
        {
            EToroProviderResponse response = await SendWithBoundedRetriesAsync(
                SearchUri(symbol, page, _options.SearchPageSize),
                deadline,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return Throttled(response);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw ProviderFailure("search instruments", response);
            }

            EToroSearchResponse pageResult =
                Deserialize<EToroSearchResponse>(response.Body, "search");
            List<EToroSearchItem> pageItems = pageResult.Items ?? [];
            totalItems = pageResult.TotalItems;

            int newObserved = 0;
            foreach (EToroSearchItem item in pageItems)
            {
                if (item.InstrumentId.HasValue
                    && observedIds.Add(item.InstrumentId.Value))
                {
                    newObserved++;
                }
            }

            items.AddRange(pageItems);

            if (observedIds.Count >= totalItems)
            {
                break;
            }

            if (pageItems.Count == 0 || newObserved == 0)
            {
                break;
            }
        }

        if (observedIds.Count < totalItems)
        {
            return MarketDataErrors.SearchPaginationIncomplete
                .WithExtension("exchange", exchange)
                .WithExtension("symbol", symbol)
                .WithExtension("totalItems", totalItems)
                .WithExtension("distinctItemsReceived", observedIds.Count);
        }

        return items;
    }

    private static EToroSearchItem[] ResolveMatches(
        string exchange,
        string symbol,
        List<EToroSearchItem> items,
        string[] mappings)
    {
        return items
            .Where(item => string.Equals(
                item.InternalSymbolFull,
                symbol,
                StringComparison.OrdinalIgnoreCase))
            .Where(item => MatchesExchange(item, mappings))
            .Where(item => item.InstrumentId.HasValue)
            .GroupBy(item => item.InstrumentId!.Value)
            .Select(group => group.First())
            .ToArray();
    }

    private Result<long> ResolveFromSearch(
        MarketInstrumentIdentity instrument,
        List<EToroSearchItem> items,
        string[] mappings)
    {
        EToroSearchItem[] matches = ResolveMatches(
            instrument.Exchange,
            instrument.Symbol,
            items,
            mappings);
        long[] matchIds = matches
            .Select(match => match.InstrumentId!.Value)
            .ToArray();

        if (matchIds.Length == 0)
        {
            return MarketDataErrors.InstrumentMappingUnresolved
                .WithExtension("exchange", instrument.Exchange)
                .WithExtension("symbol", instrument.Symbol);
        }

        if (matchIds.Length > 1)
        {
            return MarketDataErrors.InstrumentMappingAmbiguous
                .WithExtension("exchange", instrument.Exchange)
                .WithExtension("symbol", instrument.Symbol)
                .WithExtension("candidateInstrumentIds", matchIds);
        }

        _instrumentIdCache.Store(
            instrument.Exchange,
            instrument.Symbol,
            instrument.QuoteCurrency,
            matchIds[0],
            TimeSpan.FromSeconds(_options.ResolutionCacheSeconds));

        return matchIds[0];
    }

    private async Task<Result<MarketQuote>> GetRateQuoteAsync(
        MarketInstrumentIdentity instrument,
        long instrumentId,
        Instant deadline,
        CancellationToken cancellationToken)
    {
        string relativeUri = string.Create(
            CultureInfo.InvariantCulture,
            $"{RatesPath}?instrumentIds={instrumentId}");
        EToroProviderResponse response = await SendWithBoundedRetriesAsync(
            relativeUri,
            deadline,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.PartialContent)
        {
            return MapQuote(instrument, instrumentId, response);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return MarketDataErrors.PriceUnavailable
                .WithExtension("symbol", instrument.Symbol)
                .WithExtension("providerInstrumentId", instrumentId);
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return Throttled(response);
        }

        throw ProviderFailure("retrieve instrument rates", response);
    }

    private Result<MarketQuote> MapQuote(
        MarketInstrumentIdentity instrument,
        long instrumentId,
        EToroProviderResponse response)
    {
        EToroRatesResponse rates = Deserialize<EToroRatesResponse>(response.Body, "rates");

        EToroRate? rate = (rates.Results ?? [])
            .FirstOrDefault(candidate => candidate.InstrumentId == instrumentId);
        if (rate is null)
        {
            return MarketDataErrors.PriceUnavailable
                .WithExtension("symbol", instrument.Symbol)
                .WithExtension("providerInstrumentId", instrumentId);
        }

        return MapRate(instrument, instrumentId, rate, response);
    }

    private MarketQuote MapRate(
        MarketInstrumentIdentity instrument,
        long instrumentId,
        EToroRate rate,
        EToroProviderResponse response)
    {
        return new MarketQuote(
            instrument,
            ProviderName,
            instrumentId,
            rate.Bid,
            rate.Ask,
            ParseObservedAt(rate.Date),
            _clock.GetCurrentInstant(),
            ParseTimeliness(rate.QuoteType),
            response.RequestId);
    }

    private async Task<EToroProviderResponse> SendWithBoundedRetriesAsync(
        string relativeUri,
        Instant deadline,
        CancellationToken cancellationToken)
    {
        EToroProviderResponse? lastThrottledResponse = null;
        for (int attempt = 0; ; attempt++)
        {
            if (await _rateGate.WaitForSlotAsync(
                    EToroRateLimitPolicies.MarketData,
                    deadline,
                    cancellationToken) is false)
            {
                _logger.LogWarning(
                    "eToro market-data request {RequestUri} could not be paced within the " +
                    "execution deadline; the local rate window is saturated.",
                    relativeUri.Split('?')[0]);
                return lastThrottledResponse ?? new EToroProviderResponse
                {
                    StatusCode = HttpStatusCode.TooManyRequests,
                    RequestId = string.Empty,
                    ClientThrottled = true
                };
            }

            if (DeadlineExceeded(deadline))
            {
                throw new TimeoutException(
                    $"The eToro request '{relativeUri.Split('?')[0]}' did not start within the " +
                    "permitted execution budget.");
            }

            string requestId = Guid.NewGuid().ToString("D");
            try
            {
                using CancellationTokenSource attemptScope = CreateAttemptScope(
                    deadline,
                    cancellationToken);
                using HttpRequestMessage request = CreateRequest(relativeUri, requestId);
                RecordRequest(relativeUri, requestId);
                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    attemptScope.Token);

                EToroProviderResponse captured = await CaptureAsync(
                    response,
                    requestId,
                    _clock.GetCurrentInstant().ToDateTimeOffset(),
                    cancellationToken);
                LogResponse(relativeUri, captured);

                if (captured.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    lastThrottledResponse = captured;
                    _rateGate.RegisterCooldown(
                        EToroRateLimitPolicies.MarketData.Name,
                        _clock.GetCurrentInstant().Plus(
                            Duration.FromTimeSpan(
                                RequiredDelay(captured, attempt))));
                }

                if (IsTransient(captured.StatusCode) is false
                    || attempt >= _options.MaxRetryAttempts)
                {
                    return captured;
                }

                TimeSpan delay = RequiredDelay(captured, attempt);
                if (DeadlineExceededAfter(deadline, delay))
                {
                    return captured;
                }

                await Task.Delay(delay, _timeProvider, cancellationToken);
            }
            catch (OperationCanceledException exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                if (attempt >= _options.MaxRetryAttempts
                    || DeadlineExceeded(deadline)
                    || DeadlineExceededAfter(deadline, RetryBackoff(attempt)))
                {
                    throw new TimeoutException(
                        $"The eToro request '{relativeUri.Split('?')[0]}' timed out after " +
                        $"{attempt + 1} attempt(s).",
                        exception);
                }

                await Task.Delay(RetryBackoff(attempt), _timeProvider, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                if (attempt >= _options.MaxRetryAttempts
                    || DeadlineExceeded(deadline)
                    || DeadlineExceededAfter(deadline, RetryBackoff(attempt)))
                {
                    throw new EToroMarketDataException(
                        $"The eToro request '{relativeUri.Split('?')[0]}' failed after " +
                        $"{attempt + 1} attempt(s).",
                        exception);
                }

                await Task.Delay(RetryBackoff(attempt), _timeProvider, cancellationToken);
            }
        }
    }

    private static string SearchUri(string symbol, int page, int pageSize)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{SearchPath}?internalSymbolFull={Uri.EscapeDataString(symbol)}" +
            $"&fields={SearchFields}&pageNumber={page}&pageSize={pageSize}");
    }

    private async Task<Error?> CheckQuoteCurrencyAsync(
        MarketInstrumentIdentity instrument,
        CancellationToken cancellationToken)
    {
        Result<NativeQuoteUnitResolution> resolved = await _quoteUnits.ResolveAsync(
            instrument.Exchange,
            instrument.Symbol,
            cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        NativeQuoteUnitResolution resolution = resolved.Value;
        if (resolution.SourceConflict)
        {
            return MarketDataErrors.QuoteCurrencySourceConflict
                .WithExtension("exchange", instrument.Exchange)
                .WithExtension("symbol", instrument.Symbol)
                .WithExtension("persistedCurrency", resolution.Persisted?.Listing.NativeQuoteCurrency)
                .WithExtension("deploymentCurrency", resolution.DeploymentCurrency);
        }

        if (resolution.NativeQuoteCurrency is null)
        {
            return MarketDataErrors.QuoteCurrencyUnconfigured
                .WithExtension("exchange", instrument.Exchange)
                .WithExtension("symbol", instrument.Symbol)
                .WithExtension("requestedCurrency", instrument.QuoteCurrency);
        }

        if (string.Equals(
                resolution.NativeQuoteCurrency,
                instrument.QuoteCurrency,
                StringComparison.OrdinalIgnoreCase) is false)
        {
            return MarketDataErrors.QuoteCurrencyConflict
                .WithExtension("exchange", instrument.Exchange)
                .WithExtension("symbol", instrument.Symbol)
                .WithExtension("requestedCurrency", instrument.QuoteCurrency)
                .WithExtension("providerCurrency", resolution.NativeQuoteCurrency);
        }

        CachePersistedInstrument(instrument, resolution);
        return null;
    }

    private void CachePersistedInstrument(
        MarketInstrumentIdentity instrument,
        NativeQuoteUnitResolution resolution)
    {
        Domain.Instruments.InstrumentListing? listing = resolution.Persisted?.Listing;
        if (listing is null
            || string.Equals(listing.Provider, ProviderName, StringComparison.OrdinalIgnoreCase) is false
            || string.Equals(listing.Exchange, instrument.Exchange, StringComparison.OrdinalIgnoreCase) is false
            || string.Equals(listing.Symbol, instrument.Symbol, StringComparison.OrdinalIgnoreCase) is false
            || string.Equals(listing.ProviderSymbol, instrument.Symbol, StringComparison.OrdinalIgnoreCase) is false
            || _options.ExchangeMap.TryGetValue(instrument.Exchange, out string[]? mappings) is false
            || mappings.Contains(listing.ProviderExchangeName, StringComparer.OrdinalIgnoreCase) is false)
        {
            return;
        }

        _instrumentIdCache.Store(
            instrument.Exchange,
            instrument.Symbol,
            instrument.QuoteCurrency,
            listing.ProviderInstrumentId,
            TimeSpan.FromSeconds(_options.ResolutionCacheSeconds));
    }

    private HttpRequestMessage CreateRequest(string relativeUri, string requestId)
    {
        HttpRequestMessage request = new(HttpMethod.Get, relativeUri);
        request.Headers.TryAddWithoutValidation(RequestIdHeader, requestId);
        request.Headers.TryAddWithoutValidation(ApiKeyHeader, _options.ApiKey);
        request.Headers.TryAddWithoutValidation(UserKeyHeader, _options.UserKey);
        return request;
    }

    private CancellationTokenSource CreateAttemptScope(
        Instant deadline,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource scope = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        scope.CancelAfter(AttemptTimeout(deadline));
        return scope;
    }

    private TimeSpan AttemptTimeout(Instant deadline)
    {
        TimeSpan remaining = RemainingBefore(deadline);
        TimeSpan configured = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);
        return remaining < configured ? remaining : configured;
    }

    private TimeSpan RemainingBefore(Instant deadline)
    {
        Duration remaining = deadline - _clock.GetCurrentInstant();
        TimeSpan span = remaining.ToTimeSpan();
        return span < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : span;
    }

    private bool DeadlineExceeded(Instant deadline)
    {
        return _clock.GetCurrentInstant() >= deadline;
    }

    private bool DeadlineExceededAfter(Instant deadline, TimeSpan delay)
    {
        return _clock.GetCurrentInstant().Plus(Duration.FromTimeSpan(delay)) >= deadline;
    }

    private TimeSpan MaxExecutionTime()
    {
        return TimeSpan.FromSeconds(_options.MaxExecutionTimeSeconds);
    }

    private static TimeSpan RequiredDelay(EToroProviderResponse response, int attempt)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            if (response.RetryAfter is not null)
            {
                return response.RetryAfter.Value;
            }

            if (response.RateLimitResetSeconds is not null)
            {
                return TimeSpan.FromSeconds(response.RateLimitResetSeconds.Value);
            }
        }

        return RetryBackoff(attempt);
    }

    private static TimeSpan RetryBackoff(int attempt)
    {
        double multiplier = Math.Pow(2, attempt);
        TimeSpan delay = TimeSpan.FromTicks((long)(InitialRetryDelay.Ticks * multiplier));
        return delay > MaximumRetryDelay ? MaximumRetryDelay : delay;
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.TooManyRequests
            || statusCode == HttpStatusCode.RequestTimeout
            || (int)statusCode >= 500;
    }

    private static bool MatchesExchange(EToroSearchItem item, string[] mappings)
    {
        foreach (string mapping in mappings)
        {
            if (int.TryParse(mapping, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                && (item.InternalExchangeId == id || item.ExchangeId == id))
            {
                return true;
            }

            if (string.Equals(
                    item.InternalExchangeName,
                    mapping,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static Instant? ParseObservedAt(string? date)
    {
        if (DateTimeOffset.TryParse(
                date,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed))
        {
            return Instant.FromDateTimeOffset(parsed);
        }

        return null;
    }

    private static MarketQuoteTimeliness ParseTimeliness(string? quoteType)
    {
        return quoteType switch
        {
            "realtime" => MarketQuoteTimeliness.Realtime,
            "delayed" => MarketQuoteTimeliness.Delayed,
            _ => MarketQuoteTimeliness.Unknown
        };
    }

    private static T Deserialize<T>(string? body, string operation)
        where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new T();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, SerializerOptions) ?? new T();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"The eToro {operation} response could not be parsed.",
                exception);
        }
    }

    private static Error Throttled(EToroProviderResponse response)
    {
        return MarketDataErrors.ProviderThrottled
            .WithExtension("scope", response.ClientThrottled ? "local_pacing" : "provider_429")
            .WithExtension("retryAfterSeconds", response.RetryAfter?.TotalSeconds)
            .WithExtension("rateLimitRemaining", response.RateLimitRemaining);
    }

    private Exception ProviderFailure(string operation, EToroProviderResponse response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            string? errorCode = TryReadGatewayErrorCode(response.Body);
            _logger.LogWarning(
                "eToro rejected market-data credentials while attempting to {Operation} " +
                "(request {RequestId}, status {StatusCode}).",
                operation,
                response.RequestId,
                (int)response.StatusCode);
            return new EToroMarketDataException(
                $"The eToro provider rejected the configured market-data credentials " +
                $"while attempting to {operation} (status {(int)response.StatusCode}" +
                $"{(errorCode is null ? string.Empty : $", error '{errorCode}'")}).");
        }

        return new EToroMarketDataException(
            $"The eToro provider failed to {operation} " +
            $"(request '{response.RequestId}', status {(int)response.StatusCode}).");
    }

    private void LogResponse(string relativeUri, EToroProviderResponse response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning(
                "eToro market-data request {RequestId} to {RequestUri} was throttled " +
                "(rate limit remaining {RateLimitRemaining}, retry after {RetryAfterSeconds}s).",
                response.RequestId,
                relativeUri.Split('?')[0],
                response.RateLimitRemaining,
                response.RetryAfter?.TotalSeconds);
            return;
        }

        _logger.LogDebug(
            "eToro market-data request {RequestId} to {RequestUri} returned {StatusCode} " +
            "(rate limit remaining {RateLimitRemaining}).",
            response.RequestId,
            relativeUri.Split('?')[0],
            (int)response.StatusCode,
            response.RateLimitRemaining);
    }

    private static string? TryReadGatewayErrorCode(string? body)
    {
        try
        {
            EToroGatewayError error = Deserialize<EToroGatewayError>(body, "error");
            return error.ErrorCode;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private void RequireCredentials()
    {
        if (CredentialsConfigured() is false)
        {
            throw new InvalidOperationException(
                "The eToro market-data credentials are not configured. Set " +
                $"{EToroMarketDataOptions.SectionName}:ApiKey and " +
                $"{EToroMarketDataOptions.SectionName}:UserKey through a secure " +
                "configuration source.");
        }
    }

    private bool CredentialsConfigured()
    {
        return string.IsNullOrWhiteSpace(_options.ApiKey) is false
            && string.IsNullOrWhiteSpace(_options.UserKey) is false;
    }

    private Error Unavailable(MarketListingQuery query, Exception exception)
    {
        _logger.LogWarning(
            exception,
            "eToro listing resolution for {Exchange}/{Symbol} failed.",
            query.Exchange,
            query.Symbol);
        return MarketDataErrors.ProviderUnavailable
            .WithExtension("exchange", query.Exchange)
            .WithExtension("symbol", query.Symbol);
    }

    private static async Task<EToroProviderResponse> CaptureAsync(
        HttpResponseMessage response,
        string requestId,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        string body = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);

        return new EToroProviderResponse
        {
            StatusCode = response.StatusCode,
            RequestId = requestId,
            Body = body,
            RetryAfter = ReadRetryAfter(response.Headers, capturedAt),
            RateLimitRemaining = ReadHeaderInt(response.Headers, "RateLimit-Remaining"),
            RateLimitResetSeconds = ReadHeaderInt(response.Headers, "RateLimit-Reset")
        };
    }

    private static TimeSpan? ReadRetryAfter(
        HttpResponseHeaders headers,
        DateTimeOffset capturedAt)
    {
        RetryConditionHeaderValue? retryAfter = headers.RetryAfter;
        if (retryAfter?.Delta is not null)
        {
            return retryAfter.Delta;
        }

        if (retryAfter?.Date is not null)
        {
            TimeSpan delay = retryAfter.Date.Value - capturedAt;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }

    private static int? ReadHeaderInt(HttpResponseHeaders headers, string name)
    {
        return headers.TryGetValues(name, out IEnumerable<string>? values)
            && int.TryParse(
                values.FirstOrDefault(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed)
            ? parsed
            : null;
    }
}
