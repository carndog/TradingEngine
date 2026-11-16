using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.Tests.Listings;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.PriceCollection;

[TestFixture]
public sealed class CalendarBatchCollectionTests
{
    private static readonly Instant BeforeClose = Instant.FromUtc(2026, 11, 16, 16, 29, 50);
    private FakeClock _clock = null!;
    private StubMonitoredInstrumentSource _source = null!;
    private CapturingPriceObservationStore _store = null!;
    private BatchMarketDataProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeClock(BeforeClose);
        _source = new StubMonitoredInstrumentSource();
        _store = new CapturingPriceObservationStore();
        _provider = new BatchMarketDataProvider();
        _provider.Single.RetrievedAt = BeforeClose;
        _provider.Single.ProviderObservedAt = BeforeClose;
    }

    [TestCase(1000)]
    [TestCase(2)]
    public async Task HandleAsync_CloseBetweenBatches_StopsClosedExchangeAtConfiguredSize(int batchSize)
    {
        _provider.MaxBatchSize = batchSize;
        Add(batchSize + 2, "XLON");
        _provider.Responder = async (batch, token) =>
        {
            Dictionary<MarketInstrumentIdentity, Result<MarketQuote>> quotes = [];
            foreach (MarketInstrumentIdentity identity in batch)
            {
                quotes[identity] = await _provider.Single.GetCurrentQuoteAsync(identity, token);
            }
            _clock.Advance(Duration.FromSeconds(10));
            return quotes;
        };

        PriceCollectionRunResult result = await Handler().HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.That(_provider.Batches, Has.Count.EqualTo(1));
        Assert.That(_provider.Batches[0], Has.Length.EqualTo(batchSize));
        Assert.That(result.Items.Count(item => item.Outcome == PriceCollectionOutcome.Observed), Is.EqualTo(batchSize));
        Assert.That(result.Items.Count(item => item.Outcome == PriceCollectionOutcome.SkippedMarketClosed), Is.EqualTo(2));
    }

    [Test]
    public async Task HandleAsync_CloseDuringPreparation_FiltersOnlyClosedExchangeFromPendingBatch()
    {
        Add(1, "XLON");
        Add(1, "XNAS");
        Add(1, "XCONT");
        StubNativeQuoteUnitMap units = StubNativeQuoteUnitMap.Configured("USD");
        units.Responder = (symbol, _) =>
        {
            if (symbol == "DEMO00002")
            {
                _clock.Advance(Duration.FromSeconds(10));
            }
            return Task.FromResult<Result<NativeQuoteUnitResolution>>(new NativeQuoteUnitResolution("USD", null, "USD", false));
        };

        PriceCollectionRunResult result = await Handler(units).HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.That(_provider.Batches.Single().Select(identity => identity.Exchange), Is.EquivalentTo(new[] { "XNAS", "XCONT" }));
        Assert.That(result.Items.Single(item => item.InstrumentId == _source.Instruments[0].Id).Outcome,
            Is.EqualTo(PriceCollectionOutcome.SkippedMarketClosed));
    }

    [Test]
    public async Task HandleAsync_MultiDaySuspension_AbandonsExpiredWorkAndNextRunUsesNewDate()
    {
        Add(1, "XLON");
        Add(1, "XCONT");
        StubNativeQuoteUnitMap units = StubNativeQuoteUnitMap.Configured("USD");
        bool suspended = false;
        units.Responder = (_, _) =>
        {
            if (suspended is false)
            {
                suspended = true;
                _clock.Advance(Duration.FromDays(6));
            }
            return Task.FromResult<Result<NativeQuoteUnitResolution>>(new NativeQuoteUnitResolution("USD", null, "USD", false));
        };

        PriceCollectionRunResult expired = await Handler(units).HandleAsync(new CollectDuePrices(), CancellationToken.None);
        Assert.That(expired.DeadlineReached, Is.True);
        Assert.That(_provider.Batches, Is.Empty);
        _provider.Single.RetrievedAt = _clock.GetCurrentInstant();
        _provider.Single.ProviderObservedAt = _clock.GetCurrentInstant();
        PriceCollectionRunResult resumed = await Handler().HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.That(_provider.Batches.Single().Select(identity => identity.Exchange), Is.EqualTo(new[] { "XCONT" }));
        Assert.That(resumed.Items.Count(item => item.Outcome == PriceCollectionOutcome.SkippedMarketClosed), Is.EqualTo(1));
        Assert.That(_store.Observations.Single().ScheduledFor, Is.GreaterThan(BeforeClose));
    }

    [Test]
    public async Task HandleAsync_UnavailableRetryCrossesClose_DoesNotRequestAgain()
    {
        Add(1, "XLON");
        _provider.Responder = (batch, _) =>
        {
            _clock.Advance(Duration.FromSeconds(10));
            return Task.FromResult<IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>>>(
                batch.ToDictionary(identity => identity, _ => Result<MarketQuote>.Failure(MarketDataErrors.PriceUnavailable)));
        };

        PriceCollectionRunResult result = await Handler().HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.That(_provider.Batches, Has.Count.EqualTo(1));
        Assert.That(_store.Observations, Is.Empty);
        Assert.That(result.Items.Single().ErrorCode, Is.EqualTo("price_collection.session_closed"));
    }

    [Test]
    public async Task HandleAsync_SlotExpiresBeforeRequest_CountsExpiredSlotOnceAcrossPolls()
    {
        Add(1, "XCONT");
        _source.Instruments[0] = _source.Instruments[0] with { SamplingIntervalSeconds = 10 };
        StubNativeQuoteUnitMap units = StubNativeQuoteUnitMap.Configured("USD");
        bool advanced = false;
        units.Responder = (_, _) =>
        {
            if (advanced is false)
            {
                advanced = true;
                _clock.Advance(Duration.FromSeconds(11));
            }
            return Task.FromResult<Result<NativeQuoteUnitResolution>>(new NativeQuoteUnitResolution("USD", null, "USD", false));
        };
        CollectDuePricesHandler handler = Handler(units);

        PriceCollectionRunResult expired = await handler.HandleAsync(new CollectDuePrices(), CancellationToken.None);
        Assert.That(_provider.Batches, Is.Empty);
        _provider.Single.RetrievedAt = _clock.GetCurrentInstant();
        PriceCollectionRunResult next = await handler.HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.That(expired.Diagnostics.ExpiredPendingSlots, Is.EqualTo(1));
        Assert.That(next.Diagnostics.ExpiredPendingSlots, Is.Zero);
        Assert.That(next.Diagnostics.Persisted, Is.EqualTo(1));
    }

    [Test]
    public void GetBlocked_OneBatch_EvaluatesEachExchangeOnceAtFreshTime()
    {
        MarketInstrumentIdentity[] identities = Enumerable.Range(0, 1000)
            .Select(index => new MarketInstrumentIdentity("XTEST", $"DEMO{index:D4}", "USD")).ToArray();
        StubMarketSessionPolicy policy = new();
        PriceCollectionRequestGuard guard = new(policy, _clock, BeforeClose + Duration.FromDays(10),
            identities.ToDictionary(identity => identity, _ => BeforeClose + Duration.FromDays(10)));

        guard.GetBlocked(identities);
        _clock.Advance(Duration.FromDays(2));
        guard.GetBlocked(identities);

        Assert.That(policy.Calls, Has.Count.EqualTo(2));
        Assert.That(policy.Calls[1].At, Is.EqualTo(_clock.GetCurrentInstant()));
    }

    private void Add(int count, string exchange)
    {
        for (int index = 0; index < count; index++)
        {
            int number = _source.Instruments.Count;
            _source.Instruments.Add(new MonitoredInstrument(Guid.Parse($"00000000-0000-0000-0000-{number:D12}"),
                exchange, $"DEMO{number:D5}", "USD", 3600));
        }
    }

    private CollectDuePricesHandler Handler(StubNativeQuoteUnitMap? units = null)
    {
        HashSet<IsoDayOfWeek> days = [IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday,
            IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday];
        WeeklyScheduleMarketSessionPolicy sessions = new(new Dictionary<string, ExchangeSession>
        {
            ["XLON"] = new(DateTimeZoneProviders.Tzdb["Europe/London"], false,
                [new SessionWindow(days, new LocalTime(8, 0), new LocalTime(16, 30))]),
            ["XNAS"] = new(DateTimeZoneProviders.Tzdb["America/New_York"], false,
                [new SessionWindow(days, new LocalTime(9, 30), new LocalTime(16, 0))]),
            ["XCONT"] = new(null, true, [])
        });
        return new CollectDuePricesHandler(_source, _store, _provider,
            units ?? StubNativeQuoteUnitMap.Configured("USD"), new PerInstrumentSamplingPolicy(),
            sessions, _clock, TimeProvider.System, new PriceCollectionOptions());
    }
}
