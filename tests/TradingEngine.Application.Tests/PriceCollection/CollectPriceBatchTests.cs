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

    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(120, 1)]
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

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(1000)]
    public async Task HandleAsync_RepeatedFirstInstrumentDeadline_NextScopeMakesProgress(int batchSize)
    {
        AddInstruments(3);
        _provider.MaxBatchSize = batchSize;
        PriceCollectionProgress progress = new();
        _provider.Responder = async (batch, token) =>
        {
            if (batch[0].Symbol == "DEMO00000")
            {
                _clock.Advance(Duration.FromSeconds(61));
                throw new OperationCanceledException();
            }

            Dictionary<MarketInstrumentIdentity, Result<MarketQuote>> quotes = [];
            foreach (MarketInstrumentIdentity identity in batch)
            {
                quotes[identity] = await _provider.Single.GetCurrentQuoteAsync(identity, token);
            }

            return quotes;
        };

        PriceCollectionRunResult first = await Handler(progress).HandleAsync(new CollectDuePrices(), CancellationToken.None);
        PriceCollectionRunResult second = await Handler(progress).HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.DeadlineReached, Is.True);
            Assert.That(first.Diagnostics.Unprocessed, Is.EqualTo(Math.Max(0, 3 - batchSize)));
            Assert.That(first.Diagnostics.Deferred, Is.EqualTo(Math.Min(3, batchSize)));
            Assert.That(second.Diagnostics.Persisted, Is.EqualTo(2));
            Assert.That(_store.Observations.Select(item => item.InstrumentId),
                Is.EquivalentTo(_source.Instruments.Skip(1).Select(item => item.Id)));
            Assert.That(_provider.Batches[1][0].Symbol, Is.EqualTo("DEMO00001"));
        });
    }

    [Test]
    public async Task HandleAsync_QuoteRetrievedInNextSlot_RecordsExpiredSlotWithoutBackfill()
    {
        AddInstruments(1);
        _clock.Advance(Duration.FromSeconds(899));
        _provider.Responder = async (batch, token) =>
        {
            _clock.Advance(Duration.FromSeconds(2));
            _provider.Single.RetrievedAt = _clock.GetCurrentInstant();
            return new Dictionary<MarketInstrumentIdentity, Result<MarketQuote>>
            {
                [batch[0]] = await _provider.Single.GetCurrentQuoteAsync(batch[0], token)
            };
        };
        PriceCollectionProgress progress = new();

        PriceCollectionRunResult expired = await Handler(progress).HandleAsync(new CollectDuePrices(), CancellationToken.None);
        PriceCollectionRunResult next = await Handler(progress).HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(expired.Items.Single().ErrorCode, Is.EqualTo(PriceCollectionErrors.SlotExpired.Code));
            Assert.That(expired.Diagnostics.ExpiredPendingSlots, Is.EqualTo(1));
            Assert.That(next.Diagnostics.ExpiredPendingSlots, Is.Zero);
            Assert.That(_store.Observations, Has.Count.EqualTo(1));
            Assert.That(_store.Observations.Single().ScheduledFor, Is.EqualTo(Now.Plus(Duration.FromSeconds(900))));
        });
    }

    [Test]
    public async Task HandleAsync_UnfinishedSlotExpires_ReportsOnceAndUsesCurrentSlot()
    {
        AddInstruments(2);
        _provider.MaxBatchSize = 1;
        _provider.Responder = (_, _) => throw new OperationCanceledException();
        PriceCollectionProgress progress = new();
        await Handler(progress).HandleAsync(new CollectDuePrices(), CancellationToken.None);
        _clock.Advance(Duration.FromSeconds(900));
        _provider.Single.RetrievedAt = _clock.GetCurrentInstant();
        _provider.Responder = null;

        PriceCollectionRunResult next = await Handler(progress).HandleAsync(new CollectDuePrices(), CancellationToken.None);
        PriceCollectionRunResult repeated = await Handler(progress).HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(next.Diagnostics.ExpiredPendingSlots, Is.EqualTo(2));
            Assert.That(repeated.Diagnostics.ExpiredPendingSlots, Is.Zero);
            Assert.That(_store.Observations.All(item => item.ScheduledFor == Now.Plus(Duration.FromSeconds(900))), Is.True);
        });
    }

    [Test]
    public async Task HandleAsync_PartialResponse_ReportsUniqueInstrumentCountsAcrossRetries()
    {
        AddInstruments(2);
        _provider.Responder = async (batch, token) => new Dictionary<MarketInstrumentIdentity, Result<MarketQuote>>
        {
            [batch[0]] = await _provider.Single.GetCurrentQuoteAsync(batch[0], token)
        };

        PriceCollectionRunResult result = await Handler().HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_provider.Batches.Select(batch => batch.Length), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(result.Diagnostics.Eligible, Is.EqualTo(2));
            Assert.That(result.Diagnostics.Requested, Is.EqualTo(2));
            Assert.That(result.Diagnostics.Returned, Is.EqualTo(2));
            Assert.That(result.Diagnostics.Persisted, Is.EqualTo(2));
            Assert.That(result.Diagnostics.Unprocessed, Is.Zero);
            Assert.That(result.Diagnostics.EligibilityScanCompleted, Is.True);
        });
    }

    [Test]
    public async Task HandleAsync_ProviderDeadlineAfterAllPrepared_ReportsEligibilityScanCompleted()
    {
        AddInstruments(3);
        _provider.Responder = (_, _) =>
        {
            _clock.Advance(Duration.FromSeconds(61));
            throw new OperationCanceledException();
        };

        PriceCollectionRunResult result = await Handler().HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.DeadlineReached, Is.True);
            Assert.That(result.Items.All(item => item.ErrorCode == PriceCollectionErrors.DeadlineExceeded.Code), Is.True);
            Assert.That(result.Diagnostics.Eligible, Is.EqualTo(3));
            Assert.That(result.Diagnostics.Unprocessed, Is.Zero);
            Assert.That(result.Diagnostics.EligibilityScanCompleted, Is.True);
        });
    }

    [Test]
    public async Task HandleAsync_StorageDeadlineAfterAllPrepared_ReportsEligibilityScanCompleted()
    {
        AddInstruments(3);
        _store.ObservationGate = _ =>
        {
            _clock.Advance(Duration.FromSeconds(61));
            throw new OperationCanceledException();
        };

        PriceCollectionRunResult result = await Handler().HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.DeadlineReached, Is.True);
            Assert.That(result.Diagnostics.Eligible, Is.EqualTo(3));
            Assert.That(result.Diagnostics.Returned, Is.EqualTo(3));
            Assert.That(result.Diagnostics.Unprocessed, Is.Zero);
            Assert.That(result.Diagnostics.EligibilityScanCompleted, Is.True);
        });
    }

    [Test]
    public async Task HandleAsync_LastPreparationReachesDeadline_ReportsEligibilityScanIncomplete()
    {
        AddInstruments(3);
        StubNativeQuoteUnitMap units = StubNativeQuoteUnitMap.Configured("USD");
        units.Responder = (symbol, _) =>
        {
            if (symbol == "DEMO00002")
            {
                _clock.Advance(Duration.FromSeconds(61));
                throw new OperationCanceledException();
            }

            return Task.FromResult(units.Result);
        };

        PriceCollectionRunResult result = await Handler(units: units).HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.DeadlineReached, Is.True);
            Assert.That(result.Diagnostics.Eligible, Is.EqualTo(2));
            Assert.That(result.Diagnostics.Unprocessed, Is.Zero);
            Assert.That(result.Diagnostics.EligibilityScanCompleted, Is.False);
            Assert.That(_provider.Batches, Is.Empty);
        });
    }

    [TestCase(true, 1)]
    [TestCase(true, 1000)]
    [TestCase(false, 1)]
    public async Task HandleAsync_RepeatedPreparationOrStorageDeadline_AdvancesPastStalledInstrument(bool preparation, int batchSize)
    {
        AddInstruments(3);
        _provider.MaxBatchSize = batchSize;
        PriceCollectionProgress progress = new();
        StubNativeQuoteUnitMap units = StubNativeQuoteUnitMap.Configured("USD");
        if (preparation)
        {
            units.Responder = (symbol, _) =>
            {
                if (symbol == "DEMO00000")
                {
                    _clock.Advance(Duration.FromSeconds(61));
                    throw new OperationCanceledException();
                }

                return Task.FromResult(units.Result);
            };
        }
        else
        {
            _store.ObservationGate = _ =>
            {
                if (_provider.Batches.Last()[0].Symbol == "DEMO00000")
                {
                    _clock.Advance(Duration.FromSeconds(61));
                    throw new OperationCanceledException();
                }

                return Task.CompletedTask;
            };
        }

        PriceCollectionRunResult first = await Handler(progress, units).HandleAsync(new CollectDuePrices(), CancellationToken.None);
        PriceCollectionRunResult next = await Handler(progress, units).HandleAsync(new CollectDuePrices(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.DeadlineReached, Is.True);
            Assert.That(first.Diagnostics.Unprocessed, Is.EqualTo(2));
            Assert.That(first.Diagnostics.EligibilityScanCompleted, Is.False);
            Assert.That(next.Diagnostics.Persisted, Is.EqualTo(2));
            Assert.That(_store.Observations.Select(item => item.InstrumentId),
                Is.EquivalentTo(_source.Instruments.Skip(1).Select(item => item.Id)));
        });
    }

    private void AddInstruments(int count)
    {
        for (int index = 0; index < count; index++)
        {
            _source.Instruments.Add(new MonitoredInstrument(
                Guid.Parse($"00000000-0000-0000-0000-{index:D12}"), "XTEST", $"DEMO{index:D5}", "USD", 900));
        }
    }

    private CollectDuePricesHandler Handler(PriceCollectionProgress? progress = null, StubNativeQuoteUnitMap? units = null)
    {
        WeeklyScheduleMarketSessionPolicy sessions = new(new Dictionary<string, ExchangeSession>
        {
            ["XTEST"] = new ExchangeSession(null, true, []),
            ["XCLOSED"] = new ExchangeSession(DateTimeZone.Utc, false,
                [new SessionWindow(new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Saturday }, new LocalTime(9, 0), new LocalTime(17, 0))])
        });
        return new CollectDuePricesHandler(_source, _store, _provider,
            units ?? StubNativeQuoteUnitMap.Configured("USD"), new ProviderBudgetClampedSamplingPolicy(120, 60),
            sessions, _clock, TimeProvider.System, new PriceCollectionOptions(), progress);
    }
}
