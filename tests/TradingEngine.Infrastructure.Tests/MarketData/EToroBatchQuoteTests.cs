using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.Listings;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;
using TradingEngine.Infrastructure.MarketData;

namespace TradingEngine.Infrastructure.Tests.MarketData;

[TestFixture]
public sealed class EToroBatchQuoteTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 1, 5, 12, 0);
    private FakeClock _clock = null!;
    private ManualTimeProvider _time = null!;
    private ProviderRateGate _gate = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeClock(Now);
        _time = new ManualTimeProvider(delay => _clock.Advance(Duration.FromTimeSpan(delay)));
        _gate = new ProviderRateGate(_clock, _time);
    }

    [TestCase(1)]
    [TestCase(1000)]
    public async Task GetCurrentQuotesAsync_ValidBatch_UsesOneRequestAndPreservesEveryQuote(int count)
    {
        MarketInstrumentIdentity[] instruments = Instruments(count);
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, Rates(count));
        EToroMarketDataProvider provider = Provider(handler, instruments);

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync(instruments, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(results, Has.Count.EqualTo(count));
            Assert.That(results.Values.All(result => result.IsSuccess), Is.True);
            Assert.That(results.Values.Select(result => result.Value.ProviderInstrumentId),
                Is.EquivalentTo(Enumerable.Range(10000, count).Select(id => (long)id)));
            Assert.That(results.Values.All(result => result.Value.Bid == 12.345m), Is.True);
            Assert.That(results.Values.All(result => result.Value.Timeliness == MarketQuoteTimeliness.Realtime), Is.True);
            Assert.That(results.Values.All(result => result.Value.ProviderObservedAt == Now), Is.True);
            Assert.That(results.Values.All(result => result.Value.ProviderRequestId ==
                handler.Requests[0].HeaderValue("x-request-id")), Is.True);
            Assert.That(handler.Requests[0].RequestUri!.AbsolutePath, Is.EqualTo("/api/v2/market-data/rates"));
        });
    }

    [Test]
    public async Task GetCurrentQuotesAsync_PartialUnorderedResponse_KeepsSuccessesAndMarksMissing()
    {
        MarketInstrumentIdentity[] instruments = Instruments(3);
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.PartialContent, EToroPayloads.RatesBody(
            (10002, "30", "31", "2026-01-05T12:00:00Z", "delayed"),
            (99999, "99", "100", "2026-01-05T12:00:00Z", "realtime"),
            (10000, "10", "11", "2026-01-05T12:00:00Z", "realtime")));
        EToroMarketDataProvider provider = Provider(handler, instruments);

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync(instruments, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(results[instruments[0]].Value.Bid, Is.EqualTo(10));
            Assert.That(results[instruments[2]].Value.Bid, Is.EqualTo(30));
            Assert.That(results[instruments[2]].Value.Timeliness, Is.EqualTo(MarketQuoteTimeliness.Delayed));
            Assert.That(results[instruments[1]].Error.Code, Is.EqualTo(MarketDataErrors.PriceUnavailable.Code));
        });
    }

    [Test]
    public async Task GetCurrentQuotesAsync_DuplicateResponseId_RejectsOnlyAmbiguousQuote()
    {
        MarketInstrumentIdentity[] instruments = Instruments(2);
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.RatesBody(
            (10000, "10", "11", "2026-01-05T12:00:00Z", "realtime"),
            (10000, "20", "21", "2026-01-05T12:00:00Z", "realtime"),
            (10001, "30", "31", "2026-01-05T12:00:00Z", "realtime")));
        EToroMarketDataProvider provider = Provider(handler, instruments);

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync(instruments, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(results[instruments[0]].Error.Code, Is.EqualTo(MarketDataErrors.ProviderUnavailable.Code));
            Assert.That(results[instruments[1]].Value.Bid, Is.EqualTo(30));
        });
    }

    [Test]
    public async Task GetCurrentQuotesAsync_InvalidCurrency_ExcludesOnlyInvalidInstrument()
    {
        MarketInstrumentIdentity[] instruments = Instruments(2);
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, Rates(1));
        EToroMarketDataProvider provider = Provider(handler, instruments);
        MarketInstrumentIdentity invalid = instruments[1] with { QuoteCurrency = "GBP" };

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync([instruments[0], invalid], CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(results[instruments[0]].IsSuccess, Is.True);
            Assert.That(results[invalid].Error.Code, Is.EqualTo(MarketDataErrors.QuoteCurrencyConflict.Code));
            Assert.That(handler.Requests[0].RequestUri!.Query, Is.EqualTo("?instrumentIds=10000"));
        });
    }

    [Test]
    public async Task GetCurrentQuotesAsync_ThousandQuotes_ConsumesOneSharedRateLimitSlot()
    {
        for (int request = 0; request < 119; request++)
        {
            await _gate.WaitForSlotAsync(EToroRateLimitPolicies.MarketData,
                Now.Plus(Duration.FromMinutes(2)), CancellationToken.None);
        }

        MarketInstrumentIdentity[] instruments = Instruments(1000);
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, Rates(1000));
        handler.EnqueueResponse(HttpStatusCode.OK, Rates(1));
        EToroMarketDataProvider provider = Provider(handler, instruments);

        await provider.GetCurrentQuotesAsync(instruments, CancellationToken.None);
        Instant afterFirstBatch = _clock.GetCurrentInstant();
        await provider.GetCurrentQuotesAsync([instruments[0]], CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(afterFirstBatch, Is.EqualTo(Now));
            Assert.That(_clock.GetCurrentInstant(), Is.EqualTo(Now.Plus(Duration.FromSeconds(60))));
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task GetCurrentQuotesAsync_RateLimited_RetriesWholeBatchAfterCooldown()
    {
        MarketInstrumentIdentity[] instruments = Instruments(2);
        ScriptedHttpMessageHandler handler = new();
        HttpResponseMessage throttled = new(HttpStatusCode.TooManyRequests);
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
        handler.Enqueue(throttled);
        handler.EnqueueResponse(HttpStatusCode.OK, Rates(2));
        EToroMarketDataProvider provider = Provider(handler, instruments);

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync(instruments, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(results.Values.All(result => result.IsSuccess), Is.True);
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(handler.Requests[0].RequestUri, Is.EqualTo(handler.Requests[1].RequestUri));
            Assert.That(_clock.GetCurrentInstant(), Is.EqualTo(Now.Plus(Duration.FromSeconds(3))));
        });
    }

    [Test]
    public async Task GetCurrentQuotesAsync_PersistedListing_ResolvesWithoutSearchOnColdCache()
    {
        MarketInstrumentIdentity[] instruments = Instruments(1);
        InstrumentListing listing = InstrumentListing.Configure(
            "XTEST", instruments[0].Symbol, "eToro", 10000, instruments[0].Symbol,
            "Test Exchange", "Synthetic", "USD", ListingUnitSource.Administrator,
            "synthetic-admin", Now).Value;
        StubListingConfigurationStore store = new()
        {
            FindResult = new ListingConfigurationSnapshot(listing, [1])
        };
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, Rates(1));
        EToroMarketDataProvider provider = Provider(handler, instruments, store);

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync(instruments, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(results[instruments[0]].IsSuccess, Is.True);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(handler.Requests[0].RequestUri!.AbsolutePath, Is.EqualTo("/api/v2/market-data/rates"));
        });
    }

    [TestCase("OtherProvider", "Test Exchange")]
    [TestCase("eToro", "Other Exchange")]
    public async Task GetCurrentQuotesAsync_IncompatiblePersistedListing_UsesVerifiedSearch(
        string providerName, string exchangeName)
    {
        MarketInstrumentIdentity[] instruments = Instruments(1);
        InstrumentListing listing = InstrumentListing.Configure(
            "XTEST", instruments[0].Symbol, providerName, 99999, instruments[0].Symbol,
            exchangeName, "Synthetic", "USD", ListingUnitSource.Administrator,
            "synthetic-admin", Now).Value;
        StubListingConfigurationStore store = new()
        {
            FindResult = new ListingConfigurationSnapshot(listing, [1])
        };
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK,
            EToroPayloads.SearchBody((10000, instruments[0].Symbol, 5, "Test Exchange")));
        handler.EnqueueResponse(HttpStatusCode.OK, Rates(1));
        EToroMarketDataProvider provider = Provider(handler, instruments, store);

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync(instruments, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(results[instruments[0]].Value.ProviderInstrumentId, Is.EqualTo(10000));
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(handler.Requests[0].RequestUri!.AbsolutePath, Is.EqualTo("/api/v1/market-data/search"));
            Assert.That(handler.Requests[1].RequestUri!.Query, Is.EqualTo("?instrumentIds=10000"));
        });
    }

    [Test]
    public async Task GetCurrentQuotesAsync_PersistedIdRefreshed_UsesUpdatedIdInsteadOfCachedId()
    {
        MarketInstrumentIdentity[] instruments = Instruments(1);
        InstrumentListing listing = InstrumentListing.Configure(
            "XTEST", instruments[0].Symbol, "eToro", 10000, instruments[0].Symbol,
            "Test Exchange", "Synthetic", "USD", ListingUnitSource.Administrator,
            "synthetic-admin", Now).Value;
        StubListingConfigurationStore store = new()
        {
            FindResult = new ListingConfigurationSnapshot(listing, [1])
        };
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, Rates(1));
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.RatesBody(
            (10001, "12.345", "12.456", "2026-01-05T12:00:00Z", "realtime")));
        EToroMarketDataProvider provider = Provider(handler, instruments, store);
        await provider.GetCurrentQuotesAsync(instruments, CancellationToken.None);
        listing.RefreshProviderMetadata(10001, instruments[0].Symbol,
            "Test Exchange", "Synthetic", "synthetic-admin", Now);

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync(instruments, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(results[instruments[0]].Value.ProviderInstrumentId, Is.EqualTo(10001));
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(handler.Requests[1].RequestUri!.Query, Is.EqualTo("?instrumentIds=10001"));
        });
    }

    [Test]
    public async Task GetCurrentQuotesAsync_EmptyBatch_DoesNotRequestPrices()
    {
        ScriptedHttpMessageHandler handler = new();
        EToroMarketDataProvider provider = Provider(handler, []);

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync([], CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(results, Is.Empty);
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    [Test]
    public void GetCurrentQuotesAsync_OverBatchLimit_RejectsBeforeMakingRequests()
    {
        MarketInstrumentIdentity[] instruments = Instruments(1001);
        ScriptedHttpMessageHandler handler = new();
        EToroMarketDataProvider provider = Provider(handler, instruments);

        Assert.That(async () => await provider.GetCurrentQuotesAsync(instruments, CancellationToken.None),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public void GetCurrentQuotesAsync_Cancelled_PropagatesCancellationWithoutRequests()
    {
        MarketInstrumentIdentity[] instruments = Instruments(2);
        ScriptedHttpMessageHandler handler = new();
        EToroMarketDataProvider provider = Provider(handler, instruments);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.That(async () => await provider.GetCurrentQuotesAsync(instruments, cancelled.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(handler.Requests, Is.Empty);
    }

    [TestCase("timeout", "price_collection.provider_timeout")]
    [TestCase("transport", "market_data.provider_unavailable")]
    [TestCase("malformed", "market_data.provider_unavailable")]
    [TestCase("server", "market_data.provider_unavailable")]
    public async Task HandleAsync_ResolutionFailureThenRatesFailure_PersistsDistinctErrors(
        string failure, string expectedError)
    {
        MarketInstrumentIdentity[] instruments = Instruments(2);
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody());
        handler.EnqueueResponse(HttpStatusCode.OK,
            EToroPayloads.SearchBody((10001, instruments[1].Symbol, 5, "Test Exchange")));
        EnqueueRatesFailure(handler, failure);
        EToroMarketDataProvider provider = Provider(handler, instruments, cachedCount: 0,
            configure: options => options.MaxRetryAttempts = 0);
        InMemoryPriceCollectionStore store = new(instruments);

        PriceCollectionRunResult result = await Collector(provider, store, instruments)
            .HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
            Assert.That(store.Attempts.Select(attempt => attempt.ErrorCode),
                Is.EqualTo(new[] { MarketDataErrors.InstrumentMappingUnresolved.Code, expectedError }));
            Assert.That(result.Items.All(item => item.Outcome == PriceCollectionOutcome.Failed), Is.True);
            Assert.That(store.Observations, Is.Empty);
        });
    }

    [Test]
    public async Task GetCurrentQuotesAsync_InvalidCurrencyThenRatesTimeout_PreservesValidationError()
    {
        MarketInstrumentIdentity[] instruments = Instruments(2);
        ScriptedHttpMessageHandler handler = new();
        handler.Enqueue((_, _) => throw new TimeoutException("Synthetic timeout"));
        EToroMarketDataProvider provider = Provider(handler, instruments);
        MarketInstrumentIdentity invalid = instruments[0] with { QuoteCurrency = "GBP" };

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> result =
            await provider.GetCurrentQuotesAsync([invalid, instruments[1]], CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result[invalid].Error.Code, Is.EqualTo(MarketDataErrors.QuoteCurrencyConflict.Code));
            Assert.That(result[instruments[1]].Error.Code, Is.EqualTo(MarketDataErrors.ProviderTimeout.Code));
            Assert.That(handler.Requests[0].RequestUri!.Query, Is.EqualTo("?instrumentIds=10001"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task HandleAsync_ColdDiscovery_PreservesProgressAndRecoversWithinSameSamplingSlot(bool expiredCache)
    {
        MarketInstrumentIdentity[] instruments = Instruments(120);
        ScriptedHttpMessageHandler handler = new();
        EnqueueDiscoveryAndQuotes(handler, instruments);
        EToroMarketDataProvider provider = Provider(handler, instruments,
            cachedCount: expiredCache ? instruments.Length : 0,
            configure: options => options.MaxExecutionTimeSeconds = 15);
        if (expiredCache)
        {
            _clock.Advance(Duration.FromHours(1));
        }

        Instant slot = _clock.GetCurrentInstant();
        InMemoryPriceCollectionStore store = new(instruments);
        CollectDuePricesHandler collector = Collector(provider, store, instruments);
        PriceCollectionRunResult first = await collector.HandleAsync(new CollectDuePrices(), CancellationToken.None);
        int firstRequests = handler.Requests.Count;
        int firstObservations = store.Observations.Count;
        _clock.Advance(Duration.FromSeconds(15));
        PriceCollectionRunResult waiting = await collector.HandleAsync(new CollectDuePrices(), CancellationToken.None);
        int waitingRequests = handler.Requests.Count;
        _clock.Advance(Duration.FromSeconds(45));

        PriceCollectionRunResult recovered = await collector.HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(firstRequests, Is.EqualTo(120));
            Assert.That(firstObservations, Is.EqualTo(60));
            Assert.That(first.Items.Count(item => item.ErrorCode == MarketDataErrors.ProviderRequestDeferred.Code), Is.EqualTo(60));
            Assert.That(waitingRequests, Is.EqualTo(firstRequests));
            Assert.That(waiting.Items.All(item => item.ErrorCode == MarketDataErrors.ProviderRequestDeferred.Code), Is.True);
            Assert.That(recovered.Items, Has.Count.EqualTo(60));
            Assert.That(recovered.Items.All(item => item.Outcome == PriceCollectionOutcome.Observed), Is.True);
            Assert.That(store.Observations, Has.Count.EqualTo(120));
            Assert.That(store.Observations.All(observation => observation.ScheduledFor == slot), Is.True);
            Assert.That(store.Observations.Select(observation => observation.InstrumentId).Distinct().Count(), Is.EqualTo(120));
            Assert.That(handler.Requests, Has.Count.EqualTo(240));
            Assert.That(handler.Requests[0].RequestUri!.AbsolutePath, Is.EqualTo("/api/v1/market-data/search"));
            Assert.That(handler.Requests[1].RequestUri!.AbsolutePath, Is.EqualTo("/api/v2/market-data/rates"));
        });
    }

    [Test]
    public async Task GetCurrentQuotesAsync_ColdInstrumentBeforeWarmInstrument_PricesWarmInstrumentFirst()
    {
        MarketInstrumentIdentity[] instruments = Instruments(2);
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, Rates(1));
        EnqueueDiscoveryAndQuotes(handler, [instruments[1]], 10001);
        EToroMarketDataProvider provider = Provider(handler, instruments, cachedCount: 1);

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync([instruments[1], instruments[0]], CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(results.Values.All(result => result.IsSuccess), Is.True);
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
            Assert.That(handler.Requests[0].RequestUri!.Query, Is.EqualTo("?instrumentIds=10000"));
            Assert.That(handler.Requests[1].RequestUri!.AbsolutePath, Is.EqualTo("/api/v1/market-data/search"));
        });
    }

    [Test]
    public async Task HandleAsync_Provider429_KeepsCooldownAndSamplingSlotSuppression()
    {
        MarketInstrumentIdentity[] instruments = Instruments(2);
        ScriptedHttpMessageHandler handler = new();
        HttpResponseMessage throttled = new(HttpStatusCode.TooManyRequests);
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
        handler.Enqueue(throttled);
        EToroMarketDataProvider provider = Provider(handler, instruments, cachedCount: 0,
            configure: options => options.MaxExecutionTimeSeconds = 15);
        InMemoryPriceCollectionStore store = new(instruments);
        CollectDuePricesHandler collector = Collector(provider, store, instruments);
        PriceCollectionRunResult first = await collector.HandleAsync(new CollectDuePrices(), CancellationToken.None);
        bool cooldownAllowsRequest = await _gate.WaitForSlotAsync(EToroRateLimitPolicies.MarketData,
            Now.Plus(Duration.FromSeconds(60)), CancellationToken.None);
        _clock.Advance(Duration.FromSeconds(121));

        PriceCollectionRunResult second = await collector.HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.Items.All(item => item.Outcome == PriceCollectionOutcome.Throttled), Is.True);
            Assert.That(second.Items.All(item => item.Outcome == PriceCollectionOutcome.Throttled), Is.True);
            Assert.That(cooldownAllowsRequest, Is.False);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(store.Attempts, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void GetCurrentQuotesAsync_CancelledDuringRates_PropagatesCancellation()
    {
        MarketInstrumentIdentity[] instruments = Instruments(1);
        ScriptedHttpMessageHandler handler = new();
        using CancellationTokenSource cancellation = new();
        handler.Enqueue((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException();
        });
        EToroMarketDataProvider provider = Provider(handler, instruments);

        Assert.That(async () => await provider.GetCurrentQuotesAsync(instruments, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    public void GetCurrentQuotesAsync_RejectedCredentials_PreservesExceptionContract(HttpStatusCode status)
    {
        MarketInstrumentIdentity[] instruments = Instruments(1);
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(status, "{}");
        EToroMarketDataProvider provider = Provider(handler, instruments);

        Assert.That(async () => await provider.GetCurrentQuotesAsync(instruments, CancellationToken.None),
            Throws.TypeOf<EToroMarketDataException>());
    }

    [Test]
    public void GetCurrentQuotesAsync_MissingCredentials_PreservesExceptionContract()
    {
        MarketInstrumentIdentity[] instruments = Instruments(1);
        ScriptedHttpMessageHandler handler = new();
        EToroMarketDataProvider provider = Provider(handler, instruments, configure: options => options.ApiKey = null);

        Assert.That(async () => await provider.GetCurrentQuotesAsync(instruments, CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task GetCurrentQuotesAsync_DiscoveryUsesLastSlot_RecoversCachedIdWithoutRediscovery()
    {
        for (int request = 0; request < 119; request++)
        {
            await _gate.WaitForSlotAsync(EToroRateLimitPolicies.MarketData,
                Now.Plus(Duration.FromSeconds(15)), CancellationToken.None);
        }

        MarketInstrumentIdentity[] instruments = Instruments(1);
        ScriptedHttpMessageHandler handler = new();
        EnqueueDiscoveryAndQuotes(handler, instruments);
        EToroMarketDataProvider provider = Provider(handler, instruments, cachedCount: 0,
            configure: options => options.MaxExecutionTimeSeconds = 15);
        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> first =
            await provider.GetCurrentQuotesAsync(instruments, CancellationToken.None);
        int firstRequests = handler.Requests.Count;
        _clock.Advance(Duration.FromSeconds(60));

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> recovered =
            await provider.GetCurrentQuotesAsync(instruments, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first[instruments[0]].Error.Code, Is.EqualTo(MarketDataErrors.ProviderRequestDeferred.Code));
            Assert.That(firstRequests, Is.EqualTo(1));
            Assert.That(recovered[instruments[0]].IsSuccess, Is.True);
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(handler.Requests[1].RequestUri!.AbsolutePath, Is.EqualTo("/api/v2/market-data/rates"));
        });
    }

    [Test]
    public async Task GetCurrentQuotesAsync_Provider429ThenLocalPacing_PreservesProviderThrottle()
    {
        for (int request = 0; request < 119; request++)
        {
            await _gate.WaitForSlotAsync(EToroRateLimitPolicies.MarketData,
                Now.Plus(Duration.FromSeconds(15)), CancellationToken.None);
        }

        MarketInstrumentIdentity[] instruments = Instruments(1);
        ScriptedHttpMessageHandler handler = new();
        HttpResponseMessage throttled = new(HttpStatusCode.TooManyRequests);
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
        handler.Enqueue(throttled);
        EToroMarketDataProvider provider = Provider(handler, instruments,
            configure: options => options.MaxExecutionTimeSeconds = 15);

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync(instruments, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(results[instruments[0]].Error.Code, Is.EqualTo(MarketDataErrors.ProviderThrottled.Code));
            Assert.That(results[instruments[0]].Error.Extensions!["scope"], Is.EqualTo("provider_429"));
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(_clock.GetCurrentInstant(), Is.EqualTo(Now.Plus(Duration.FromSeconds(1))));
        });
    }

    private CollectDuePricesHandler Collector(
        EToroMarketDataProvider provider,
        InMemoryPriceCollectionStore store,
        MarketInstrumentIdentity[] instruments)
    {
        EToroMarketDataOptions options = new()
        {
            QuoteCurrencyMap = new Dictionary<string, Dictionary<string, string>>
            {
                ["XTEST"] = instruments.ToDictionary(instrument => instrument.Symbol, _ => "USD")
            }
        };
        CompositeNativeQuoteUnitMap units = new(new NullListingConfigurationStore(), Options.Create(options));
        WeeklyScheduleMarketSessionPolicy sessions = new(new Dictionary<string, ExchangeSession>
        {
            ["XTEST"] = new ExchangeSession(null, true, [])
        });
        return new CollectDuePricesHandler(store, store, provider, units,
            new PerInstrumentSamplingPolicy(), sessions, _clock, TimeProvider.System, new PriceCollectionOptions());
    }

    private static void EnqueueDiscoveryAndQuotes(
        ScriptedHttpMessageHandler handler, MarketInstrumentIdentity[] instruments, int firstId = 10000)
    {
        for (int index = 0; index < instruments.Length; index++)
        {
            long id = firstId + index;
            handler.EnqueueResponse(HttpStatusCode.OK,
                EToroPayloads.SearchBody((id, instruments[index].Symbol, 5, "Test Exchange")));
            handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.RatesBody(
                (id, "12.345", "12.456", "2026-01-05T12:00:00Z", "realtime")));
        }
    }

    private static void EnqueueRatesFailure(ScriptedHttpMessageHandler handler, string failure)
    {
        switch (failure)
        {
            case "timeout":
                handler.Enqueue((_, _) => throw new OperationCanceledException("Synthetic request timeout"));
                break;
            case "transport":
                handler.Enqueue((_, _) => throw new HttpRequestException("Synthetic transport failure"));
                break;
            case "malformed":
                handler.EnqueueResponse(HttpStatusCode.OK, "{");
                break;
            case "server":
                handler.EnqueueResponse(HttpStatusCode.ServiceUnavailable, "{}");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failure));
        }
    }

    private EToroMarketDataProvider Provider(
        ScriptedHttpMessageHandler handler,
        MarketInstrumentIdentity[] instruments,
        IListingConfigurationStore? listings = null,
        int? cachedCount = null,
        Action<EToroMarketDataOptions>? configure = null)
    {
        EToroMarketDataOptions options = new()
        {
            ApiKey = "synthetic-key",
            UserKey = "synthetic-user-key",
            MaxExecutionTimeSeconds = 120,
            ExchangeMap = new Dictionary<string, string[]> { ["XTEST"] = ["Test Exchange"] },
            QuoteCurrencyMap = new Dictionary<string, Dictionary<string, string>>
            {
                ["XTEST"] = instruments.ToDictionary(instrument => instrument.Symbol, _ => "USD")
            }
        };
        configure?.Invoke(options);
        EToroInstrumentIdCache cache = new(_clock);
        if (listings is null)
        {
            for (int index = 0; index < (cachedCount ?? instruments.Length); index++)
            {
                cache.Store("XTEST", instruments[index].Symbol, "USD", 10000 + index, TimeSpan.FromHours(1));
            }
        }

        return new EToroMarketDataProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://public-api.test/") },
            Options.Create(options), _gate, cache,
            new CompositeNativeQuoteUnitMap(listings ?? new NullListingConfigurationStore(), Options.Create(options)),
            _clock, _time, NullLogger<EToroMarketDataProvider>.Instance);
    }

    private static MarketInstrumentIdentity[] Instruments(int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => new MarketInstrumentIdentity("XTEST", $"DEMO{index:D5}", "USD"))
            .ToArray();
    }

    private static string Rates(int count)
    {
        return EToroPayloads.RatesBody(Enumerable.Range(10000, count)
            .Reverse()
            .Select(id => ((long)id, "12.345", "12.456", "2026-01-05T12:00:00Z", "realtime"))
            .ToArray());
    }
}
