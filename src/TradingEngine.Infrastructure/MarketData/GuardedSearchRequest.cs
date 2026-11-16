using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Infrastructure.MarketData;

internal sealed class GuardedSearchRequest(
    string uri, MarketInstrumentIdentity? instrument, IMarketDataRequestGuard? guard)
{
    public Error? Blocked { get; private set; }

    public string? PrepareUri()
    {
        if (instrument is not null && guard?.GetBlocked([instrument]).TryGetValue(instrument, out Error? error) == true)
        {
            Blocked = error;
        }

        return Blocked is null ? uri : null;
    }
}
