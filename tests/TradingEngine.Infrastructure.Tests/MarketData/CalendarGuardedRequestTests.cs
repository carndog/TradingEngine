using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Domain.Results;
using TradingEngine.Infrastructure.MarketData;
using TradingEngine.Infrastructure.MarketSessions;

namespace TradingEngine.Infrastructure.Tests.MarketData;

[TestFixture]
public sealed class CalendarGuardedRequestTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 12, 24, 12, 29, 50);
    private static readonly MarketInstrumentIdentity Equity = new("XLON", "DEMOEQ", "USD");
    private static readonly MarketInstrumentIdentity Continuous = new("XCONT", "DEMOCONT", "USD");
    private FakeClock _clock = null!;
    private ManualTimeProvider _time = null!;
    private ProviderRateGate _gate = null!;
    private ConfiguredExchangeTradingCalendar _calendar = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeClock(Now);
        _time = new ManualTimeProvider(delay => _clock.Advance(Duration.FromTimeSpan(delay)));
        _gate = new ProviderRateGate(_clock, _time);
        _calendar = new ConfiguredExchangeTradingCalendar(new ConfigurationBuilder().Build(),
            NullLogger<ConfiguredExchangeTradingCalendar>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _calendar.Dispose();
    }

    [Test]
    public async Task GetCurrentQuotesAsync_CooldownCrossesEarlyClose_FiltersActualHttpBatch()
    {
        _gate.RegisterCooldown(EToroRateLimitPolicies.MarketData.Name, Now + Duration.FromSeconds(11));
        ScriptedHttpMessageHandler http = new();
        http.EnqueueResponse(HttpStatusCode.OK, Rates());
        EToroMarketDataProvider provider = Provider(http);

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync([Equity, Continuous], CancellationToken.None, Guard());

        Assert.That(http.Requests, Has.Count.EqualTo(1));
        Assert.That(http.Requests[0].RequestUri!.Query, Is.EqualTo("?instrumentIds=11"));
        Assert.That(results[Equity].Error.Code, Is.EqualTo("price_collection.outside_replacement_session"));
        Assert.That(results[Continuous].IsSuccess, Is.True);
        Assert.That(provider.RequestStatistics.RateBatches, Is.EqualTo(new Dictionary<int, long> { [1] = 1 }));
    }

    [Test]
    public async Task GetCurrentQuotesAsync_RetryAfterCrossesEarlyClose_RebuildsActualHttpBatch()
    {
        ScriptedHttpMessageHandler http = new();
        HttpResponseMessage throttled = new(HttpStatusCode.TooManyRequests);
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(11));
        http.Enqueue(throttled);
        http.EnqueueResponse(HttpStatusCode.OK, Rates());
        EToroMarketDataProvider provider = Provider(http);

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync([Equity, Continuous], CancellationToken.None, Guard());

        Assert.That(http.Requests, Has.Count.EqualTo(2));
        Assert.That(http.Requests[0].RequestUri!.Query, Is.EqualTo("?instrumentIds=10,11"));
        Assert.That(http.Requests[1].RequestUri!.Query, Is.EqualTo("?instrumentIds=11"));
        Assert.That(results[Equity].Error.Code, Is.EqualTo("price_collection.outside_replacement_session"));
        Assert.That(results[Continuous].IsSuccess, Is.True);
        Assert.That(provider.RequestStatistics.RatesRequests, Is.EqualTo(2));
    }

    [Test]
    public async Task GetCurrentQuotesAsync_AllClosedAfterCooldown_MakesNoHttpRequest()
    {
        _gate.RegisterCooldown(EToroRateLimitPolicies.MarketData.Name, Now + Duration.FromSeconds(11));
        ScriptedHttpMessageHandler http = new();
        EToroMarketDataProvider provider = Provider(http);

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync([Equity], CancellationToken.None, Guard());

        Assert.That(http.Requests, Is.Empty);
        Assert.That(results[Equity].Error.Code, Is.EqualTo("price_collection.outside_replacement_session"));
        Assert.That(provider.RequestStatistics.RatesRequests, Is.Zero);
    }

    [Test]
    public async Task GetCurrentQuotesAsync_DiscoveryFinishesAfterClose_SuppressesRatesSubBatch()
    {
        ScriptedHttpMessageHandler http = new();
        http.Enqueue((_, _) =>
        {
            _clock.Advance(Duration.FromSeconds(11));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(EToroPayloads.SearchBody((10, "DEMOEQ", 7, "Test Exchange")))
            });
        });
        EToroMarketDataProvider provider = Provider(http, false);

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync([Equity], CancellationToken.None, Guard());

        Assert.That(http.Requests, Has.Count.EqualTo(1));
        Assert.That(http.Requests[0].RequestUri!.AbsolutePath, Is.EqualTo("/api/v1/market-data/search"));
        Assert.That(results[Equity].Error.Code, Is.EqualTo("price_collection.outside_replacement_session"));
        Assert.That(provider.RequestStatistics.RatesRequests, Is.Zero);
    }

    [Test]
    public async Task GetCurrentQuotesAsync_SearchRetryCrossesClose_SuppressesNextSearch()
    {
        ScriptedHttpMessageHandler http = new();
        HttpResponseMessage throttled = new(HttpStatusCode.TooManyRequests);
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(11));
        http.Enqueue(throttled);
        EToroMarketDataProvider provider = Provider(http, false);

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync([Equity], CancellationToken.None, Guard());

        Assert.That(http.Requests, Has.Count.EqualTo(1));
        Assert.That(results[Equity].Error.Code, Is.EqualTo("price_collection.outside_replacement_session"));
        Assert.That(provider.RequestStatistics.SearchRequests, Is.EqualTo(1));
    }

    [Test]
    public async Task GetCurrentQuotesAsync_ServiceSuspendsDuringCooldown_DropsExpiredRunForAllAssets()
    {
        _time = new ManualTimeProvider(_ => _clock.Advance(Duration.FromDays(4)));
        _gate = new ProviderRateGate(_clock, _time);
        _gate.RegisterCooldown(EToroRateLimitPolicies.MarketData.Name, Now + Duration.FromSeconds(11));
        ScriptedHttpMessageHandler http = new();
        EToroMarketDataProvider provider = Provider(http);

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync([Equity, Continuous], CancellationToken.None, Guard());

        Assert.That(http.Requests, Is.Empty);
        Assert.That(results.Values.All(result => result.IsFailure && result.Error.Code == PriceCollectionErrors.DeadlineExceeded.Code), Is.True);
    }

    [Test]
    public async Task GetCurrentQuotesAsync_SlotExpiresDuringCooldown_DropsExpiredSlotBeforeHttp()
    {
        _gate.RegisterCooldown(EToroRateLimitPolicies.MarketData.Name, Now + Duration.FromSeconds(11));
        ScriptedHttpMessageHandler http = new();
        EToroMarketDataProvider provider = Provider(http);

        IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> results =
            await provider.GetCurrentQuotesAsync([Continuous], CancellationToken.None, Guard(Duration.FromSeconds(10)));

        Assert.That(http.Requests, Is.Empty);
        Assert.That(results[Continuous].Error.Code, Is.EqualTo(PriceCollectionErrors.SlotExpired.Code));
    }

    private PriceCollectionRequestGuard Guard(Duration? expiry = null)
    {
        HashSet<IsoDayOfWeek> days = [IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday,
            IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday];
        CalendarMarketSessionPolicy policy = new(new Dictionary<string, ExchangeSession>
        {
            ["XLON"] = new(DateTimeZoneProviders.Tzdb["Europe/London"], false,
                [new SessionWindow(days, new LocalTime(8, 0), new LocalTime(16, 30))], true),
            ["XCONT"] = new(null, true, [])
        }, _calendar);
        return new PriceCollectionRequestGuard(policy, _clock, Now + Duration.FromSeconds(90),
            new Dictionary<MarketInstrumentIdentity, Instant>
            {
                [Equity] = Now + (expiry ?? Duration.FromHours(1)),
                [Continuous] = Now + (expiry ?? Duration.FromHours(1))
            });
    }

    private EToroMarketDataProvider Provider(ScriptedHttpMessageHandler http, bool cached = true)
    {
        EToroMarketDataOptions options = new()
        {
            ApiKey = "synthetic-key", UserKey = "synthetic-user", MaxExecutionTimeSeconds = 120,
            ExchangeMap = new Dictionary<string, string[]> { ["XLON"] = ["Test Exchange"], ["XCONT"] = ["Test Exchange"] },
            QuoteCurrencyMap = new Dictionary<string, Dictionary<string, string>>
            {
                ["XLON"] = new() { ["DEMOEQ"] = "USD" }, ["XCONT"] = new() { ["DEMOCONT"] = "USD" }
            }
        };
        EToroInstrumentIdCache cache = new(_clock);
        if (cached)
        {
            cache.Store("XLON", "DEMOEQ", "USD", 10, TimeSpan.FromHours(1));
            cache.Store("XCONT", "DEMOCONT", "USD", 11, TimeSpan.FromHours(1));
        }
        return new EToroMarketDataProvider(new HttpClient(http) { BaseAddress = new Uri("https://example.test/") },
            Options.Create(options), _gate, cache,
            new CompositeNativeQuoteUnitMap(new NullListingConfigurationStore(), Options.Create(options)),
            _clock, _time, NullLogger<EToroMarketDataProvider>.Instance);
    }

    private static string Rates()
    {
        return EToroPayloads.RatesBody((11, "12.3", "12.4", "2026-12-24T12:29:50Z", "realtime"));
    }
}
