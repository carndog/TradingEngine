using NodaTime;

namespace TradingEngine.Application.Notifications;

public sealed record IntentDispatchResult(
    Guid IntentId,
    Guid InstrumentId,
    Instant ScheduledFor,
    DispatchItemOutcome Outcome,
    string? ErrorKind);
