using NodaTime;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Application.Ports;

public interface IPriceObservationStore
{
    Task<IReadOnlyDictionary<Guid, Instant>> GetLatestScheduledForAsync(
        IReadOnlyCollection<Guid> instrumentIds,
        CancellationToken cancellationToken);

    Task<bool> TryRecordObservationAsync(
        ScheduledPriceObservation observation,
        CancellationToken cancellationToken);

    Task RecordAttemptAsync(
        PriceCollectionAttempt attempt,
        CancellationToken cancellationToken);
}
