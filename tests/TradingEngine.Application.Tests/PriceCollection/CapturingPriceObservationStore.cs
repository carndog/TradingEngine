using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Application.Tests.PriceCollection;

internal sealed class CapturingPriceObservationStore : IPriceObservationStore
{
    private readonly HashSet<(Guid InstrumentId, Instant Slot)> _taken = [];
    private readonly object _gate = new();

    public Dictionary<Guid, Instant> LatestSlots { get; set; } = [];

    public Dictionary<Guid, PriceCollectionAttempt> LatestAttempts { get; set; } = [];

    public bool ForceDuplicate { get; set; }

    public Func<CancellationToken, Task>? ObservationGate { get; set; }

    public Func<CancellationToken, Task>? AttemptGate { get; set; }

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

    public Task<IReadOnlyDictionary<Guid, PriceCollectionAttempt>> GetLatestAttemptsAsync(
        IReadOnlyCollection<Guid> instrumentIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyDictionary<Guid, PriceCollectionAttempt>>(
            new Dictionary<Guid, PriceCollectionAttempt>(LatestAttempts));
    }

    public async Task<bool> TryRecordObservationAndAttemptAsync(
        ScheduledPriceObservation observation,
        PriceCollectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (ObservationGate is not null)
        {
            await ObservationGate(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            bool fresh = ForceDuplicate is false
                && _taken.Add((observation.InstrumentId, observation.ScheduledFor));
            if (fresh)
            {
                Observations.Add(observation);
                Attempts.Add(attempt);
            }

            return fresh;
        }
    }

    public async Task RecordAttemptAsync(
        PriceCollectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (AttemptGate is not null)
        {
            await AttemptGate(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            Attempts.Add(attempt);
        }
    }
}
