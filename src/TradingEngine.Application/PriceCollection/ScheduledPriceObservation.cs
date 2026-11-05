using NodaTime;
using TradingEngine.Application.MarketData;

namespace TradingEngine.Application.PriceCollection;

public sealed record ScheduledPriceObservation(
    Guid InstrumentId,
    Instant ScheduledFor,
    string Provider,
    long ProviderInstrumentId,
    decimal? Bid,
    decimal? Ask,
    string QuoteCurrency,
    Instant? ProviderObservedAt,
    Instant RetrievedAt,
    MarketQuoteTimeliness Timeliness,
    string? ProviderRequestId,
    Guid InvocationId);
