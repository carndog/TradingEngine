using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.MonitoringRules;
using TradingEngine.Application.Recommendations;
using TradingEngine.Application.Tests.PriceCollection;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Revisions;

namespace TradingEngine.Application.Tests.Recommendations;

[TestFixture]
public sealed class EvaluateSavedPricesHandlerTests
{
    private static readonly Guid InstrumentId =
        Guid.Parse("3f8a2c1d-9e4b-4a5c-b7d6-2e1f0a9b8c7d");
    private static readonly Guid SecondInstrumentId =
        Guid.Parse("7c1d3e5f-2a4b-4c6d-8e9f-1a2b3c4d5e6f");
    private static readonly Guid RuleId =
        Guid.Parse("11111111-2222-4333-8444-555555555555");
    private static readonly Guid RevisionOneId =
        Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid RevisionTwoId =
        Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 30, 13, 45, 30);
    private static readonly Instant T0 = Instant.FromUtc(2026, 10, 30, 13, 0, 0);

    private FakeClock _clock = null!;
    private InMemoryRecommendationStore _store = null!;
    private StubMonitoringRuleStore _rules = null!;
    private ManualTimeProvider _timeProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeClock(Now);
        _store = new InMemoryRecommendationStore();
        _rules = new StubMonitoringRuleStore();
        _timeProvider = new ManualTimeProvider();
        _store.Instruments.Add(Instrument());
        _rules.Snapshots[InstrumentId] = new MonitoringRuleSnapshot(
            BuildRule(RevisionOneId, T0, null, Zone(95m, 100m, 105m)),
            []);
    }

    [Test]
    public async Task HandleAsync_WhenFirstEnabled_SeedsBaselineFromLatestAndSendsNothing()
    {
        AddObservation(T0, 120m);
        AddObservation(T0.Plus(Duration.FromMinutes(1)), 102m);
        AddObservation(T0.Plus(Duration.FromMinutes(2)), 97m);
        AddObservation(T0.Plus(Duration.FromMinutes(3)), 110m);
        AddObservation(T0.Plus(Duration.FromMinutes(4)), 102m);

        EvaluationRunResult result = await CreateHandler().HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Has.Count.EqualTo(1));
            Assert.That(result.Items[0].Evaluated, Is.EqualTo(0));
            Assert.That(result.Items[0].IntentsCreated, Is.EqualTo(0));
            Assert.That(_store.Intents, Is.Empty);
            Assert.That(
                _store.Checkpoints[InstrumentId],
                Is.EqualTo(T0.Plus(Duration.FromMinutes(4))));
        });
    }

    [Test]
    public async Task HandleAsync_WhenSequenceReEnters_CreatesExactlyTwoIntents()
    {
        SeedBaseline(T0, 120m);
        AddObservation(T0.Plus(Duration.FromMinutes(1)), 102m);
        AddObservation(T0.Plus(Duration.FromMinutes(2)), 97m);
        AddObservation(T0.Plus(Duration.FromMinutes(3)), 110m);
        AddObservation(T0.Plus(Duration.FromMinutes(4)), 102m);

        EvaluationRunResult result = await CreateHandler().HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items[0].Evaluated, Is.EqualTo(4));
            Assert.That(result.Items[0].NoAction, Is.EqualTo(2));
            Assert.That(result.Items[0].IntentsCreated, Is.EqualTo(2));
            Assert.That(_store.Intents, Has.Count.EqualTo(2));
            Assert.That(
                _store.Intents.ContainsKey((InstrumentId, T0.Plus(Duration.FromMinutes(1)))),
                Is.True);
            Assert.That(
                _store.Intents.ContainsKey((InstrumentId, T0.Plus(Duration.FromMinutes(4)))),
                Is.True);
            Assert.That(
                _store.Intents[(InstrumentId, T0.Plus(Duration.FromMinutes(1)))]
                    .MonitoringRuleRevisionId,
                Is.EqualTo(RevisionOneId));
            Assert.That(
                _store.Intents[(InstrumentId, T0.Plus(Duration.FromMinutes(1)))].PreviousScheduledFor,
                Is.EqualTo(T0));
            Assert.That(
                _store.Checkpoints[InstrumentId],
                Is.EqualTo(T0.Plus(Duration.FromMinutes(4))));
        });
    }

    [Test]
    public async Task HandleAsync_WhenReplayed_KeepsIntentCountAtTwo()
    {
        SeedBaseline(T0, 120m);
        AddObservation(T0.Plus(Duration.FromMinutes(1)), 102m);
        AddObservation(T0.Plus(Duration.FromMinutes(2)), 97m);
        AddObservation(T0.Plus(Duration.FromMinutes(3)), 110m);
        AddObservation(T0.Plus(Duration.FromMinutes(4)), 102m);

        EvaluateSavedPricesHandler handler = CreateHandler();
        await handler.HandleAsync(new EvaluateSavedPrices(), CancellationToken.None);
        EvaluationRunResult replayed = await handler.HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(replayed.Items[0].Evaluated, Is.EqualTo(0));
            Assert.That(replayed.Items[0].IntentsCreated, Is.EqualTo(0));
            Assert.That(_store.Intents, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task HandleAsync_WhenRevisionBoundaryCrossed_PreservesEachSlotRevisionId()
    {
        _rules.Snapshots[InstrumentId] = new MonitoringRuleSnapshot(
            BuildTwoRevisionRule(),
            []);

        Instant boundary = T0.Plus(Duration.FromMinutes(3));
        SeedBaseline(T0, 120m);
        AddObservation(T0.Plus(Duration.FromMinutes(1)), 102m);
        AddObservation(T0.Plus(Duration.FromMinutes(2)), 110m);
        AddObservation(boundary, 120m);
        AddObservation(T0.Plus(Duration.FromMinutes(4)), 102m);

        EvaluationRunResult result = await CreateHandler().HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items[0].IntentsCreated, Is.EqualTo(2));
            Assert.That(
                _store.Intents[(InstrumentId, T0.Plus(Duration.FromMinutes(1)))]
                    .MonitoringRuleRevisionId,
                Is.EqualTo(RevisionOneId));
            Assert.That(
                _store.Intents[(InstrumentId, T0.Plus(Duration.FromMinutes(4)))]
                    .MonitoringRuleRevisionId,
                Is.EqualTo(RevisionTwoId));
        });
    }

    [Test]
    public async Task HandleAsync_WhenSlotHasNoApplicableRevision_AdvancesCheckpoint()
    {
        _rules.Snapshots[InstrumentId] = new MonitoringRuleSnapshot(
            BuildRule(
                RevisionOneId,
                T0.Plus(Duration.FromMinutes(3)),
                null,
                Zone(95m, 100m, 105m)),
            []);

        SeedBaseline(T0, 120m);
        AddObservation(T0.Plus(Duration.FromMinutes(1)), 102m);
        AddObservation(T0.Plus(Duration.FromMinutes(2)), 97m);
        AddObservation(T0.Plus(Duration.FromMinutes(3)), 110m);
        AddObservation(T0.Plus(Duration.FromMinutes(4)), 102m);

        EvaluationRunResult result = await CreateHandler().HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items[0].SkippedNoRevision, Is.EqualTo(2));
            Assert.That(result.Items[0].IntentsCreated, Is.EqualTo(1));
            Assert.That(
                _store.Checkpoints[InstrumentId],
                Is.EqualTo(T0.Plus(Duration.FromMinutes(4))));
        });
    }

    [Test]
    public async Task HandleAsync_WhenEvaluationFails_LeavesSlotForRetry()
    {
        SeedBaseline(T0, 120m);
        AddObservation(T0.Plus(Duration.FromMinutes(1)), null, null);
        AddObservation(T0.Plus(Duration.FromMinutes(2)), 110m);

        EvaluationRunResult first = await CreateHandler().HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.Items[0].FailureCode, Is.Not.Null);
            Assert.That(
                first.Items[0].FailedSlot,
                Is.EqualTo(T0.Plus(Duration.FromMinutes(1))));
            Assert.That(_store.Checkpoints[InstrumentId], Is.EqualTo(T0));
            Assert.That(_store.Intents, Is.Empty);
        });

        _store.Observations[InstrumentId][1] = Observation(T0.Plus(Duration.FromMinutes(1)), 102m);

        EvaluationRunResult second = await CreateHandler().HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(second.Items[0].FailureCode, Is.Null);
            Assert.That(second.Items[0].Evaluated, Is.EqualTo(2));
            Assert.That(
                _store.Checkpoints[InstrumentId],
                Is.EqualTo(T0.Plus(Duration.FromMinutes(2))));
        });
    }

    [Test]
    public async Task HandleAsync_WhenCheckpointConflicts_StopsInstrumentProcessing()
    {
        SeedBaseline(T0, 120m);
        AddObservation(T0.Plus(Duration.FromMinutes(1)), 102m);
        AddObservation(T0.Plus(Duration.FromMinutes(2)), 97m);
        _store.CommitHook = _ =>
        {
            _store.Checkpoints[InstrumentId] = T0.Plus(Duration.FromMinutes(9));
        };

        EvaluationRunResult result = await CreateHandler().HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items[0].FailureCode,
                Is.EqualTo(RecommendationErrors.CheckpointConflict.Code));
            Assert.That(
                result.Items[0].FailedSlot,
                Is.EqualTo(T0.Plus(Duration.FromMinutes(1))));
            Assert.That(_store.Intents, Is.Empty);
        });
    }

    [Test]
    public async Task HandleAsync_WhenCommitReturnsAlreadyRecorded_DoesNotCountNewIntent()
    {
        SeedBaseline(T0, 120m);
        AddObservation(T0.Plus(Duration.FromMinutes(1)), 102m);
        _store.Intents[(InstrumentId, T0.Plus(Duration.FromMinutes(1)))] = new TriggeredRecommendation(
            InstrumentId,
            T0.Plus(Duration.FromMinutes(1)),
            T0,
            RevisionOneId,
            PriceBasis.Bid,
            "USD",
            100m,
            101m,
            []);

        EvaluationRunResult result = await CreateHandler().HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items[0].IntentsCreated, Is.EqualTo(0));
            Assert.That(_store.Intents, Has.Count.EqualTo(1));
            Assert.That(
                _store.Checkpoints[InstrumentId],
                Is.EqualTo(T0.Plus(Duration.FromMinutes(1))));
        });
    }

    [Test]
    public async Task HandleAsync_WhenInstrumentHasNoPendingObservations_Idles()
    {
        SeedBaseline(T0, 120m);

        EvaluationRunResult result = await CreateHandler().HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items[0].Evaluated, Is.EqualTo(0));
            Assert.That(_rules.GetCalls, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task HandleAsync_WhenNoCheckpointAndNoObservations_StaysUnseeded()
    {
        EvaluationRunResult result = await CreateHandler().HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items[0].Evaluated, Is.EqualTo(0));
            Assert.That(_store.Checkpoints.ContainsKey(InstrumentId), Is.False);
        });
    }

    [Test]
    public async Task HandleAsync_WhenMultipleInstruments_ProcessesIndependently()
    {
        _store.Instruments.Add(SecondInstrument());
        _rules.Snapshots[SecondInstrumentId] = new MonitoringRuleSnapshot(
            BuildRule(RevisionTwoId, T0, null, Zone(95m, 100m, 105m)),
            []);

        SeedBaseline(T0, 120m);
        AddObservation(T0.Plus(Duration.FromMinutes(1)), 102m);
        _store.Observations[SecondInstrumentId] = [Observation(T0, 130m)];
        _store.Checkpoints[SecondInstrumentId] = T0;
        _store.Observations[SecondInstrumentId].Add(Observation(T0.Plus(Duration.FromMinutes(1)), 110m));

        EvaluationRunResult result = await CreateHandler().HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Has.Count.EqualTo(2));
            Assert.That(_store.Intents, Has.Count.EqualTo(1));
            Assert.That(
                _store.Intents.ContainsKey((InstrumentId, T0.Plus(Duration.FromMinutes(1)))),
                Is.True);
        });
    }

    private EvaluateSavedPricesHandler CreateHandler()
    {
        return new EvaluateSavedPricesHandler(
            _store,
            _rules,
            new ChartAnalysisEvaluator(),
            _clock,
            _timeProvider,
            new RecommendationOptions { PriceBasis = "Bid" });
    }

    private void SeedBaseline(Instant slot, decimal bid)
    {
        _store.Observations[InstrumentId] = [Observation(slot, bid)];
        _store.Checkpoints[InstrumentId] = slot;
    }

    private void AddObservation(Instant slot, decimal? bid, decimal? ask = null)
    {
        if (_store.Observations.ContainsKey(InstrumentId) is false)
        {
            _store.Observations[InstrumentId] = [];
        }

        _store.Observations[InstrumentId].Add(Observation(slot, bid, ask));
    }

    private static SavedPriceObservation Observation(
        Instant slot,
        decimal? bid,
        decimal? ask = null)
    {
        return new SavedPriceObservation(slot, bid, ask ?? bid + 1m, "USD");
    }

    private static EvaluableInstrument Instrument()
    {
        return new EvaluableInstrument(InstrumentId, "DEMO", "XNAS", "USD");
    }

    private static EvaluableInstrument SecondInstrument()
    {
        return new EvaluableInstrument(SecondInstrumentId, "DEMO-2", "XNAS", "USD");
    }

    private static MonitoringRule BuildRule(
        Guid revisionId,
        Instant effectiveFrom,
        Instant? effectiveTo,
        ChartAnalysisDefinition definition)
    {
        return MonitoringRule.Restore(
            RuleId,
            InstrumentId,
            T0.Minus(Duration.FromDays(1)),
            [
                new RestoredRevision<ChartAnalysisDefinition>(
                    revisionId,
                    definition,
                    T0.Minus(Duration.FromDays(1)),
                    "synthetic",
                    null,
                    1,
                    effectiveFrom,
                    effectiveTo,
                    null,
                    null)
            ]).Value;
    }

    private static MonitoringRule BuildTwoRevisionRule()
    {
        Instant boundary = T0.Plus(Duration.FromMinutes(3));
        return MonitoringRule.Restore(
            RuleId,
            InstrumentId,
            T0.Minus(Duration.FromDays(1)),
            [
                new RestoredRevision<ChartAnalysisDefinition>(
                    RevisionOneId,
                    Zone(95m, 100m, 105m),
                    T0.Minus(Duration.FromDays(2)),
                    "synthetic",
                    null,
                    1,
                    T0,
                    boundary,
                    null,
                    null),
                new RestoredRevision<ChartAnalysisDefinition>(
                    RevisionTwoId,
                    Zone(95m, 100m, 105m),
                    T0.Minus(Duration.FromDays(1)),
                    "synthetic",
                    null,
                    2,
                    boundary,
                    null,
                    null,
                    null)
            ]).Value;
    }

    private static ChartAnalysisDefinition Zone(decimal lower, decimal level, decimal upper)
    {
        return ChartAnalysisDefinition.Create(
            2,
            [
                ChartZone.Create(
                    ChartAnalysisIdentifier.From("support-a").Value,
                    lower,
                    level,
                    upper,
                    [
                        ChartCondition.Create(
                            ChartConditionType.BuyZone,
                            ChartAnalysisIdentifier.From("notify").Value).Value,
                        ChartCondition.Create(
                            ChartConditionType.SupportLoss,
                            ChartAnalysisIdentifier.From("notify").Value).Value
                    ]).Value
            ],
            []).Value;
    }
}
