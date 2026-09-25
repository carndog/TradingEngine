using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.WatchedInstruments.Read;

public sealed class GetWatchedInstrumentHandler
{
    private readonly IWatchedInstrumentStore _store;

    public GetWatchedInstrumentHandler(IWatchedInstrumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<Result<WatchedInstrumentConfiguration>> HandleAsync(
        GetWatchedInstrument query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return _store.GetAsync(query.InstrumentId, cancellationToken);
    }
}
