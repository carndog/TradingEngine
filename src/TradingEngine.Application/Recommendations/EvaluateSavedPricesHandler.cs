using NodaTime;
using TradingEngine.Application.MonitoringRules;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Time;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;

namespace TradingEngine.Application.Recommendations;

public sealed class EvaluateSavedPricesHandler
{
    private readonly IRecommendationEvaluationStore _store;
    private readonly IMonitoringRuleStore _ruleStore;
    private readonly ChartAnalysisEvaluator _evaluator;
    private readonly IClock _clock;
    private readonly TimeProvider _timeProvider;
    private readonly RecommendationOptions _options;
    private readonly PriceBasis _priceBasis;

    public EvaluateSavedPricesHandler(
        IRecommendationEvaluationStore store,
        IMonitoringRuleStore ruleStore,
        ChartAnalysisEvaluator evaluator,
        IClock clock,
        TimeProvider timeProvider,
        RecommendationOptions options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _ruleStore = ruleStore ?? throw new ArgumentNullException(nameof(ruleStore));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _priceBasis = ParsePriceBasis(options);
    }

    public async Task<EvaluationRunResult> HandleAsync(
        EvaluateSavedPrices command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Instant startedAt = PersistedInstant.Require(
            _clock.GetCurrentInstant(),
            nameof(command));
        Guid invocationId = Guid.CreateVersion7();
        Instant deadline = startedAt.Plus(
            Duration.FromSeconds(_options.MaxExecutionTimeSeconds));

        using CancellationTokenSource budgetTimeout = new(
            TimeSpan.FromSeconds(_options.MaxExecutionTimeSeconds),
            _timeProvider);
        using CancellationTokenSource budgetScope =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                budgetTimeout.Token);
        CancellationToken budgetToken = budgetScope.Token;

        IReadOnlyList<EvaluableInstrument> instruments;
        try
        {
            instruments = await _store.ListEvaluableAsync(budgetToken);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested is false)
        {
            return new EvaluationRunResult(
                invocationId,
                startedAt,
                _clock.GetCurrentInstant(),
                true,
                []);
        }

        List<InstrumentEvaluationResult> items = [];
        bool deadlineReached = false;
        foreach (EvaluableInstrument instrument in instruments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            InstrumentEvaluationResult item = await ProcessInstrumentAsync(
                instrument,
                deadline,
                budgetToken,
                cancellationToken);
            items.Add(item);

            if (item.FailureCode == RecommendationErrors.DeadlineExceeded.Code
                || _clock.GetCurrentInstant() >= deadline)
            {
                deadlineReached = true;
                break;
            }
        }

        return new EvaluationRunResult(
            invocationId,
            startedAt,
            _clock.GetCurrentInstant(),
            deadlineReached,
            items);
    }

    private async Task<InstrumentEvaluationResult> ProcessInstrumentAsync(
        EvaluableInstrument instrument,
        Instant deadline,
        CancellationToken budgetToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ProcessInstrumentCoreAsync(
                instrument,
                deadline,
                budgetToken,
                cancellationToken);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested is false)
        {
            return Failed(
                instrument.Id,
                null,
                RecommendationErrors.DeadlineExceeded.Code);
        }
        catch (Exception)
        {
            return Failed(
                instrument.Id,
                null,
                RecommendationErrors.StorageError.Code);
        }
    }

    private async Task<InstrumentEvaluationResult> ProcessInstrumentCoreAsync(
        EvaluableInstrument instrument,
        Instant deadline,
        CancellationToken budgetToken,
        CancellationToken cancellationToken)
    {
        EvaluationCheckpoint? checkpoint = await _store.GetCheckpointAsync(
            instrument.Id,
            budgetToken);

        if (checkpoint is null)
        {
            checkpoint = await EstablishBaselineAsync(
                instrument,
                budgetToken,
                cancellationToken);
            if (checkpoint is null)
            {
                return Idle(instrument.Id);
            }
        }

        SavedPriceObservation? previous = await _store.GetObservationAtAsync(
            instrument.Id,
            checkpoint.LastProcessedScheduledFor,
            budgetToken);
        if (previous is null)
        {
            return Failed(
                instrument.Id,
                checkpoint.LastProcessedScheduledFor,
                RecommendationErrors.BaselineObservationMissing.Code);
        }

        IReadOnlyList<SavedPriceObservation> pending =
            await _store.ListObservationsAfterAsync(
                instrument.Id,
                checkpoint.LastProcessedScheduledFor,
                _options.MaxObservationsPerInstrument,
                budgetToken);
        if (pending.Count == 0)
        {
            return Idle(instrument.Id);
        }

        Result<MonitoringRuleSnapshot> snapshot = await _ruleStore.GetAsync(
            instrument.Id,
            budgetToken);
        if (snapshot.IsFailure)
        {
            return Failed(
                instrument.Id,
                checkpoint.LastProcessedScheduledFor,
                snapshot.Error.Code);
        }

        return await ProcessPendingAsync(
            instrument,
            checkpoint.LastProcessedScheduledFor,
            previous,
            pending,
            snapshot.Value.Rule,
            deadline,
            budgetToken);
    }

    private async Task<EvaluationCheckpoint?> EstablishBaselineAsync(
        EvaluableInstrument instrument,
        CancellationToken budgetToken,
        CancellationToken cancellationToken)
    {
        SavedPriceObservation? latest = await _store.GetLatestObservationAsync(
            instrument.Id,
            budgetToken);
        if (latest is null)
        {
            return null;
        }

        await _store.TrySeedCheckpointAsync(
            instrument.Id,
            latest.ScheduledFor,
            cancellationToken);

        return await _store.GetCheckpointAsync(instrument.Id, budgetToken);
    }

    private async Task<InstrumentEvaluationResult> ProcessPendingAsync(
        EvaluableInstrument instrument,
        Instant checkpointSlot,
        SavedPriceObservation previous,
        IReadOnlyList<SavedPriceObservation> pending,
        MonitoringRule rule,
        Instant deadline,
        CancellationToken budgetToken)
    {
        int evaluated = 0;
        int noAction = 0;
        int skippedNoRevision = 0;
        List<CommittedIntent> committedIntents = [];
        Instant? failedSlot = null;
        string? failureCode = null;
        Instant expected = checkpointSlot;

        foreach (SavedPriceObservation observation in pending)
        {
            if (_clock.GetCurrentInstant() >= deadline)
            {
                failedSlot = observation.ScheduledFor;
                failureCode = RecommendationErrors.DeadlineExceeded.Code;
                break;
            }

            Revision<ChartAnalysisDefinition>? revision =
                rule.EffectiveAt(observation.ScheduledFor);
            TriggeredRecommendation? intent = null;
            bool ranEvaluation = false;
            bool triggeredHere = false;

            if (revision is not null)
            {
                Result<EvaluationResult> evaluation = Evaluate(
                    revision,
                    previous,
                    observation);
                if (evaluation.IsFailure)
                {
                    failedSlot = observation.ScheduledFor;
                    failureCode = evaluation.Error.Code;
                    break;
                }

                ranEvaluation = true;
                if (evaluation.Value.Outcome == EvaluationOutcome.Triggered)
                {
                    intent = MapIntent(
                        instrument.Id,
                        observation.InvocationId,
                        evaluation.Value);
                    triggeredHere = true;
                }
            }

            EvaluationCommitOutcome committed = await _store.CommitEvaluationAsync(
                new RecommendationCommit(
                    instrument.Id,
                    expected,
                    observation.ScheduledFor,
                    _clock.GetCurrentInstant(),
                    intent),
                budgetToken);

            if (committed == EvaluationCommitOutcome.CheckpointConflict)
            {
                failedSlot = observation.ScheduledFor;
                failureCode = RecommendationErrors.CheckpointConflict.Code;
                break;
            }

            expected = observation.ScheduledFor;
            previous = observation;
            if (ranEvaluation)
            {
                evaluated++;
                if (triggeredHere is false)
                {
                    noAction++;
                }
            }
            else
            {
                skippedNoRevision++;
            }

            if (committed == EvaluationCommitOutcome.Committed && intent is not null)
            {
                committedIntents.Add(new CommittedIntent(
                    intent.IntentId,
                    intent.ScheduledFor,
                    intent.MonitoringRuleRevisionId,
                    intent.CollectionInvocationId));
            }
        }

        return new InstrumentEvaluationResult(
            instrument.Id,
            evaluated,
            noAction,
            skippedNoRevision,
            committedIntents,
            failedSlot,
            failureCode);
    }

    private Result<EvaluationResult> Evaluate(
        Revision<ChartAnalysisDefinition> revision,
        SavedPriceObservation previous,
        SavedPriceObservation current)
    {
        Result<PriceObservation> previousObservation = ToDomainObservation(previous);
        Result<PriceObservation> currentObservation = ToDomainObservation(current);

        if (previousObservation.IsFailure)
        {
            return previousObservation.Error;
        }

        if (currentObservation.IsFailure)
        {
            return currentObservation.Error;
        }

        return _evaluator.Evaluate(new EvaluationInput(
            revision.Id,
            revision.Definition,
            previousObservation.Value,
            currentObservation.Value,
            _priceBasis));
    }

    private static Result<PriceObservation> ToDomainObservation(
        SavedPriceObservation observation)
    {
        return PriceObservation.Create(
            observation.ScheduledFor,
            observation.Bid,
            observation.Ask,
            observation.QuoteCurrency);
    }

    private static TriggeredRecommendation MapIntent(
        Guid instrumentId,
        Guid collectionInvocationId,
        EvaluationResult result)
    {
        return new TriggeredRecommendation(
            Guid.CreateVersion7(),
            instrumentId,
            result.ObservedAt,
            result.PreviousObservedAt,
            result.MonitoringRuleRevisionId,
            result.PriceBasis,
            result.QuoteCurrency,
            result.CurrentBid,
            result.CurrentAsk,
            result.Matches,
            collectionInvocationId);
    }

    private static InstrumentEvaluationResult Idle(Guid instrumentId)
    {
        return new InstrumentEvaluationResult(
            instrumentId,
            0,
            0,
            0,
            [],
            null,
            null);
    }

    private static InstrumentEvaluationResult Failed(
        Guid instrumentId,
        Instant? slot,
        string errorCode)
    {
        return new InstrumentEvaluationResult(
            instrumentId,
            0,
            0,
            0,
            [],
            slot,
            errorCode);
    }

    private static PriceBasis ParsePriceBasis(RecommendationOptions options)
    {
        if (Enum.TryParse(options.PriceBasis, true, out PriceBasis parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Recommendations:PriceBasis '{options.PriceBasis}' is not a defined value; " +
            "use 'Bid' or 'Ask'.");
    }
}
