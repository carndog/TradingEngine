using TradingEngine.Application.MarketData;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Ports;

public interface INativeQuoteUnitMap
{
    Task<Result<NativeQuoteUnitResolution>> ResolveAsync(
        string exchange,
        string symbol,
        CancellationToken cancellationToken);
}
