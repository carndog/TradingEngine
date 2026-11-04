using TradingEngine.Application.MarketData;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Ports;

public interface IMarketDataProvider
{
    Task<Result<MarketQuote>> GetCurrentQuoteAsync(
        MarketInstrumentIdentity instrument,
        CancellationToken cancellationToken);
}
