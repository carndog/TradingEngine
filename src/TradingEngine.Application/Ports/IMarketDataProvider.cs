using TradingEngine.Application.MarketData;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Ports;

public interface IMarketDataProvider
{
    int MaxBatchSize => 1;

    MarketDataRequestStatistics RequestStatistics => MarketDataRequestStatistics.Empty;

    Task<Result<MarketQuote>> GetCurrentQuoteAsync(
        MarketInstrumentIdentity instrument,
        CancellationToken cancellationToken);

    async Task<IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>>> GetCurrentQuotesAsync(
        IReadOnlyCollection<MarketInstrumentIdentity> instruments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instruments);

        Dictionary<MarketInstrumentIdentity, Result<MarketQuote>> results = [];
        foreach (MarketInstrumentIdentity instrument in instruments.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            results[instrument] = await GetCurrentQuoteAsync(instrument, cancellationToken);
        }

        return results;
    }

    Task<Result<MarketListing>> ResolveListingAsync(
        MarketListingQuery query,
        CancellationToken cancellationToken);
}
