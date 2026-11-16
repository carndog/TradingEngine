using TradingEngine.Application.MarketData;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Ports;

public interface IMarketDataRequestGuard
{
    IReadOnlyDictionary<MarketInstrumentIdentity, Error> GetBlocked(
        IReadOnlyCollection<MarketInstrumentIdentity> instruments);
}
