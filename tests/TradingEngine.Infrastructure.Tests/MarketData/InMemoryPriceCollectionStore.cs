using NodaTime;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Infrastructure.Tests.MarketData;

internal sealed class InMemoryPriceCollectionStore : IPriceObservationStore, IMonitoredInstrumentSource
{
    private readonly IReadOnlyList<MonitoredInstrument> _instruments;
    private readonly Dictionary<Guid, Instant> _latestSlots = [];
    private readonly Dictionary<Guid, PriceCollectionAttempt> _latestAttempts = [];

    public InMemoryPriceCollectionStore(MarketInstrumentIdentity[] instruments)
    {
        _instruments = instruments.Select((instrument, index) => new MonitoredInstrument(
            Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}"),
            instrument.Exchange, instrument.Symbol, instrument.QuoteCurrency, 900)).ToArray();
    }

    public List<ScheduledPriceObservation> Observations { get; } = [];

    public List<PriceCollectionAttempt> Attempts { get; } = [];

    public Task<IReadOnlyList<MonitoredInstrument>> ListMonitoredAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_instruments);
    }

    public Task<IReadOnlyDictionary<Guid, Instant>> GetLatestScheduledForAsync(
        IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyDictionary<Guid, Instant>>(new Dictionary<Guid, Instant>(_latestSlots));
    }

    public Task<IReadOnlyDictionary<Guid, PriceCollectionAttempt>> GetLatestAttemptsAsync(
        IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyDictionary<Guid, PriceCollectionAttempt>>(
            new Dictionary<Guid, PriceCollectionAttempt>(_latestAttempts));
    }

    public async Task<bool> TryRecordObservationAndAttemptAsync(
        ScheduledPriceObservation observation, PriceCollectionAttempt attempt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_latestSlots.TryGetValue(observation.InstrumentId, out Instant slot)
            && slot >= observation.ScheduledFor)
        {
            return false;
        }

        Observations.Add(observation);
        _latestSlots[observation.InstrumentId] = observation.ScheduledFor;
        await RecordAttemptAsync(attempt, cancellationToken);
        return true;
    }

    public Task RecordAttemptAsync(PriceCollectionAttempt attempt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Attempts.Add(attempt);
        _latestAttempts[attempt.InstrumentId] = attempt;
        return Task.CompletedTask;
    }
}
