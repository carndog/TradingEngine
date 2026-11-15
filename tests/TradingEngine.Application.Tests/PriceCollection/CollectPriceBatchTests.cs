using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.Tests.Listings;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.PriceCollection;

[TestFixture]
public sealed class CollectPriceBatchTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 1, 5, 12, 0);
    private FakeClock _clock = null!;
    private StubMonitoredInstrumentSource _source = null!;
    private CapturingPriceObservationStore _store = null!;
    private BatchMarketDataProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeClock(Now);
        _source = new StubMonitoredInstrumentSource();
        _store = new CapturingPriceObservationStore();
        _provider = new BatchMarketDataProvider();
        _provider.Single.RetrievedAt = Now;
        _provider.Single.ProviderObservedAt = Now;
    }

    [TestCase(1000, 1)]
    [TestCase(1001, 2)]
    [TestCase(10000, 10)]
    public async Task HandleAsync_DueInstruments_CollectsInBoundedBatchesWithoutChangingSlots(
        int count, int expectedBatches)
    {
        AddInstruments(count);
        CollectDuePricesHandler handler = Handler();

        PriceCollectionRunResult result = await handler.HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.DeadlineReached, Is.False);
            Assert.That(_provider.Batches, Has.Count.EqualTo(expectedBatches));
            Assert.That(_provider.Batches.All(batch => batch.Length <= 1000), Is.True);
            Assert.That(result.Items, Has.Count.EqualTo(count));
            Assert.That(result.Items.All(item => item.Outcome == PriceCollectionOutcome.Observed), Is.True);
            Assert.That(_store.Observations, Has.Count.EqualTo(count));
            Assert.That(_store.Attempts, Has.Count.EqualTo(count));
            Assert.That(_store.Observations.Select(item => item.InstrumentId).Distinct().Count(), Is.EqualTo(count));
            Assert.That(_store.Observations.All(item => item.ScheduledFor == Now), Is.True);
            Assert.That(_store.Observations.All(item => item.Bid == _provider.Single.Bid), Is.True);
        });
    }

    [Test]
    public async Task HandleAsync_ClosedObservedInvalidAndAlreadyThrottled_RequestsOnlyEligibleInstruments()
    {
        AddInstruments(5);
        _source.Instruments[1] = _source.Instruments[1] with { Exchange = "XCLOSED" };
        _store.LatestSlots[_source.Instruments[2].Id] = Now;
        _source.Instruments[3] = _source.Instruments[3] with { QuoteCurrency = "GBP" };
        _store.LatestAttempts[_source.Instruments[4].Id] = new PriceCollectionAttempt(
            _source.Instruments[4].Id, Now, PriceCollectionOutcome.Throttled,
            MarketDataErrors.ProviderThrottled.Code, "eToro", "synthetic-request",
            Guid.Empty, Now, Now);

        PriceCollectionRunResult result = await Handler().HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_provider.Batches, Has.Count.EqualTo(1));
            Assert.That(_provider.Batches[0].Select(item => item.Symbol), Is.EqualTo(new[] { "DEMO00000" }));
            Assert.That(result.Items.Count(item => item.Outcome == PriceCollectionOutcome.Observed), Is.EqualTo(1));
            Assert.That(result.Items.Count(item => item.Outcome == PriceCollectionOutcome.SkippedMarketClosed), Is.EqualTo(1));
            Assert.That(result.Items.Count(item => item.Outcome == PriceCollectionOutcome.Throttled), Is.EqualTo(1));
            Assert.That(result.Items.Count(item => item.ErrorCode == MarketDataErrors.QuoteCurrencyConflict.Code), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleAsync_PartialResponse_RetriesOnlyMissingInstrumentAndKeepsSuccess()
    {
        AddInstruments(2);
        _provider.Responder = async (batch, token) =>
        {
            Dictionary<MarketInstrumentIdentity, Result<MarketQuote>> results = [];
            if (_provider.Batches.Count == 1)
            {
                results[batch[0]] = await _provider.Single.GetCurrentQuoteAsync(batch[0], token);
            }

            return results;
        };

        PriceCollectionRunResult result = await Handler().HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_provider.Batches.Select(batch => batch.Length), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(_provider.Batches[1][0].Symbol, Is.EqualTo("DEMO00001"));
            Assert.That(_store.Observations, Has.Count.EqualTo(1));
            Assert.That(result.Items.Count(item => item.Outcome == PriceCollectionOutcome.Observed), Is.EqualTo(1));
            Assert.That(result.Items.Count(item => item.Outcome == PriceCollectionOutcome.PriceUnavailable), Is.EqualTo(1));
            Assert.That(_store.Attempts, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task HandleAsync_OneBatchFails_RecordsEachFailureAndContinuesNextBatch()
    {
        AddInstruments(3);
        _provider.MaxBatchSize = 2;
        _provider.Responder = async (batch, token) =>
        {
            if (_provider.Batches.Count == 1)
            {
                throw new HttpRequestException("Synthetic transport failure");
            }

            return new Dictionary<MarketInstrumentIdentity, Result<MarketQuote>>
            {
                [batch[0]] = await _provider.Single.GetCurrentQuoteAsync(batch[0], token)
            };
        };

        PriceCollectionRunResult result = await Handler().HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_provider.Batches, Has.Count.EqualTo(2));
            Assert.That(result.Items.Count(item => item.ErrorCode == PriceCollectionErrors.ProviderError.Code), Is.EqualTo(2));
            Assert.That(result.Items.Count(item => item.Outcome == PriceCollectionOutcome.Observed), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleAsync_DuplicateObservations_PreservesPerInstrumentIdempotency()
    {
        AddInstruments(3);
        CollectDuePricesHandler handler = Handler();
        await handler.HandleAsync(new CollectDuePrices(), CancellationToken.None);

        PriceCollectionRunResult result = await handler.HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_store.Observations, Has.Count.EqualTo(3));
            Assert.That(result.Items.All(item => item.Outcome == PriceCollectionOutcome.SkippedDuplicate), Is.True);
        });
    }

    [Test]
    public void HandleAsync_CancelledDuringBatch_PropagatesCancellation()
    {
        AddInstruments(2);
        using CancellationTokenSource cancellation = new();
        _provider.Responder = (_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException();
        };

        Assert.That(async () => await Handler().HandleAsync(new CollectDuePrices(), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(_store.Observations, Is.Empty);
    }

    private void AddInstruments(int count)
    {
        for (int index = 0; index < count; index++)
        {
            _source.Instruments.Add(new MonitoredInstrument(
                Guid.Parse($"00000000-0000-0000-0000-{index:D12}"), "XTEST", $"DEMO{index:D5}", "USD", 900));
        }
    }

    private CollectDuePricesHandler Handler()
    {
        WeeklyScheduleMarketSessionPolicy sessions = new(new Dictionary<string, ExchangeSession>
        {
            ["XTEST"] = new ExchangeSession(null, true, []),
            ["XCLOSED"] = new ExchangeSession(DateTimeZone.Utc, false,
                [new SessionWindow(new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Saturday }, new LocalTime(9, 0), new LocalTime(17, 0))])
        });
        return new CollectDuePricesHandler(_source, _store, _provider,
            StubNativeQuoteUnitMap.Configured("USD"), new ProviderBudgetClampedSamplingPolicy(120, 60),
            sessions, _clock, TimeProvider.System, new PriceCollectionOptions());
    }
}
