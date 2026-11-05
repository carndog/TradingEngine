using NodaTime;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Application.Ports;

public interface IMarketSessionPolicy
{
    ExchangeSessionStatus GetStatus(string exchange, Instant at);
}
