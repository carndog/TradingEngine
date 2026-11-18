using NodaTime;

namespace TradingEngine.Application.Signals;

public sealed record SignalQuote(
    Guid Id,
    Instant ScheduledFor,
    Instant RetrievedAt,
    Instant? ProviderObservedAt,
    decimal? Bid,
    decimal? Ask,
    string QuoteCurrency)
{
    public Instant ObservedAt => ProviderObservedAt ?? RetrievedAt;
}
