namespace TradingEngine.Contracts.WatchedInstruments;

public sealed record VerificationQuoteDto(
    decimal? Bid,
    decimal? Ask,
    string NativeQuoteCurrency,
    DateTimeOffset RetrievedAt,
    DateTimeOffset? ProviderObservedAt,
    string Timeliness);
