using NodaTime;

namespace TradingEngine.Application.Notifications;

public sealed record IntentDispatchResult(
    Guid IntentId,
    Guid InstrumentId,
    Instant ScheduledFor,
    Guid MonitoringRuleRevisionId,
    DispatchItemOutcome Outcome,
    string? ErrorKind);
