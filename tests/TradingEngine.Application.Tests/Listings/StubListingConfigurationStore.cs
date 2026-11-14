using TradingEngine.Application.Listings;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.Listings;

internal sealed class StubListingConfigurationStore : IListingConfigurationStore
{
    public Result<ListingConfigurationSnapshot> FindResult { get; set; } =
        InstrumentListingErrors.NotFound;

    public Result<ListingConfigurationSnapshot>? SaveResult { get; set; }

    public ListingConfigurationUpsert? SavedUpsert { get; private set; }

    public byte[]? ExpectedToken { get; private set; }

    public Task<Result<ListingConfigurationSnapshot>> FindAsync(
        string exchange,
        string symbol,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(FindResult);
    }

    public Task<Result<ListingConfigurationSnapshot>> SaveAsync(
        ListingConfigurationUpsert upsert,
        byte[]? expectedConcurrencyToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SavedUpsert = upsert;
        ExpectedToken = expectedConcurrencyToken;

        if (SaveResult is not null)
        {
            return Task.FromResult(SaveResult);
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

        return Task.FromResult<Result<ListingConfigurationSnapshot>>(
            new ListingConfigurationSnapshot(listing.Value, [9, 9, 9, 9, 9, 9, 9, 9]));
    }
}
