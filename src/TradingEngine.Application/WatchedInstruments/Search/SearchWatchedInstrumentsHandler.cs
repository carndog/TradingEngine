using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.WatchedInstruments.Search;

public sealed class SearchWatchedInstrumentsHandler
{
    private readonly IWatchedInstrumentStore _store;

    public SearchWatchedInstrumentsHandler(IWatchedInstrumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<WatchedInstrumentPage>> HandleAsync(
        SearchWatchedInstruments query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await _store.SearchAsync(
            query.Query,
            query.Page,
            query.PageSize,
            cancellationToken);
    }
}
