using TradingEngine.Application.Ports;
using TradingEngine.Application.WatchedInstruments.Status;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.WatchedInstruments.Settings;

public sealed class GetWatchedInstrumentSettingsHandler
{
    private readonly IWatchedInstrumentStore _store;
    private readonly ListingStatusEvaluator _listingStatus;
    private readonly CollectionStatusEvaluator _collectionStatus;

    public GetWatchedInstrumentSettingsHandler(
        IWatchedInstrumentStore store,
        ListingStatusEvaluator listingStatus,
        CollectionStatusEvaluator collectionStatus)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _listingStatus = listingStatus
            ?? throw new ArgumentNullException(nameof(listingStatus));
        _collectionStatus = collectionStatus
            ?? throw new ArgumentNullException(nameof(collectionStatus));
    }

    public async Task<Result<WatchedInstrumentSettingsSnapshot>> HandleAsync(
        GetWatchedInstrumentSettings query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result<WatchedInstrumentSettingsSnapshot> snapshot =
            await _store.GetSettingsAsync(query.InstrumentId, cancellationToken);
        if (snapshot.IsFailure)
        {
            return snapshot;
        }

        Result<ListingStatusInfo> listing = await _listingStatus.EvaluateAsync(
            snapshot.Value.Instrument.Exchange,
            snapshot.Value.Instrument.Symbol,
            cancellationToken);
        if (listing.IsFailure)
        {
            return listing.Error;
        }

        CollectionStatusInfo collection = await _collectionStatus.EvaluateAsync(
            snapshot.Value.Instrument,
            cancellationToken);

        return snapshot.Value with
        {
            Listing = listing.Value,
            Collection = collection
        };
    }
}
