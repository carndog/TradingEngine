using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.WatchedInstruments.Read;

public sealed class GetWatchedInstrumentHandler
{
    private readonly IWatchedInstrumentStore _store;
    private readonly IClock _clock;

    public GetWatchedInstrumentHandler(IWatchedInstrumentStore store, IClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<Result<WatchedInstrumentConfiguration>> HandleAsync(
        GetWatchedInstrument query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return _store.GetAsync(query.InstrumentId, _clock.GetCurrentInstant(), cancellationToken);
    }
}
