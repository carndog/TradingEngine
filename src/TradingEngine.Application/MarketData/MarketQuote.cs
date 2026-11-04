using NodaTime;

namespace TradingEngine.Application.MarketData;

public sealed record MarketQuote(
    MarketInstrumentIdentity Instrument,
    string Provider,
    long ProviderInstrumentId,
    decimal? Bid,
    decimal? Ask,
    Instant? ProviderObservedAt,
    Instant RetrievedAt,
    MarketQuoteTimeliness Timeliness,
    string? ProviderRequestId);
