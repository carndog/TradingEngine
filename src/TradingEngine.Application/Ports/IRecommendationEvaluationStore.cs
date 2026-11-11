using NodaTime;
using TradingEngine.Application.Recommendations;

namespace TradingEngine.Application.Ports;

public interface IRecommendationEvaluationStore
{
    Task<IReadOnlyList<EvaluableInstrument>> ListEvaluableAsync(
        CancellationToken cancellationToken);

    Task<EvaluationCheckpoint?> GetCheckpointAsync(
        Guid instrumentId,
        CancellationToken cancellationToken);

    Task<bool> TrySeedCheckpointAsync(
        Guid instrumentId,
        Instant establishedAt,
        CancellationToken cancellationToken);

    Task<SavedPriceObservation?> GetObservationAtAsync(
        Guid instrumentId,
        Instant scheduledFor,
        CancellationToken cancellationToken);

    Task<SavedPriceObservation?> GetLatestObservationAsync(
        Guid instrumentId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SavedPriceObservation>> ListObservationsAfterAsync(
        Guid instrumentId,
        Instant afterScheduledFor,
        int maxCount,
        CancellationToken cancellationToken);

    Task<EvaluationCommitOutcome> CommitEvaluationAsync(
        RecommendationCommit commit,
        CancellationToken cancellationToken);
}
