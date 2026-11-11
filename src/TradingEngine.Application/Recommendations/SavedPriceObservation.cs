using NodaTime;

namespace TradingEngine.Application.Recommendations;

public sealed record SavedPriceObservation(
    Instant ScheduledFor,
    decimal? Bid,
    decimal? Ask,
    string QuoteCurrency,
    Guid InvocationId);
