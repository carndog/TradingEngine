using TradingEngine.Application.Listings;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.IntegrationTests.WatchedInstruments;

internal sealed class StubListingConfigurationStore : IListingConfigurationStore
{
    private readonly Dictionary<(string Exchange, string Symbol), ListingConfigurationSnapshot> _rows =
        [];

    public List<ListingConfigurationUpsert> SavedUpserts { get; } = [];

    public List<byte[]?> ExpectedTokens { get; } = [];

    public void Seed(ListingConfigurationSnapshot snapshot)
    {
        _rows[(snapshot.Listing.Exchange, snapshot.Listing.Symbol)] = snapshot;
    }

    public Task<Result<ListingConfigurationSnapshot>> FindAsync(
        string exchange,
        string symbol,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            _rows.TryGetValue((exchange, symbol), out ListingConfigurationSnapshot? snapshot)
                ? Result<ListingConfigurationSnapshot>.Success(snapshot)
                : Result<ListingConfigurationSnapshot>.Failure(InstrumentListingErrors.NotFound));
    }

    public Task<Result<ListingConfigurationSnapshot>> SaveAsync(
        ListingConfigurationUpsert upsert,
        byte[]? expectedConcurrencyToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SavedUpserts.Add(upsert);
        ExpectedTokens.Add(expectedConcurrencyToken);

        (string Exchange, string Symbol) key = (upsert.Exchange, upsert.Symbol);
        if (_rows.TryGetValue(key, out ListingConfigurationSnapshot? existing))
        {
            if (expectedConcurrencyToken is null)
            {
                return Task.FromResult<Result<ListingConfigurationSnapshot>>(
                    InstrumentListingErrors.ConcurrencyTokenRequired);
            }

            if (existing.ConcurrencyToken.SequenceEqual(expectedConcurrencyToken) is false)
            {
                return Task.FromResult<Result<ListingConfigurationSnapshot>>(
                    InstrumentListingErrors.ConcurrentChange);
            }

            if (string.Equals(
                    existing.Listing.NativeQuoteCurrency,
                    upsert.NativeQuoteCurrency,
                    StringComparison.Ordinal) is false)
            {
                return Task.FromResult<Result<ListingConfigurationSnapshot>>(
                    InstrumentListingErrors.UnitConflict);
            }
        }
        else if (expectedConcurrencyToken is not null)
        {
            return Task.FromResult<Result<ListingConfigurationSnapshot>>(
                InstrumentListingErrors.ConcurrentChange);
        }

        Result<InstrumentListing> listing = InstrumentListing.Restore(
            upsert.Exchange,
            upsert.Symbol,
            upsert.Provider,
            upsert.ProviderInstrumentId,
            upsert.ProviderSymbol,
            upsert.ProviderExchangeName,
            upsert.DisplayName,
            upsert.NativeQuoteCurrency,
            upsert.Source,
            upsert.ConfirmedBy,
            upsert.ConfirmedAt,
            upsert.ConfirmedAt);

        byte[] token = [9, 9, 9, 9, 9, 9, 9, (byte)(_rows.Count + 1)];
        ListingConfigurationSnapshot snapshot = new(listing.Value, token);
        _rows[key] = snapshot;

        return Task.FromResult<Result<ListingConfigurationSnapshot>>(snapshot);
    }
}
