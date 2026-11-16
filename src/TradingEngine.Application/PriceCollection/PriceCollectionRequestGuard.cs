using NodaTime;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.PriceCollection;

public sealed class PriceCollectionRequestGuard : IMarketDataRequestGuard
{
    private readonly IMarketSessionPolicy _sessions;
    private readonly IClock _clock;
    private readonly Instant _deadline;
    private readonly IReadOnlyDictionary<MarketInstrumentIdentity, Instant> _expiresAt;

    public PriceCollectionRequestGuard(
        IMarketSessionPolicy sessions, IClock clock, Instant deadline,
        IReadOnlyDictionary<MarketInstrumentIdentity, Instant> expiresAt)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(expiresAt);
        _sessions = sessions;
        _clock = clock;
        _deadline = deadline;
        _expiresAt = expiresAt;
    }

    public IReadOnlyDictionary<MarketInstrumentIdentity, Error> GetBlocked(
        IReadOnlyCollection<MarketInstrumentIdentity> instruments)
    {
        Instant now = _clock.GetCurrentInstant();
        Dictionary<string, MarketSessionDecision> decisions = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<MarketInstrumentIdentity, Error> blocked = [];
        foreach (MarketInstrumentIdentity instrument in instruments)
        {
            if (now >= _deadline)
            {
                blocked[instrument] = PriceCollectionErrors.DeadlineExceeded;
            }
            else if (_expiresAt.TryGetValue(instrument, out Instant expiry) is false || now >= expiry)
            {
                blocked[instrument] = PriceCollectionErrors.SlotExpired;
            }
            else
            {
                if (decisions.TryGetValue(instrument.Exchange, out MarketSessionDecision? decision) is false)
                {
                    decision = _sessions.GetDecision(instrument.Exchange, now);
                    decisions[instrument.Exchange] = decision;
                }

                if (decision.BlockingError is Error error)
                {
                    blocked[instrument] = error;
                }
            }
        }

        return blocked;
    }
}
