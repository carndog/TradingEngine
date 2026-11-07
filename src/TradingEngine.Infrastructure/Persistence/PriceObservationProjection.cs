using NodaTime;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed record PriceObservationProjection(
    Instant ScheduledFor,
    Instant RetrievedAt,
    Instant? ProviderObservedAt,
    decimal? Bid,
    decimal? Ask,
    string QuoteCurrency,
    string Provider,
    string Timeliness);
