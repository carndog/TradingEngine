using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Application.Tests.PriceCollection;

internal sealed class StubMarketSessionPolicy : IMarketSessionPolicy
{
    public List<(string Exchange, Instant At)> Calls { get; } = [];

    public Func<string, Instant, ExchangeSessionStatus>? Responder { get; set; }

    public ExchangeSessionStatus GetStatus(string exchange, Instant at)
    {
        Calls.Add((exchange, at));
        return Responder?.Invoke(exchange, at) ?? ExchangeSessionStatus.Open;
    }
}
