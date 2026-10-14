using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.WatchedInstruments.Settings;

public sealed class GetWatchedInstrumentSettingsHandler
{
    private readonly IWatchedInstrumentStore _store;

    public GetWatchedInstrumentSettingsHandler(IWatchedInstrumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<WatchedInstrumentSettingsSnapshot>> HandleAsync(
        GetWatchedInstrumentSettings query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await _store.GetSettingsAsync(query.InstrumentId, cancellationToken);
    }
}
