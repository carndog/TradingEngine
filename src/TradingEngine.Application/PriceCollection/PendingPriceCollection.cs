using NodaTime;
using TradingEngine.Application.MarketData;

namespace TradingEngine.Application.PriceCollection;

internal sealed record PendingPriceCollection(
    MonitoredInstrument Instrument,
    Instant ScheduledFor,
    Instant AttemptedAt,
    Instant ExpiresAt)
{
    public MarketInstrumentIdentity Identity => new(
        Instrument.Exchange, Instrument.Symbol, Instrument.QuoteCurrency);
}
