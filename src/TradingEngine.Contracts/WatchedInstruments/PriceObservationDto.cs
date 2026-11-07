namespace TradingEngine.Contracts.WatchedInstruments;

public sealed record PriceObservationDto(
    DateTimeOffset ScheduledFor,
    DateTimeOffset RetrievedAt,
    DateTimeOffset? ProviderObservedAt,
    decimal? Bid,
    decimal? Ask,
    string QuoteCurrency,
    string Provider,
    string Timeliness);
