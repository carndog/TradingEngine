using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Api.IntegrationTests.WatchedInstruments;

internal sealed class StubPriceObservationStore : IPriceObservationStore
{
    public Dictionary<Guid, PriceCollectionAttempt> LatestAttempts { get; } = [];

    public Dictionary<Guid, Instant> LatestSlots { get; } = [];

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

    public Task<bool> TryRecordObservationAndAttemptAsync(
        ScheduledPriceObservation observation,
        PriceCollectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Observations.Add(observation);
        Attempts.Add(attempt);
        LatestAttempts[observation.InstrumentId] = attempt;
        LatestSlots[observation.InstrumentId] = observation.ScheduledFor;
        return Task.FromResult(true);
    }

    public Task RecordAttemptAsync(
        PriceCollectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Attempts.Add(attempt);
        LatestAttempts[attempt.InstrumentId] = attempt;
        return Task.CompletedTask;
    }
}
