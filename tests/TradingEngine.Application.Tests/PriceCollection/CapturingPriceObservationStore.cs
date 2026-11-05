using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Application.Tests.PriceCollection;

internal sealed class CapturingPriceObservationStore : IPriceObservationStore
{
    private readonly HashSet<(Guid InstrumentId, Instant Slot)> _taken = [];
    private readonly object _gate = new();

    public Dictionary<Guid, Instant> LatestSlots { get; set; } = [];

    public bool ForceDuplicate { get; set; }

    public List<ScheduledPriceObservation> Observations { get; } = [];

    public List<PriceCollectionAttempt> Attempts { get; } = [];

    public Task<IReadOnlyDictionary<Guid, Instant>> GetLatestScheduledForAsync(
        IReadOnlyCollection<Guid> instrumentIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyDictionary<Guid, Instant>>(
            new Dictionary<Guid, Instant>(LatestSlots));
    }

    public Task<bool> TryRecordObservationAsync(
        ScheduledPriceObservation observation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            bool fresh = ForceDuplicate is false
                && _taken.Add((observation.InstrumentId, observation.ScheduledFor));
            if (fresh)
            {
                Observations.Add(observation);
            }

            return Task.FromResult(fresh);
        }
    }

    public Task RecordAttemptAsync(
        PriceCollectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            Attempts.Add(attempt);
        }

        return Task.CompletedTask;
    }
}
