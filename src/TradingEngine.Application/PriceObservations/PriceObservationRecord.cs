using NodaTime;
using TradingEngine.Application.MarketData;

namespace TradingEngine.Application.PriceObservations;

public sealed record PriceObservationRecord(
    Instant ScheduledFor,
    Instant RetrievedAt,
    Instant? ProviderObservedAt,
    decimal? Bid,
    decimal? Ask,
    string QuoteCurrency,
    string Provider,
    MarketQuoteTimeliness Timeliness);
