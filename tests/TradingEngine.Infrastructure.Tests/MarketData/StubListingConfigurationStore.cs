using TradingEngine.Application.Listings;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Infrastructure.Tests.MarketData;

internal sealed class StubListingConfigurationStore : IListingConfigurationStore
{
    public Result<ListingConfigurationSnapshot> FindResult { get; set; } =
        InstrumentListingErrors.NotFound;

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
        return Task.FromResult<Result<ListingConfigurationSnapshot>>(
            InstrumentListingErrors.NotFound);
    }
}
