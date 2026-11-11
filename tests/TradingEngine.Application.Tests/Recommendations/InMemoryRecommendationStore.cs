using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Recommendations;

namespace TradingEngine.Application.Tests.Recommendations;

internal sealed class InMemoryRecommendationStore : IRecommendationEvaluationStore
{
    private readonly object _gate = new();

    public List<EvaluableInstrument> Instruments { get; } = [];

    public Dictionary<Guid, List<SavedPriceObservation>> Observations { get; } = [];

    public Dictionary<Guid, Instant> Checkpoints { get; } = [];

    public Dictionary<(Guid, Instant), TriggeredRecommendation> Intents { get; } = [];

    public int CommitCalls { get; private set; }

    public Action<RecommendationCommit>? CommitHook { get; set; }

    public Task<IReadOnlyList<EvaluableInstrument>> ListEvaluableAsync(
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<EvaluableInstrument>>(
                Instruments.ToList());
        }
    }

    public Task<EvaluationCheckpoint?> GetCheckpointAsync(
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            EvaluationCheckpoint? checkpoint = Checkpoints.TryGetValue(
                    instrumentId,
                    out Instant slot)
                ? new EvaluationCheckpoint(instrumentId, slot)
                : null;

            return Task.FromResult(checkpoint);
        }
    }

    public Task<bool> TrySeedCheckpointAsync(
        Guid instrumentId,
        Instant establishedAt,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (Checkpoints.ContainsKey(instrumentId))
            {
                return Task.FromResult(false);
            }

            Checkpoints[instrumentId] = establishedAt;
            return Task.FromResult(true);
        }
    }

    public Task<SavedPriceObservation?> GetObservationAtAsync(
        Guid instrumentId,
        Instant scheduledFor,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            SavedPriceObservation? observation = Observations
                .GetValueOrDefault(instrumentId, [])
                .SingleOrDefault(item => item.ScheduledFor == scheduledFor);

            return Task.FromResult(observation);
        }
    }

    public Task<SavedPriceObservation?> GetLatestObservationAsync(
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            SavedPriceObservation? latest = Observations
                .GetValueOrDefault(instrumentId, [])
                .OrderByDescending(item => item.ScheduledFor)
                .FirstOrDefault();

            return Task.FromResult(latest);
        }
    }

    public Task<IReadOnlyList<SavedPriceObservation>> ListObservationsAfterAsync(
        Guid instrumentId,
        Instant afterScheduledFor,
        int maxCount,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            List<SavedPriceObservation> pending = Observations
                .GetValueOrDefault(instrumentId, [])
                .Where(item => item.ScheduledFor > afterScheduledFor)
                .OrderBy(item => item.ScheduledFor)
                .Take(maxCount)
                .ToList();

            return Task.FromResult<IReadOnlyList<SavedPriceObservation>>(pending);
        }
    }

    public Task<EvaluationCommitOutcome> CommitEvaluationAsync(
        RecommendationCommit commit,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            CommitCalls++;
            CommitHook?.Invoke(commit);

            if (Checkpoints.TryGetValue(commit.InstrumentId, out Instant current) is false
                || current != commit.ExpectedCheckpoint)
            {
                return Task.FromResult(EvaluationCommitOutcome.CheckpointConflict);
            }

            (Guid, Instant) key = (commit.InstrumentId, commit.ProcessedSlot);
            if (commit.Intent is not null && Intents.ContainsKey(key))
            {
                Checkpoints[commit.InstrumentId] = commit.ProcessedSlot;
                return Task.FromResult(EvaluationCommitOutcome.AlreadyRecorded);
            }

            if (commit.Intent is not null)
            {
                Intents[key] = commit.Intent;
            }

            Checkpoints[commit.InstrumentId] = commit.ProcessedSlot;
            return Task.FromResult(EvaluationCommitOutcome.Committed);
        }
    }
}
