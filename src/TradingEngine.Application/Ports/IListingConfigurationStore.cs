using TradingEngine.Application.Listings;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Ports;

public interface IListingConfigurationStore
{
    Task<Result<ListingConfigurationSnapshot>> FindAsync(
        string exchange,
        string symbol,
        CancellationToken cancellationToken);

    Task<Result<ListingConfigurationSnapshot>> SaveAsync(
        ListingConfigurationUpsert upsert,
        byte[]? expectedConcurrencyToken,
        CancellationToken cancellationToken);
}
