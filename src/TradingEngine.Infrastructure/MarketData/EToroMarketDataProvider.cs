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
        "instrumentId,internalSymbolFull,internalExchangeId,internalExchangeName,exchangeID";
    private const string RequestIdHeader = "x-request-id";
    private const string ApiKeyHeader = "x-api-key";
    private const string UserKeyHeader = "x-user-key";

    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly EToroMarketDataOptions _options;
    private readonly ProviderRateGate _rateGate;
    private readonly EToroInstrumentIdCache _instrumentIdCache;
    private readonly IClock _clock;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EToroMarketDataProvider> _logger;

    public EToroMarketDataProvider(
        HttpClient httpClient,
        IOptions<EToroMarketDataOptions> options,
        ProviderRateGate rateGate,
        EToroInstrumentIdCache instrumentIdCache,
        IClock clock,
        TimeProvider timeProvider,
        ILogger<EToroMarketDataProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _rateGate = rateGate ?? throw new ArgumentNullException(nameof(rateGate));
        _instrumentIdCache = instrumentIdCache
            ?? throw new ArgumentNullException(nameof(instrumentIdCache));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<MarketQuote>> GetCurrentQuoteAsync(
        MarketInstrumentIdentity instrument,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        RequireCredentials();

        Error? currencyError = CheckQuoteCurrency(instrument);
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

        List<EToroSearchItem> items = [];
        HashSet<long> observedIds = [];
        int totalItems = int.MaxValue;

        for (int page = 1; page <= _options.MaxSearchPages; page++)
        {
            EToroProviderResponse response = await SendWithBoundedRetriesAsync(
                SearchUri(instrument.Symbol, page, _options.SearchPageSize),
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
                .WithExtension("exchange", instrument.Exchange)
                .WithExtension("symbol", instrument.Symbol)
                .WithExtension("totalItems", totalItems)
                .WithExtension("distinctItemsReceived", observedIds.Count);
        }

        return ResolveFromSearch(instrument, items, mappings);
    }

    private Result<long> ResolveFromSearch(
        MarketInstrumentIdentity instrument,
        List<EToroSearchItem> items,
        string[] mappings)
    {
        long[] matches = items
            .Where(item => string.Equals(
                item.InternalSymbolFull,
                instrument.Symbol,
                StringComparison.OrdinalIgnoreCase))
            .Where(item => MatchesExchange(item, mappings))
            .Select(item => item.InstrumentId)
            .OfType<long>()
            .Distinct()
            .ToArray();

        if (matches.Length == 0)
        {
            return MarketDataErrors.InstrumentMappingUnresolved
                .WithExtension("exchange", instrument.Exchange)
                .WithExtension("symbol", instrument.Symbol);
        }

        if (matches.Length > 1)
        {
            return MarketDataErrors.InstrumentMappingAmbiguous
                .WithExtension("exchange", instrument.Exchange)
                .WithExtension("symbol", instrument.Symbol)
                .WithExtension("candidateInstrumentIds", matches);
        }

        _instrumentIdCache.Store(
            instrument.Exchange,
            instrument.Symbol,
            instrument.QuoteCurrency,
            matches[0],
            TimeSpan.FromSeconds(_options.ResolutionCacheSeconds));

        return matches[0];
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
                    relativeUri);
                return new EToroProviderResponse
                {
                    StatusCode = HttpStatusCode.TooManyRequests,
                    RequestId = string.Empty,
                    ClientThrottled = true
                };
            }

            if (DeadlineExceeded(deadline))
            {
                throw new TimeoutException(
                    $"The eToro request '{relativeUri}' did not start within the " +
                    "permitted execution budget.");
            }

            string requestId = Guid.NewGuid().ToString("D");
            try
            {
                using CancellationTokenSource attemptScope = CreateAttemptScope(
                    deadline,
                    cancellationToken);
                using HttpRequestMessage request = CreateRequest(relativeUri, requestId);
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
                        $"The eToro request '{relativeUri}' timed out after " +
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
                        $"The eToro request '{relativeUri}' failed after " +
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

    private Error? CheckQuoteCurrency(MarketInstrumentIdentity instrument)
    {
        if (_options.QuoteCurrencyMap.TryGetValue(
                instrument.Exchange,
                out Dictionary<string, string>? symbols) is false
            || symbols.TryGetValue(instrument.Symbol, out string? nativeCurrency) is false
            || string.IsNullOrWhiteSpace(nativeCurrency))
        {
            return MarketDataErrors.QuoteCurrencyUnconfigured
                .WithExtension("exchange", instrument.Exchange)
                .WithExtension("symbol", instrument.Symbol)
                .WithExtension("requestedCurrency", instrument.QuoteCurrency);
        }

        if (string.Equals(
                nativeCurrency,
                instrument.QuoteCurrency,
                StringComparison.OrdinalIgnoreCase) is false)
        {
            return MarketDataErrors.QuoteCurrencyConflict
                .WithExtension("exchange", instrument.Exchange)
                .WithExtension("symbol", instrument.Symbol)
                .WithExtension("requestedCurrency", instrument.QuoteCurrency)
                .WithExtension("providerCurrency", nativeCurrency);
        }

        return null;
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
                relativeUri,
                response.RateLimitRemaining,
                response.RetryAfter?.TotalSeconds);
            return;
        }

        _logger.LogDebug(
            "eToro market-data request {RequestId} to {RequestUri} returned {StatusCode} " +
            "(rate limit remaining {RateLimitRemaining}).",
            response.RequestId,
            relativeUri,
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
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(_options.UserKey))
        {
            throw new InvalidOperationException(
                "The eToro market-data credentials are not configured. Set " +
                $"{EToroMarketDataOptions.SectionName}:ApiKey and " +
                $"{EToroMarketDataOptions.SectionName}:UserKey through a secure " +
                "configuration source.");
        }
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
