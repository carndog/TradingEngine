using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.PriceCollection;

[TestFixture]
public sealed class CollectDuePricesHandlerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 30, 13, 45, 30);
    private static readonly Instant CurrentSlot = Instant.FromUtc(2026, 10, 30, 13, 45, 0);
    private static readonly Instant ClosedNow = Instant.FromUtc(2026, 10, 30, 20, 45, 30);
    private static readonly Instant ClosedSlot = Instant.FromUtc(2026, 10, 30, 20, 45, 0);
    private static readonly Guid InstrumentId =
        Guid.Parse("3f8a2c1d-9e4b-4a5c-b7d6-2e1f0a9b8c7d");
    private static readonly Guid SecondInstrumentId =
        Guid.Parse("7c1d3e5f-2a4b-4c6d-8e9f-1a2b3c4d5e6f");

    private static readonly MonitoredInstrument Instrument =
        new(InstrumentId, "XNAS", "DEMO", "USD", 60);
    private static readonly MonitoredInstrument SecondInstrument =
        new(SecondInstrumentId, "XNAS", "DEMO-2", "USD", 60);

    private FakeClock _clock = null!;
    private StubMonitoredInstrumentSource _source = null!;
    private CapturingPriceObservationStore _store = null!;
    private StubMarketDataProvider _marketData = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeClock(Now);
        _source = new StubMonitoredInstrumentSource();
        _store = new CapturingPriceObservationStore();
        _marketData = new StubMarketDataProvider
        {
            RetrievedAt = Now,
            ProviderObservedAt = Instant.FromUtc(2026, 10, 30, 13, 45, 28)
        };
        _source.Instruments.Add(Instrument);
    }

    [Test]
    public async Task HandleAsync_WhenInstrumentDueAndMarketOpen_RecordsObservationWithProvenance()
    {
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.DeadlineReached, Is.False);
            Assert.That(result.Items, Has.Count.EqualTo(1));

            PriceCollectionItemResult item = result.Items[0];
            Assert.That(item.Outcome, Is.EqualTo(PriceCollectionOutcome.Observed));
            Assert.That(item.ScheduledFor, Is.EqualTo(CurrentSlot));
            Assert.That(item.InstrumentId, Is.EqualTo(InstrumentId));
            Assert.That(item.ErrorCode, Is.Null);
            Assert.That(item.ProviderRequestId, Is.EqualTo("synthetic-request-1"));

            Assert.That(_marketData.Requests, Has.Count.EqualTo(1));
            Assert.That(
                _marketData.Requests[0],
                Is.EqualTo(new MarketInstrumentIdentity("XNAS", "DEMO", "USD")));

            Assert.That(_store.Observations, Has.Count.EqualTo(1));
            ScheduledPriceObservation observation = _store.Observations[0];
            Assert.That(observation.InstrumentId, Is.EqualTo(InstrumentId));
            Assert.That(observation.ScheduledFor, Is.EqualTo(CurrentSlot));
            Assert.That(observation.Provider, Is.EqualTo("eToro"));
            Assert.That(observation.ProviderInstrumentId, Is.EqualTo(4201));
            Assert.That(observation.Bid, Is.EqualTo(123.456789012345678m));
            Assert.That(observation.Ask, Is.EqualTo(124.000000000000001m));
            Assert.That(observation.QuoteCurrency, Is.EqualTo("USD"));
            Assert.That(
                observation.ProviderObservedAt,
                Is.EqualTo(Instant.FromUtc(2026, 10, 30, 13, 45, 28)));
            Assert.That(observation.RetrievedAt, Is.EqualTo(Now));
            Assert.That(observation.Timeliness, Is.EqualTo(MarketQuoteTimeliness.Realtime));
            Assert.That(observation.ProviderRequestId, Is.EqualTo("synthetic-request-1"));
            Assert.That(observation.InvocationId, Is.EqualTo(result.InvocationId));

            Assert.That(_store.Attempts, Has.Count.EqualTo(1));
            PriceCollectionAttempt attempt = _store.Attempts[0];
            Assert.That(attempt.Outcome, Is.EqualTo(PriceCollectionOutcome.Observed));
            Assert.That(attempt.ScheduledFor, Is.EqualTo(CurrentSlot));
            Assert.That(attempt.Provider, Is.EqualTo("eToro"));
            Assert.That(attempt.ProviderRequestId, Is.EqualTo("synthetic-request-1"));
            Assert.That(attempt.ErrorCode, Is.Null);
            Assert.That(attempt.InvocationId, Is.EqualTo(result.InvocationId));
            Assert.That(attempt.AttemptedAt, Is.EqualTo(Now));
            Assert.That(attempt.RecordedAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public async Task HandleAsync_WhenObservationExistsForCurrentSlot_MakesNoAttempt()
    {
        _store.LatestSlots[InstrumentId] = CurrentSlot;
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Is.Empty);
            Assert.That(_marketData.Requests, Is.Empty);
            Assert.That(_store.Observations, Is.Empty);
            Assert.That(_store.Attempts, Is.Empty);
        });
    }

    [Test]
    public async Task HandleAsync_WhenOnlyEarlierSlotObserved_CollectsCurrentSlot()
    {
        _store.LatestSlots[InstrumentId] = CurrentSlot
            .Minus(Duration.FromSeconds(60));
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Has.Count.EqualTo(1));
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.Observed));
            Assert.That(result.Items[0].ScheduledFor, Is.EqualTo(CurrentSlot));
        });
    }

    [Test]
    public async Task HandleAsync_WhenMarketClosed_RecordsMarketClosedSkip()
    {
        _clock = new FakeClock(ClosedNow);
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Has.Count.EqualTo(1));
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.SkippedMarketClosed));
            Assert.That(result.Items[0].ScheduledFor, Is.EqualTo(ClosedSlot));
            Assert.That(_marketData.Requests, Is.Empty);
            Assert.That(_store.Observations, Is.Empty);
            Assert.That(_store.Attempts, Has.Count.EqualTo(1));
            Assert.That(
                _store.Attempts[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.SkippedMarketClosed));
            Assert.That(_store.Attempts[0].ErrorCode, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WhenExchangeSessionUnconfigured_RecordsFailedAttempt()
    {
        CollectDuePricesHandler handler = CreateHandler(NoSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Has.Count.EqualTo(1));
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.Failed));
            Assert.That(
                result.Items[0].ErrorCode,
                Is.EqualTo(PriceCollectionErrors.ExchangeSessionUnconfigured.Code));
            Assert.That(_marketData.Requests, Is.Empty);
            Assert.That(_store.Observations, Is.Empty);
        });
    }

    [Test]
    public async Task HandleAsync_WhenProviderThrottled_RecordsThrottledOutcome()
    {
        _marketData.Responder = (_, _) =>
            Task.FromResult<Result<MarketQuote>>(MarketDataErrors.ProviderThrottled);
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Has.Count.EqualTo(1));
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.Throttled));
            Assert.That(
                result.Items[0].ErrorCode,
                Is.EqualTo(MarketDataErrors.ProviderThrottled.Code));
            Assert.That(_store.Observations, Is.Empty);
            Assert.That(
                _store.Attempts[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.Throttled));
            Assert.That(
                _store.Attempts[0].ErrorCode,
                Is.EqualTo(MarketDataErrors.ProviderThrottled.Code));
        });
    }

    [Test]
    public async Task HandleAsync_WhenProviderReturnsError_RecordsFailedOutcome()
    {
        _marketData.Responder = (_, _) =>
            Task.FromResult<Result<MarketQuote>>(MarketDataErrors.PriceUnavailable);
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items[0].Outcome, Is.EqualTo(PriceCollectionOutcome.Failed));
            Assert.That(
                result.Items[0].ErrorCode,
                Is.EqualTo(MarketDataErrors.PriceUnavailable.Code));
            Assert.That(_store.Observations, Is.Empty);
        });
    }

    [Test]
    public async Task HandleAsync_WhenObservationSlotAlreadyTaken_RecordsDuplicateOutcome()
    {
        _store.ForceDuplicate = true;
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Has.Count.EqualTo(1));
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.SkippedDuplicate));
            Assert.That(result.Items[0].ProviderRequestId, Is.Not.Null);
            Assert.That(_store.Observations, Is.Empty);
            Assert.That(
                _store.Attempts[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.SkippedDuplicate));
        });
    }

    [Test]
    public async Task HandleAsync_WhenProviderTimesOut_RecordsTimeoutFailure()
    {
        _marketData.Responder = (_, _) => throw new TimeoutException();
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items[0].Outcome, Is.EqualTo(PriceCollectionOutcome.Failed));
            Assert.That(
                result.Items[0].ErrorCode,
                Is.EqualTo(PriceCollectionErrors.ProviderTimeout.Code));
        });
    }

    [Test]
    public async Task HandleAsync_WhenProviderThrows_RecordsProviderFailure()
    {
        _marketData.Responder = (_, _) => throw new InvalidOperationException();
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items[0].Outcome, Is.EqualTo(PriceCollectionOutcome.Failed));
            Assert.That(
                result.Items[0].ErrorCode,
                Is.EqualTo(PriceCollectionErrors.ProviderError.Code));
        });
    }

    [Test]
    public async Task HandleAsync_WhenRunDeadlineCancelsAttempt_RecordsDeadlineFailureAndStops()
    {
        _source.Instruments.Add(SecondInstrument);
        _marketData.Responder = (_, _) => throw new OperationCanceledException();
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.DeadlineReached, Is.True);
            Assert.That(result.Items, Has.Count.EqualTo(1));
            Assert.That(result.Items[0].Outcome, Is.EqualTo(PriceCollectionOutcome.Failed));
            Assert.That(
                result.Items[0].ErrorCode,
                Is.EqualTo(PriceCollectionErrors.DeadlineExceeded.Code));
            Assert.That(_marketData.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleAsync_WhenProviderAdvancesClockPastDeadline_StopsBeforeNextInstrument()
    {
        _source.Instruments.Add(SecondInstrument);
        _marketData.Responder = (instrument, _) =>
        {
            _clock.Advance(Duration.FromSeconds(90));
            return Task.FromResult<Result<MarketQuote>>(new MarketQuote(
                instrument,
                "eToro",
                4242,
                1m,
                2m,
                null,
                _clock.GetCurrentInstant(),
                MarketQuoteTimeliness.Realtime,
                "synthetic-request"));
        };
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.DeadlineReached, Is.True);
            Assert.That(result.Items, Has.Count.EqualTo(1));
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.Observed));
            Assert.That(_marketData.Requests, Has.Count.EqualTo(1));
            Assert.That(_store.Attempts, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleAsync_WhenSessionEvaluationConsumesDeadline_RecordsDeadlineFailure()
    {
        StubMarketSessionPolicy sessions = new()
        {
            Responder = (_, _) =>
            {
                _clock.Advance(Duration.FromSeconds(90));
                return ExchangeSessionStatus.Open;
            }
        };
        CollectDuePricesHandler handler = CreateHandler(sessions);

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.DeadlineReached, Is.True);
            Assert.That(result.Items, Has.Count.EqualTo(1));
            Assert.That(result.Items[0].Outcome, Is.EqualTo(PriceCollectionOutcome.Failed));
            Assert.That(
                result.Items[0].ErrorCode,
                Is.EqualTo(PriceCollectionErrors.DeadlineExceeded.Code));
            Assert.That(_marketData.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task HandleAsync_WhenCallerCancelled_PropagatesCancellation()
    {
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await handler.HandleAsync(new CollectDuePrices(), cancelled.Token));
        Assert.Multiple(() =>
        {
            Assert.That(_source.CallCount, Is.EqualTo(0));
            Assert.That(_store.Attempts, Is.Empty);
        });
    }

    [Test]
    public async Task HandleAsync_WhenCancelledMidRun_PropagatesCancellation()
    {
        _source.Instruments.Add(SecondInstrument);
        using CancellationTokenSource cancellation = new();
        _marketData.Responder = (instrument, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult<Result<MarketQuote>>(new MarketQuote(
                instrument,
                "eToro",
                4242,
                1m,
                2m,
                null,
                Now,
                MarketQuoteTimeliness.Realtime,
                "synthetic-request"));
        };
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await handler.HandleAsync(new CollectDuePrices(), cancellation.Token));
        Assert.That(_marketData.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task HandleAsync_WhenNoMonitoredInstruments_ReturnsEmptyRun()
    {
        _source.Instruments.Clear();
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Is.Empty);
            Assert.That(result.DeadlineReached, Is.False);
            Assert.That(_marketData.Requests, Is.Empty);
            Assert.That(_store.Attempts, Is.Empty);
        });
    }

    [Test]
    public async Task HandleAsync_WithBudgetClampedPolicy_ClampsSlotToProviderBudget()
    {
        _clock = new FakeClock(Instant.FromUtc(2026, 10, 30, 13, 45, 31));
        for (int index = 0; index < 9; index++)
        {
            _source.Instruments.Add(new MonitoredInstrument(
                Guid.NewGuid(),
                "XNAS",
                $"DEMO-{index}",
                "USD",
                1));
        }

        _source.Instruments[0] = Instrument with { SamplingIntervalSeconds = 1 };
        CollectDuePricesHandler handler = CreateHandler(
            OpenSessions(),
            new ProviderBudgetClampedSamplingPolicy(120, 60));

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Has.Count.EqualTo(10));
            foreach (PriceCollectionItemResult item in result.Items)
            {
                Assert.That(
                    item.ScheduledFor,
                    Is.EqualTo(Instant.FromUtc(2026, 10, 30, 13, 45, 30)));
            }
        });
    }

    [Test]
    public async Task HandleAsync_WhenRunsOverlapForSameSlot_RecordsSingleObservation()
    {
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult[] results = await Task.WhenAll(
            handler.HandleAsync(new CollectDuePrices(), CancellationToken.None),
            handler.HandleAsync(new CollectDuePrices(), CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(_store.Observations, Has.Count.EqualTo(1));
            Assert.That(_store.Attempts, Has.Count.EqualTo(2));
            Assert.That(
                results.SelectMany(result => result.Items)
                    .Count(item => item.Outcome == PriceCollectionOutcome.Observed),
                Is.EqualTo(1));
            Assert.That(
                results.SelectMany(result => result.Items)
                    .Count(item => item.Outcome == PriceCollectionOutcome.SkippedDuplicate),
                Is.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleAsync_WhenSecondSlotBecomesDue_RecordsBothObservations()
    {
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult first = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);
        _store.LatestSlots[InstrumentId] = CurrentSlot;
        _clock.Advance(Duration.FromSeconds(60));
        _marketData.RetrievedAt = _clock.GetCurrentInstant();
        PriceCollectionRunResult second = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_store.Observations, Has.Count.EqualTo(2));
            Assert.That(
                _store.Observations[0].ScheduledFor, Is.EqualTo(CurrentSlot));
            Assert.That(
                _store.Observations[1].ScheduledFor,
                Is.EqualTo(CurrentSlot.Plus(Duration.FromSeconds(60))));
            Assert.That(
                _store.Observations[0].Bid,
                Is.EqualTo(_store.Observations[1].Bid));
            Assert.That(
                first.Items[0].Outcome, Is.EqualTo(PriceCollectionOutcome.Observed));
            Assert.That(
                second.Items[0].Outcome, Is.EqualTo(PriceCollectionOutcome.Observed));
            Assert.That(
                second.InvocationId, Is.Not.EqualTo(first.InvocationId));
        });
    }

    [Test]
    public async Task HandleAsync_WhenOutcomesVary_RecordsEachOutcomeDistinctly()
    {
        MonitoredInstrument closed = new(
            Guid.NewGuid(), "XTKS", "DEMO-JP", "JPY", 60);
        MonitoredInstrument throttled = new(
            Guid.NewGuid(), "XNAS", "DEMO-T", "USD", 60);
        MonitoredInstrument failing = new(
            Guid.NewGuid(), "XNAS", "DEMO-F", "USD", 60);
        _source.Instruments.AddRange([closed, throttled, failing]);

        _marketData.Responder = (instrument, _) =>
        {
            if (instrument.Symbol == "DEMO-T")
            {
                return Task.FromResult<Result<MarketQuote>>(
                    MarketDataErrors.ProviderThrottled);
            }

            if (instrument.Symbol == "DEMO-F")
            {
                return Task.FromResult<Result<MarketQuote>>(
                    MarketDataErrors.PriceUnavailable);
            }

            return Task.FromResult<Result<MarketQuote>>(new MarketQuote(
                instrument,
                "eToro",
                4242,
                1m,
                2m,
                null,
                Now,
                MarketQuoteTimeliness.Realtime,
                $"synthetic-request-{instrument.Symbol}"));
        };

        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Has.Count.EqualTo(4));
            Assert.That(
                result.Items.Select(item => item.Outcome).ToArray(),
                Is.EqualTo(new[]
                {
                    PriceCollectionOutcome.Observed,
                    PriceCollectionOutcome.SkippedMarketClosed,
                    PriceCollectionOutcome.Throttled,
                    PriceCollectionOutcome.Failed
                }));
            Assert.That(_store.Attempts, Has.Count.EqualTo(4));
            Assert.That(_store.Observations, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleAsync_WhenExecutionBudgetIsZero_ReturnsDeadlineWithoutProviderCalls()
    {
        CollectDuePricesHandler handler = CreateHandler(
            OpenSessions(),
            new PerInstrumentSamplingPolicy(),
            new PriceCollectionOptions { MaxExecutionTimeSeconds = 0 });

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.DeadlineReached, Is.True);
            Assert.That(result.Items, Is.Empty);
            Assert.That(_marketData.Requests, Is.Empty);
            Assert.That(_store.Attempts, Is.Empty);
        });
    }

    [Test]
    public async Task HandleAsync_WhenQuoteIsOneSided_PreservesNullsDistinctly()
    {
        _marketData.Bid = 987.123456789012345678m;
        _marketData.Ask = null;
        _marketData.ProviderObservedAt = null;
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        await handler.HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            ScheduledPriceObservation observation = _store.Observations[0];
            Assert.That(observation.Bid, Is.EqualTo(987.123456789012345678m));
            Assert.That(observation.Ask, Is.Null);
            Assert.That(observation.ProviderObservedAt, Is.Null);
            Assert.That(observation.RetrievedAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public async Task HandleAsync_WhenMarketClosedTwiceForSameSlot_RecordsSingleMarketClosedAttempt()
    {
        _clock = new FakeClock(ClosedNow);
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult first = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);
        _store.LatestAttempts[InstrumentId] = _store.Attempts[0];
        PriceCollectionRunResult second = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                first.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.SkippedMarketClosed));
            Assert.That(
                second.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.SkippedMarketClosed));
            Assert.That(_store.Attempts, Has.Count.EqualTo(1));
            Assert.That(_marketData.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task HandleAsync_WhenMarketClosedInNextSlot_RecordsSecondSlotAttempt()
    {
        _clock = new FakeClock(ClosedNow);
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        await handler.HandleAsync(new CollectDuePrices(), CancellationToken.None);
        _store.LatestAttempts[InstrumentId] = _store.Attempts[0];
        _clock.Advance(Duration.FromSeconds(60));
        PriceCollectionRunResult second = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                second.Items[0].ScheduledFor,
                Is.EqualTo(ClosedSlot.Plus(Duration.FromSeconds(60))));
            Assert.That(_store.Attempts, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task HandleAsync_WhenLatestAttemptThrottledForSlot_SkipsWithoutProviderCall()
    {
        _store.LatestAttempts[InstrumentId] = new PriceCollectionAttempt(
            InstrumentId,
            CurrentSlot,
            PriceCollectionOutcome.Throttled,
            MarketDataErrors.ProviderThrottled.Code,
            null,
            "throttled-request",
            Guid.NewGuid(),
            Now,
            Now);
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.Throttled));
            Assert.That(
                result.Items[0].ErrorCode,
                Is.EqualTo(MarketDataErrors.ProviderThrottled.Code));
            Assert.That(
                result.Items[0].ProviderRequestId,
                Is.EqualTo("throttled-request"));
            Assert.That(_marketData.Requests, Is.Empty);
            Assert.That(_store.Attempts, Is.Empty);
        });
    }

    [Test]
    public async Task HandleAsync_WhenLatestAttemptThrottledForEarlierSlot_CollectsNormally()
    {
        _store.LatestAttempts[InstrumentId] = new PriceCollectionAttempt(
            InstrumentId,
            CurrentSlot.Minus(Duration.FromSeconds(60)),
            PriceCollectionOutcome.Throttled,
            MarketDataErrors.ProviderThrottled.Code,
            null,
            "throttled-request",
            Guid.NewGuid(),
            Now,
            Now);
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.Observed));
            Assert.That(_marketData.Requests, Has.Count.EqualTo(1));
            Assert.That(_store.Observations, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleAsync_WhenQuoteHasNeitherBidNorAsk_RetriesThenRecordsPriceUnavailable()
    {
        _marketData.Bid = null;
        _marketData.Ask = null;
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_marketData.Requests, Has.Count.EqualTo(2));
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.PriceUnavailable));
            Assert.That(
                result.Items[0].ErrorCode,
                Is.EqualTo(PriceCollectionErrors.PriceUnavailable.Code));
            Assert.That(
                result.Items[0].ProviderRequestId,
                Is.EqualTo("synthetic-request-2"));
            Assert.That(_store.Observations, Is.Empty);
            Assert.That(_store.Attempts, Has.Count.EqualTo(1));
            Assert.That(
                _store.Attempts[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.PriceUnavailable));
        });
    }

    [Test]
    public async Task HandleAsync_WhenUnavailableRetriesDisabled_RecordsPriceUnavailableAfterSingleCall()
    {
        _marketData.Bid = null;
        _marketData.Ask = null;
        CollectDuePricesHandler handler = CreateHandler(
            OpenSessions(),
            options: new PriceCollectionOptions { UnavailableRetryAttempts = 0 });

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_marketData.Requests, Has.Count.EqualTo(1));
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.PriceUnavailable));
        });
    }

    [Test]
    public async Task HandleAsync_WhenRetrySuppliesQuote_RecordsObservation()
    {
        int calls = 0;
        _marketData.Responder = (instrument, _) =>
        {
            calls++;
            return Task.FromResult<Result<MarketQuote>>(new MarketQuote(
                instrument,
                "eToro",
                4242,
                calls == 1 ? null : 5m,
                null,
                null,
                Now,
                MarketQuoteTimeliness.Realtime,
                $"synthetic-request-{calls}"));
        };
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_marketData.Requests, Has.Count.EqualTo(2));
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.Observed));
            Assert.That(_store.Observations, Has.Count.EqualTo(1));
            Assert.That(_store.Observations[0].Bid, Is.EqualTo(5m));
            Assert.That(_store.Observations[0].Ask, Is.Null);
            Assert.That(
                _store.Observations[0].ProviderRequestId,
                Is.EqualTo("synthetic-request-2"));
        });
    }

    [Test]
    public async Task HandleAsync_WhenLatestAttemptPriceUnavailableForSlot_SkipsWithoutProviderCall()
    {
        _store.LatestAttempts[InstrumentId] = new PriceCollectionAttempt(
            InstrumentId,
            CurrentSlot,
            PriceCollectionOutcome.PriceUnavailable,
            PriceCollectionErrors.PriceUnavailable.Code,
            "eToro",
            "unavailable-request",
            Guid.NewGuid(),
            Now,
            Now);
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.PriceUnavailable));
            Assert.That(
                result.Items[0].ProviderRequestId,
                Is.EqualTo("unavailable-request"));
            Assert.That(_marketData.Requests, Is.Empty);
            Assert.That(_store.Attempts, Is.Empty);
        });
    }

    [Test]
    public async Task HandleAsync_WhenReadStallsPastDeadline_ReturnsDeadlineWithoutItems()
    {
        ManualTimeProvider timeProvider = new();
        _source.Gate = token => Task.Delay(Timeout.InfiniteTimeSpan, token);
        CollectDuePricesHandler handler = CreateHandler(
            OpenSessions(),
            timeProvider: timeProvider);

        Task<PriceCollectionRunResult> run = handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(61));
        PriceCollectionRunResult result = await run;

        Assert.Multiple(() =>
        {
            Assert.That(result.DeadlineReached, Is.True);
            Assert.That(result.Items, Is.Empty);
            Assert.That(_marketData.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task HandleAsync_WhenObservationWriteStallsPastDeadline_RecordsDeadlineFailure()
    {
        ManualTimeProvider timeProvider = new();
        _store.ObservationGate = token => Task.Delay(Timeout.InfiniteTimeSpan, token);
        CollectDuePricesHandler handler = CreateHandler(
            OpenSessions(),
            timeProvider: timeProvider);

        Task<PriceCollectionRunResult> run = handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(61));
        PriceCollectionRunResult result = await run;

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Has.Count.EqualTo(1));
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.Failed));
            Assert.That(
                result.Items[0].ErrorCode,
                Is.EqualTo(PriceCollectionErrors.DeadlineExceeded.Code));
            Assert.That(_store.Observations, Is.Empty);
            Assert.That(_store.Attempts, Is.Empty);
        });
    }

    [Test]
    public async Task HandleAsync_WhenAttemptWriteStallsPastDeadline_ReturnsDeadlineWithoutWriting()
    {
        ManualTimeProvider timeProvider = new();
        StubMarketSessionPolicy sessions = new()
        {
            Responder = (_, _) => ExchangeSessionStatus.Closed
        };
        _store.AttemptGate = token => Task.Delay(Timeout.InfiniteTimeSpan, token);
        CollectDuePricesHandler handler = CreateHandler(
            sessions,
            timeProvider: timeProvider);

        Task<PriceCollectionRunResult> run = handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(61));
        PriceCollectionRunResult result = await run;

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Has.Count.EqualTo(1));
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.Failed));
            Assert.That(
                result.Items[0].ErrorCode,
                Is.EqualTo(PriceCollectionErrors.DeadlineExceeded.Code));
            Assert.That(_store.Observations, Is.Empty);
            Assert.That(_store.Attempts, Is.Empty);
            Assert.That(result.DeadlineReached, Is.True);
        });
    }

    [Test]
    public async Task HandleAsync_WhenObservationStoreFails_RecordsStorageFailureAndContinues()
    {
        _source.Instruments.Add(SecondInstrument);
        int writes = 0;
        _store.ObservationGate = _ =>
        {
            writes++;
            if (writes == 1)
            {
                throw new PriceObservationStorageException(
                    "synthetic storage failure",
                    new InvalidOperationException());
            }

            return Task.CompletedTask;
        };
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Has.Count.EqualTo(2));
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.Failed));
            Assert.That(
                result.Items[0].ErrorCode,
                Is.EqualTo(PriceCollectionErrors.StorageError.Code));
            Assert.That(
                result.Items[1].Outcome,
                Is.EqualTo(PriceCollectionOutcome.Observed));
            Assert.That(_store.Observations, Has.Count.EqualTo(1));
            Assert.That(
                _store.Attempts.Select(attempt => attempt.Outcome).ToArray(),
                Is.EqualTo(new[]
                {
                    PriceCollectionOutcome.Failed,
                    PriceCollectionOutcome.Observed
                }));
        });
    }

    [Test]
    public async Task HandleAsync_WhenAttemptWriteFails_RunContinuesToNextInstrument()
    {
        _source.Instruments.Add(SecondInstrument);
        int attemptWrites = 0;
        _store.AttemptGate = _ =>
        {
            attemptWrites++;
            if (attemptWrites == 1)
            {
                throw new PriceObservationStorageException(
                    "synthetic storage failure",
                    new InvalidOperationException());
            }

            return Task.CompletedTask;
        };
        _marketData.Bid = null;
        _marketData.Ask = null;
        CollectDuePricesHandler handler = CreateHandler(OpenSessions());

        PriceCollectionRunResult result = await handler.HandleAsync(
            new CollectDuePrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Has.Count.EqualTo(2));
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.Failed));
            Assert.That(
                result.Items[0].ErrorCode,
                Is.EqualTo(PriceCollectionErrors.StorageError.Code));
            Assert.That(
                result.Items[1].Outcome,
                Is.EqualTo(PriceCollectionOutcome.PriceUnavailable));
        });
    }

    private CollectDuePricesHandler CreateHandler(
        IMarketSessionPolicy sessionPolicy,
        ISamplingPolicy? samplingPolicy = null,
        PriceCollectionOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        return new CollectDuePricesHandler(
            _source,
            _store,
            _marketData,
            samplingPolicy ?? new PerInstrumentSamplingPolicy(),
            sessionPolicy,
            _clock,
            timeProvider ?? TimeProvider.System,
            options ?? new PriceCollectionOptions());
    }

    private static WeeklyScheduleMarketSessionPolicy OpenSessions()
    {
        return new WeeklyScheduleMarketSessionPolicy(
            new Dictionary<string, ExchangeSession>(StringComparer.OrdinalIgnoreCase)
            {
                ["XNAS"] = NasdaqWeek(),
                ["XTKS"] = TokyoWeek()
            });
    }

    private static WeeklyScheduleMarketSessionPolicy NoSessions()
    {
        return new WeeklyScheduleMarketSessionPolicy(
            new Dictionary<string, ExchangeSession>(StringComparer.OrdinalIgnoreCase));
    }

    private static ExchangeSession NasdaqWeek()
    {
        return new ExchangeSession(
            DateTimeZoneProviders.Tzdb["America/New_York"],
            false,
            [Weekdays(new LocalTime(9, 30), new LocalTime(16, 0))]);
    }

    private static ExchangeSession TokyoWeek()
    {
        return new ExchangeSession(
            DateTimeZoneProviders.Tzdb["Asia/Tokyo"],
            false,
            [Weekdays(new LocalTime(9, 0), new LocalTime(15, 0))]);
    }

    private static SessionWindow Weekdays(LocalTime open, LocalTime close)
    {
        return new SessionWindow(
            new HashSet<IsoDayOfWeek>
            {
                IsoDayOfWeek.Monday,
                IsoDayOfWeek.Tuesday,
                IsoDayOfWeek.Wednesday,
                IsoDayOfWeek.Thursday,
                IsoDayOfWeek.Friday
            },
            open,
            close);
    }
}
