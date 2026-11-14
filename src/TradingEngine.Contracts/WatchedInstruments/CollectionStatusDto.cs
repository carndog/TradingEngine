namespace TradingEngine.Contracts.WatchedInstruments;

public sealed record CollectionStatusDto(
    string Status,
    string? ReasonCode,
    string? Reason,
    DateTimeOffset? LastObservedAt,
    DateTimeOffset? LastAttemptAt,
    string? LastAttemptOutcome,
    string? LastAttemptErrorCode);
