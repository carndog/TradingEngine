using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.Listings;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
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

    private EToroMarketDataProvider Provider(
        ScriptedHttpMessageHandler handler,
        MarketInstrumentIdentity[] instruments,
        IListingConfigurationStore? listings = null)
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
        EToroInstrumentIdCache cache = new(_clock);
        if (listings is null)
        {
            for (int index = 0; index < instruments.Length; index++)
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
