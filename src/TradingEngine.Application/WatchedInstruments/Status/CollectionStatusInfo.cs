using NodaTime;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Application.WatchedInstruments.Status;

public sealed record CollectionStatusInfo(
    InstrumentCollectionStatus Status,
    string? ReasonCode,
    string? Reason,
    Instant? LastObservedAt,
    Instant? LastAttemptAt,
    PriceCollectionOutcome? LastAttemptOutcome,
    string? LastAttemptErrorCode);
