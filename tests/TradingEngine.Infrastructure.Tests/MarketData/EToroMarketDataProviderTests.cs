using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;
using TradingEngine.Infrastructure.MarketData;

namespace TradingEngine.Infrastructure.Tests.MarketData;

[TestFixture]
public sealed class EToroMarketDataProviderTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 30, 12, 0, 0);
    private static readonly MarketInstrumentIdentity Instrument =
        new("XTEST", "DEMO", "USD");

    private FakeClock _clock = null!;
    private ManualTimeProvider _timeProvider = null!;
    private EToroMarketDataOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeClock(Now);
        _timeProvider = new ManualTimeProvider(
            delay => _clock.Advance(Duration.FromTimeSpan(delay)));
        _options = new EToroMarketDataOptions
        {
            ApiKey = "test-api-key",
            UserKey = "test-user-key",
            RequestTimeoutSeconds = 5,
            MaxExecutionTimeSeconds = 60,
            MaxRetryAttempts = 2,
            ResolutionCacheSeconds = 3600,
            ExchangeMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["XTEST"] = ["Test Exchange"],
                ["XID"] = ["7"]
            }
        };
    }

    [Test]
    public async Task GetCurrentQuoteAsync_WhenProviderReturnsQuote_MapsFieldsAndProvenance()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody((4242, "DEMO", 5, "Test Exchange")));
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.RatesBody(
            (4242, "123.456789012345678", "124.000000000000001", "2026-10-30T14:31:12Z", "realtime")));
        IMarketDataProvider provider = CreateProvider(handler);

        Result<MarketQuote> result = await provider.GetCurrentQuoteAsync(
            Instrument,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Instrument, Is.SameAs(Instrument));
            Assert.That(result.Value.Provider, Is.EqualTo("eToro"));
            Assert.That(result.Value.ProviderInstrumentId, Is.EqualTo(4242));
            Assert.That(result.Value.Bid, Is.EqualTo(123.456789012345678m));
            Assert.That(result.Value.Ask, Is.EqualTo(124.000000000000001m));
            Assert.That(
                result.Value.ProviderObservedAt,
                Is.EqualTo(Instant.FromUtc(2026, 10, 30, 14, 31, 12)));
            Assert.That(result.Value.RetrievedAt, Is.EqualTo(Now));
            Assert.That(result.Value.Timeliness, Is.EqualTo(MarketQuoteTimeliness.Realtime));
            Assert.That(
                result.Value.ProviderRequestId,
                Is.EqualTo(handler.Requests[1].HeaderValue("x-request-id")));
        });
    }

    [Test]
    public async Task GetCurrentQuoteAsync_Always_SendsRequiredProviderHeaders()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody((4242, "DEMO", 5, "Test Exchange")));
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.RatesBody(
            (4242, "10.5", "10.6", "2026-10-30T14:31:12Z", "realtime")));
        IMarketDataProvider provider = CreateProvider(handler);

        await provider.GetCurrentQuoteAsync(Instrument, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            foreach (CapturedRequest request in handler.Requests)
            {
                Assert.That(request.HeaderValue("x-api-key"), Is.EqualTo("test-api-key"));
                Assert.That(request.HeaderValue("x-user-key"), Is.EqualTo("test-user-key"));
                Assert.That(
                    Guid.TryParse(request.HeaderValue("x-request-id"), out Guid _),
                    Is.True);
            }

            Assert.That(
                handler.Requests[0].RequestUri!.ToString(),
                Does.Contain("/api/v1/market-data/search"));
            Assert.That(
                handler.Requests[0].RequestUri!.ToString(),
                Does.Contain("internalSymbolFull=DEMO"));
            Assert.That(
                handler.Requests[1].RequestUri!.ToString(),
                Does.Contain("/api/v2/market-data/rates"));
            Assert.That(
                handler.Requests[1].RequestUri!.ToString(),
                Does.Contain("instrumentIds=4242"));
        });
    }

    [Test]
    public void GetCurrentQuoteAsync_WithoutConfiguredCredentials_ThrowsInvalidOperation()
    {
        _options.ApiKey = string.Empty;
        _options.UserKey = string.Empty;
        ScriptedHttpMessageHandler handler = new();
        IMarketDataProvider provider = CreateProvider(handler);

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await provider.GetCurrentQuoteAsync(
                    Instrument,
                    CancellationToken.None),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task GetCurrentQuoteAsync_WithoutExchangeMapping_ReturnsExchangeUnconfigured()
    {
        _options.ExchangeMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        ScriptedHttpMessageHandler handler = new();
        IMarketDataProvider provider = CreateProvider(handler);

        Result<MarketQuote> result = await provider.GetCurrentQuoteAsync(
            Instrument,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error.Code,
                Is.EqualTo("market_data.exchange_mapping_unconfigured"));
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task GetCurrentQuoteAsync_WhenSymbolHasNoExactMatch_ReturnsUnresolved()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody(
            (4242, "DEMO.EXT", 5, "Test Exchange"),
            (4243, "OTHER", 5, "Test Exchange")));
        IMarketDataProvider provider = CreateProvider(handler);

        Result<MarketQuote> result = await provider.GetCurrentQuoteAsync(
            Instrument,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error.Code,
                Is.EqualTo("market_data.instrument_mapping_unresolved"));
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task GetCurrentQuoteAsync_WhenCandidatesSkipExchange_ReturnsUnresolved()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody(
            (4242, "DEMO", 9, "Other Exchange")));
        IMarketDataProvider provider = CreateProvider(handler);

        Result<MarketQuote> result = await provider.GetCurrentQuoteAsync(
            Instrument,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error.Code,
                Is.EqualTo("market_data.instrument_mapping_unresolved"));
        });
    }

    [Test]
    public async Task GetCurrentQuoteAsync_WhenMultipleCandidatesMatch_ReturnsAmbiguous()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody(
            (4242, "DEMO", 5, "Test Exchange"),
            (4243, "DEMO", 5, "Test Exchange")));
        IMarketDataProvider provider = CreateProvider(handler);

        Result<MarketQuote> result = await provider.GetCurrentQuoteAsync(
            Instrument,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error.Code,
                Is.EqualTo("market_data.instrument_mapping_ambiguous"));
        });
    }

    [Test]
    public async Task GetCurrentQuoteAsync_WhenExchangeIdMappingMatches_ResolvesById()
    {
        MarketInstrumentIdentity byId = new("XID", "DEMO", "USD");
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody((4242, "DEMO", 7, "Venue Seven")));
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.RatesBody(
            (4242, "10.5", "10.6", "2026-10-30T14:31:12Z", "realtime")));
        IMarketDataProvider provider = CreateProvider(handler);

        Result<MarketQuote> result = await provider.GetCurrentQuoteAsync(
            byId,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.ProviderInstrumentId, Is.EqualTo(4242));
        });
    }

    [Test]
    public async Task GetCurrentQuoteAsync_WhenRateEntryMissing_ReturnsPriceUnavailable()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody((4242, "DEMO", 5, "Test Exchange")));
        handler.EnqueueResponse(HttpStatusCode.PartialContent, EToroPayloads.RatesBody(
            (9999, "1.0", "1.1", "2026-10-30T14:31:12Z", "realtime")));
        IMarketDataProvider provider = CreateProvider(handler);

        Result<MarketQuote> result = await provider.GetCurrentQuoteAsync(
            Instrument,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Code, Is.EqualTo("market_data.price_unavailable"));
        });
    }

    [Test]
    public async Task GetCurrentQuoteAsync_WhenRatesEndpointNotFound_ReturnsPriceUnavailable()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody((4242, "DEMO", 5, "Test Exchange")));
        handler.EnqueueResponse(HttpStatusCode.NotFound, string.Empty);
        IMarketDataProvider provider = CreateProvider(handler);

        Result<MarketQuote> result = await provider.GetCurrentQuoteAsync(
            Instrument,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Code, Is.EqualTo("market_data.price_unavailable"));
        });
    }

    [Test]
    public async Task GetCurrentQuoteAsync_WhenPricesAreNull_PreservesMissingValues()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody((4242, "DEMO", 5, "Test Exchange")));
        handler.EnqueueResponse(HttpStatusCode.OK, """
            {"results":[{"instrumentId":4242,"bid":null,"ask":null,"date":null,"quoteType":"delayed"}]}
            """);
        IMarketDataProvider provider = CreateProvider(handler);

        Result<MarketQuote> result = await provider.GetCurrentQuoteAsync(
            Instrument,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Bid, Is.Null);
            Assert.That(result.Value.Ask, Is.Null);
            Assert.That(result.Value.ProviderObservedAt, Is.Null);
            Assert.That(result.Value.Timeliness, Is.EqualTo(MarketQuoteTimeliness.Delayed));
        });
    }

    [Test]
    public void GetCurrentQuoteAsync_WhenCredentialsRejected_ThrowsProviderException()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.Unauthorized,
            """{"errorCode":"Unauthorized","errorMessage":"Bad or missing API key."}""");
        IMarketDataProvider provider = CreateProvider(handler);

        Assert.That(
            async () => await provider.GetCurrentQuoteAsync(Instrument, CancellationToken.None),
            Throws.TypeOf<EToroMarketDataException>());
    }

    [Test]
    public async Task GetCurrentQuoteAsync_WhenThrottledOnce_RetriesAfterProviderDelay()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody((4242, "DEMO", 5, "Test Exchange")));
        handler.Enqueue((_, _) =>
        {
            HttpResponseMessage throttled = new(HttpStatusCode.TooManyRequests);
            throttled.Headers.TryAddWithoutValidation("Retry-After", "7");
            throttled.Headers.TryAddWithoutValidation("RateLimit-Remaining", "0");
            return Task.FromResult(throttled);
        });
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.RatesBody(
            (4242, "10.5", "10.6", "2026-10-30T14:31:12Z", "realtime")));
        IMarketDataProvider provider = CreateProvider(handler);

        Result<MarketQuote> result = await provider.GetCurrentQuoteAsync(
            Instrument,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_timeProvider.DelayRequests,
                Is.EqualTo(new[] { TimeSpan.FromSeconds(7) }));
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task GetCurrentQuoteAsync_WhenThrottledBeyondRetryBudget_ReturnsThrottled()
    {
        _options.MaxRetryAttempts = 1;
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody((4242, "DEMO", 5, "Test Exchange")));
        handler.Enqueue((_, _) =>
        {
            HttpResponseMessage throttled = new(HttpStatusCode.TooManyRequests);
            throttled.Headers.TryAddWithoutValidation("Retry-After", "7");
            return Task.FromResult(throttled);
        });
        handler.Enqueue((_, _) =>
        {
            HttpResponseMessage throttled = new(HttpStatusCode.TooManyRequests);
            throttled.Headers.TryAddWithoutValidation("Retry-After", "7");
            return Task.FromResult(throttled);
        });
        IMarketDataProvider provider = CreateProvider(handler);

        Result<MarketQuote> result = await provider.GetCurrentQuoteAsync(
            Instrument,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Code, Is.EqualTo("market_data.provider_throttled"));
            Assert.That(result.Error.Extensions?["scope"], Is.EqualTo("provider_429"));
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
            Assert.That(_timeProvider.DelayRequests,
                Is.EqualTo(new[] { TimeSpan.FromSeconds(7) }));
        });
    }

    [Test]
    public async Task GetCurrentQuoteAsync_WhenProviderDelayExceedsDeadline_ReturnsThrottledWithoutRetrying()
    {
        _options.MaxExecutionTimeSeconds = 10;
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody((4242, "DEMO", 5, "Test Exchange")));
        handler.Enqueue((_, _) =>
        {
            HttpResponseMessage throttled = new(HttpStatusCode.TooManyRequests);
            throttled.Headers.TryAddWithoutValidation("Retry-After", "600");
            return Task.FromResult(throttled);
        });
        IMarketDataProvider provider = CreateProvider(handler);

        Result<MarketQuote> result = await provider.GetCurrentQuoteAsync(
            Instrument,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Code, Is.EqualTo("market_data.provider_throttled"));
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(_timeProvider.DelayRequests, Is.Empty);
        });
    }

    [Test]
    public void GetCurrentQuoteAsync_WhenServerErrorsExceedRetries_ThrowsProviderException()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody((4242, "DEMO", 5, "Test Exchange")));
        handler.EnqueueResponse(HttpStatusCode.InternalServerError, string.Empty);
        handler.EnqueueResponse(HttpStatusCode.InternalServerError, string.Empty);
        handler.EnqueueResponse(HttpStatusCode.InternalServerError, string.Empty);
        IMarketDataProvider provider = CreateProvider(handler);

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await provider.GetCurrentQuoteAsync(
                    Instrument,
                    CancellationToken.None),
                Throws.TypeOf<EToroMarketDataException>());
            Assert.That(_timeProvider.DelayRequests, Is.EqualTo(new[]
            {
                TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(400)
            }));
        });
    }

    [Test]
    public void GetCurrentQuoteAsync_WhenAttemptsTimeOut_ThrowsTimeoutException()
    {
        _options.RequestTimeoutSeconds = 0;
        _options.MaxRetryAttempts = 1;
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody((4242, "DEMO", 5, "Test Exchange")));
        handler.Enqueue(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        handler.Enqueue(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        IMarketDataProvider provider = CreateProvider(handler);

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await provider.GetCurrentQuoteAsync(
                    Instrument,
                    CancellationToken.None),
                Throws.TypeOf<TimeoutException>());
            Assert.That(_timeProvider.DelayRequests,
                Is.EqualTo(new[] { TimeSpan.FromMilliseconds(200) }));
        });
    }

    [Test]
    public void GetCurrentQuoteAsync_WhenCallerCancels_PropagatesCancellation()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody((4242, "DEMO", 5, "Test Exchange")));
        handler.Enqueue(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        IMarketDataProvider provider = CreateProvider(handler);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.That(
            async () => await provider.GetCurrentQuoteAsync(Instrument, cancelled.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void GetCurrentQuoteAsync_WhenRatesBodyMalformed_ThrowsInvalidData()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody((4242, "DEMO", 5, "Test Exchange")));
        handler.EnqueueResponse(HttpStatusCode.OK, "this-is-not-json");
        IMarketDataProvider provider = CreateProvider(handler);

        Assert.That(
            async () => await provider.GetCurrentQuoteAsync(Instrument, CancellationToken.None),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public async Task GetCurrentQuoteAsync_OnSecondCall_ReusesResolvedInstrumentId()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody((4242, "DEMO", 5, "Test Exchange")));
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.RatesBody(
            (4242, "10.5", "10.6", "2026-10-30T14:31:12Z", "realtime")));
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.RatesBody(
            (4242, "10.7", "10.8", "2026-10-30T14:32:12Z", "realtime")));
        IMarketDataProvider provider = CreateProvider(handler);

        Result<MarketQuote> first = await provider.GetCurrentQuoteAsync(
            Instrument,
            CancellationToken.None);
        Result<MarketQuote> second = await provider.GetCurrentQuoteAsync(
            Instrument,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
            Assert.That(
                handler.Requests[2].RequestUri!.ToString(),
                Does.Contain("/api/v2/market-data/rates"));
            Assert.That(second.Value.Bid, Is.EqualTo(10.7m));
        });
    }

    private EToroMarketDataProvider CreateProvider(ScriptedHttpMessageHandler handler)
    {
        return new EToroMarketDataProvider(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://public-api.test/")
            },
            Options.Create(_options),
            new ProviderRateGate(_clock, _timeProvider),
            new EToroInstrumentIdCache(_clock),
            _clock,
            _timeProvider,
            NullLogger<EToroMarketDataProvider>.Instance);
    }

}
