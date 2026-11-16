using System.Globalization;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Infrastructure.MarketData;

internal sealed class GuardedQuoteRequest
{
    private readonly IMarketDataRequestGuard? _guard;

    public GuardedQuoteRequest(
        IReadOnlyDictionary<MarketInstrumentIdentity, long> resolved, IMarketDataRequestGuard? guard)
    {
        Remaining = new Dictionary<MarketInstrumentIdentity, long>(resolved);
        _guard = guard;
    }

    public Dictionary<MarketInstrumentIdentity, long> Remaining { get; }

    public Dictionary<MarketInstrumentIdentity, Error> Blocked { get; } = [];

    public string? PrepareUri()
    {
        if (_guard is not null)
        {
            foreach (KeyValuePair<MarketInstrumentIdentity, Error> entry in _guard.GetBlocked(Remaining.Keys.ToArray()))
            {
                Blocked[entry.Key] = entry.Value;
                Remaining.Remove(entry.Key);
            }
        }

        return Remaining.Count == 0 ? null : "api/v2/market-data/rates?instrumentIds="
            + string.Join(",", Remaining.Values.Distinct().Select(id => id.ToString(CultureInfo.InvariantCulture)));
    }
}
