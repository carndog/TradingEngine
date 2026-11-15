using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.PriceCollection;

internal sealed class BatchMarketDataProvider : IMarketDataProvider
{
    public int MaxBatchSize { get; set; } = 1000;

    public List<MarketInstrumentIdentity[]> Batches { get; } = [];

    public StubMarketDataProvider Single { get; } = new();

    public Func<MarketInstrumentIdentity[], CancellationToken,
        Task<IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>>>>? Responder { get; set; }

    public async Task<IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>>> GetCurrentQuotesAsync(
        IReadOnlyCollection<MarketInstrumentIdentity> instruments,
        CancellationToken cancellationToken)
    {
        MarketInstrumentIdentity[] batch = instruments.ToArray();
        Batches.Add(batch);
        if (Responder is not null)
        {
            return await Responder(batch, cancellationToken);
        }

        Dictionary<MarketInstrumentIdentity, Result<MarketQuote>> results = [];
        foreach (MarketInstrumentIdentity instrument in batch)
        {
            results[instrument] = await Single.GetCurrentQuoteAsync(instrument, cancellationToken);
        }

        return results;
    }

    public Task<Result<MarketQuote>> GetCurrentQuoteAsync(
        MarketInstrumentIdentity instrument,
        CancellationToken cancellationToken)
    {
        return Single.GetCurrentQuoteAsync(instrument, cancellationToken);
    }

    public Task<Result<MarketListing>> ResolveListingAsync(
        MarketListingQuery query,
        CancellationToken cancellationToken)
    {
        return Single.ResolveListingAsync(query, cancellationToken);
    }
}
