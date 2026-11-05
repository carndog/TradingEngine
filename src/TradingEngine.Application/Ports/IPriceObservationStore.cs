using NodaTime;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Application.Ports;

public interface IPriceObservationStore
{
    Task<IReadOnlyDictionary<Guid, Instant>> GetLatestScheduledForAsync(
        IReadOnlyCollection<Guid> instrumentIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, PriceCollectionAttempt>> GetLatestAttemptsAsync(
        IReadOnlyCollection<Guid> instrumentIds,
        CancellationToken cancellationToken);

    Task<bool> TryRecordObservationAndAttemptAsync(
        ScheduledPriceObservation observation,
        PriceCollectionAttempt attempt,
        CancellationToken cancellationToken);

    Task RecordAttemptAsync(
        PriceCollectionAttempt attempt,
        CancellationToken cancellationToken);
}
