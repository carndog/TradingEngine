using TradingEngine.Application.Listings;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Infrastructure.MarketData;

internal sealed class NullListingConfigurationStore : IListingConfigurationStore
{
    public Task<Result<ListingConfigurationSnapshot>> FindAsync(
        string exchange,
        string symbol,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<Result<ListingConfigurationSnapshot>>(
            InstrumentListingErrors.NotFound);
    }

    public Task<Result<ListingConfigurationSnapshot>> SaveAsync(
        ListingConfigurationUpsert upsert,
        byte[]? expectedConcurrencyToken,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(
            "No listing-configuration store is registered; persisted listing " +
            "confirmation requires the application database.");
    }
}
