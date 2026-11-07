using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.IntegrationTests.PriceObservations;

internal sealed class CountingMarketDataProvider : IMarketDataProvider
{
    public int CallCount { get; private set; }

    public Task<Result<MarketQuote>> GetCurrentQuoteAsync(
        MarketInstrumentIdentity instrument,
        CancellationToken cancellationToken)
    {
        CallCount++;
        throw new InvalidOperationException("The price history read must not call the market-data provider.");
    }
}
