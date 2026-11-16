using NodaTime;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Application.Ports;

public interface IMarketSessionPolicy
{
    ExchangeSessionStatus GetStatus(string exchange, Instant at);

    MarketSessionDecision GetDecision(string exchange, Instant at)
    {
        return MarketSessionDecision.FromStatus(GetStatus(exchange, at));
    }
}
