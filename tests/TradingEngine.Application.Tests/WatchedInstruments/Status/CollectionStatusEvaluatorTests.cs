using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.PriceObservations;
using TradingEngine.Application.Tests.Listings;
using TradingEngine.Application.Tests.PriceCollection;
using TradingEngine.Application.WatchedInstruments.Status;
using TradingEngine.Domain.Instruments;

namespace TradingEngine.Application.Tests.WatchedInstruments.Status;

[TestFixture]
public sealed class CollectionStatusEvaluatorTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 1, 2, 9, 30);
    private static readonly Guid InstrumentId =
        Guid.Parse("4b6e8f2a-1c3d-4e5f-9a8b-7c6d5e4f3a2b");

    [Test]
    public async Task EvaluateAsync_WhenNotMonitored_ReturnsNotMonitored()
    {
        CollectionStatusEvaluator evaluator = CreateEvaluator();

        CollectionStatusInfo result = await evaluator.EvaluateAsync(
            CreateInstrument(MonitoringState.Configured),
            CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(InstrumentCollectionStatus.NotMonitored));
    }

    [Test]
    public async Task EvaluateAsync_WhenCollectorDisabled_ReturnsCollectorDisabled()
    {
        CollectionStatusEvaluator evaluator = CreateEvaluator(
            options: new PriceCollectionOptions { Enabled = false });

        CollectionStatusInfo result = await evaluator.EvaluateAsync(
            CreateInstrument(MonitoringState.Monitored),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Status,
                Is.EqualTo(InstrumentCollectionStatus.CollectorDisabled));
            Assert.That(result.ReasonCode, Is.EqualTo("price_collection.disabled"));
        });
    }

    [Test]
    public async Task EvaluateAsync_WhenSessionUnconfigured_ReturnsUnsupportedVenue()
    {
        StubMarketSessionPolicy sessions = new()
        {
            Responder = (_, _) => ExchangeSessionStatus.Unconfigured
        };
        CollectionStatusEvaluator evaluator = CreateEvaluator(sessions: sessions);

        CollectionStatusInfo result = await evaluator.EvaluateAsync(
            CreateInstrument(MonitoringState.Monitored),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Status,
                Is.EqualTo(InstrumentCollectionStatus.UnsupportedVenue));
            Assert.That(
                result.ReasonCode,
                Is.EqualTo(PriceCollectionErrors.ExchangeSessionUnconfigured.Code));
        });
    }

    [Test]
    public async Task EvaluateAsync_WhenUnitUnconfigured_ReturnsNativeUnitUnconfirmed()
    {
        CollectionStatusEvaluator evaluator = CreateEvaluator(
            quoteUnits: StubNativeQuoteUnitMap.Unconfigured());

        CollectionStatusInfo result = await evaluator.EvaluateAsync(
            CreateInstrument(MonitoringState.Monitored),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Status,
                Is.EqualTo(InstrumentCollectionStatus.NativeUnitUnconfirmed));
            Assert.That(
                result.ReasonCode,
                Is.EqualTo(MarketDataErrors.QuoteCurrencyUnconfigured.Code));
        });
    }

    [Test]
    public async Task EvaluateAsync_WhenUnitResolutionFails_ReturnsNativeUnitUnconfirmed()
    {
        StubNativeQuoteUnitMap quoteUnits = new()
        {
            Result = MarketDataErrors.ProviderThrottled
        };
        CollectionStatusEvaluator evaluator = CreateEvaluator(quoteUnits: quoteUnits);

        CollectionStatusInfo result = await evaluator.EvaluateAsync(
            CreateInstrument(MonitoringState.Monitored),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Status,
                Is.EqualTo(InstrumentCollectionStatus.NativeUnitUnconfirmed));
            Assert.That(
                result.ReasonCode,
                Is.EqualTo(MarketDataErrors.ProviderThrottled.Code));
        });
    }

    [Test]
    public async Task EvaluateAsync_WhenSourcesConflict_ReturnsNativeUnitConflict()
    {
        StubNativeQuoteUnitMap quoteUnits = new()
        {
            Result = new NativeQuoteUnitResolution(null, null, "GBP", true)
        };
        CollectionStatusEvaluator evaluator = CreateEvaluator(quoteUnits: quoteUnits);

        CollectionStatusInfo result = await evaluator.EvaluateAsync(
            CreateInstrument(MonitoringState.Monitored),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Status,
                Is.EqualTo(InstrumentCollectionStatus.NativeUnitConflict));
            Assert.That(
                result.ReasonCode,
                Is.EqualTo(MarketDataErrors.QuoteCurrencySourceConflict.Code));
        });
    }

    [Test]
    public async Task EvaluateAsync_WhenUnitDiffersFromInstrument_ReturnsNativeUnitConflict()
    {
        StubNativeQuoteUnitMap quoteUnits = StubNativeQuoteUnitMap.Configured("USD");
        CollectionStatusEvaluator evaluator = CreateEvaluator(quoteUnits: quoteUnits);

        CollectionStatusInfo result = await evaluator.EvaluateAsync(
            CreateInstrument(MonitoringState.Monitored),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Status,
                Is.EqualTo(InstrumentCollectionStatus.NativeUnitConflict));
            Assert.That(
                result.ReasonCode,
                Is.EqualTo(MarketDataErrors.QuoteCurrencyConflict.Code));
        });
    }

    [Test]
    public async Task EvaluateAsync_WhenSessionClosed_ReturnsSessionClosed()
    {
        StubMarketSessionPolicy sessions = new()
        {
            Responder = (_, _) => ExchangeSessionStatus.Closed
        };
        CapturingPriceObservationStore observations = new();
        observations.LatestAttempts[InstrumentId] = CreateAttempt();
        CollectionStatusEvaluator evaluator = CreateEvaluator(
            sessions: sessions,
            observations: observations);

        CollectionStatusInfo result = await evaluator.EvaluateAsync(
            CreateInstrument(MonitoringState.Monitored),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Status,
                Is.EqualTo(InstrumentCollectionStatus.SessionClosed));
            Assert.That(
                result.ReasonCode,
                Is.EqualTo("price_collection.session_closed"));
            Assert.That(result.LastAttemptOutcome, Is.EqualTo(PriceCollectionOutcome.Observed));
            Assert.That(result.LastAttemptAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public async Task EvaluateAsync_WhenNoObservations_ReturnsAwaitingFirstObservation()
    {
        CapturingPriceObservationStore observations = new();
        observations.LatestAttempts[InstrumentId] = CreateAttempt();
        StubPriceObservationHistoryReader history = new()
        {
            Result = new PriceObservationHistory(
                InstrumentId,
                null,
                [],
                1,
                null,
                null)
        };
        CollectionStatusEvaluator evaluator = CreateEvaluator(
            observations: observations,
            history: history);

        CollectionStatusInfo result = await evaluator.EvaluateAsync(
            CreateInstrument(MonitoringState.Monitored),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Status,
                Is.EqualTo(InstrumentCollectionStatus.AwaitingFirstObservation));
            Assert.That(result.LastObservedAt, Is.Null);
            Assert.That(result.LastAttemptOutcome, Is.EqualTo(PriceCollectionOutcome.Observed));
        });
    }

    [Test]
    public async Task EvaluateAsync_WhenObserved_ReturnsCollectingWithTimestamps()
    {
        PriceObservationRecord latest = new(
            Now.Minus(Duration.FromSeconds(60)),
            Now.Minus(Duration.FromSeconds(55)),
            null,
            100m,
            101m,
            "GBP",
            "eToro",
            MarketQuoteTimeliness.Realtime);
        StubPriceObservationHistoryReader history = new()
        {
            Result = new PriceObservationHistory(
                InstrumentId,
                latest,
                [latest],
                1,
                null,
                null)
        };
        CapturingPriceObservationStore observations = new();
        observations.LatestAttempts[InstrumentId] = CreateAttempt();
        CollectionStatusEvaluator evaluator = CreateEvaluator(
            observations: observations,
            history: history);

        CollectionStatusInfo result = await evaluator.EvaluateAsync(
            CreateInstrument(MonitoringState.Monitored),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Status,
                Is.EqualTo(InstrumentCollectionStatus.Collecting));
            Assert.That(result.LastObservedAt, Is.EqualTo(latest.RetrievedAt));
            Assert.That(result.LastAttemptAt, Is.EqualTo(Now));
            Assert.That(result.LastAttemptOutcome, Is.EqualTo(PriceCollectionOutcome.Observed));
        });
    }

    private static CollectionStatusEvaluator CreateEvaluator(
        StubNativeQuoteUnitMap? quoteUnits = null,
        StubMarketSessionPolicy? sessions = null,
        CapturingPriceObservationStore? observations = null,
        StubPriceObservationHistoryReader? history = null,
        PriceCollectionOptions? options = null)
    {
        return new CollectionStatusEvaluator(
            quoteUnits ?? StubNativeQuoteUnitMap.Configured("GBP"),
            sessions ?? new StubMarketSessionPolicy(),
            observations ?? new CapturingPriceObservationStore(),
            history ?? new StubPriceObservationHistoryReader(),
            options ?? new PriceCollectionOptions { Enabled = true },
            new FakeClock(Now));
    }

    private static WatchedInstrument CreateInstrument(MonitoringState state)
    {
        return WatchedInstrument.Restore(
            InstrumentId,
            "DEMO-2",
            "XTEST",
            "GBP",
            state,
            60,
            Now,
            Now).Value;
    }

    private static PriceCollectionAttempt CreateAttempt()
    {
        return new PriceCollectionAttempt(
            InstrumentId,
            Now.Minus(Duration.FromSeconds(5)),
            PriceCollectionOutcome.Observed,
            null,
            "eToro",
            "synthetic-request-1",
            Guid.Parse("9c7f2a31-84d5-4e6b-a1c2-3d4e5f607182"),
            Now,
            Now);
    }
}
