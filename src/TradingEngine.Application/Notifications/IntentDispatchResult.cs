using NodaTime;

namespace TradingEngine.Application.Notifications;

public sealed record IntentDispatchResult(
    Guid IntentId,
    Guid InstrumentId,
    Instant ScheduledFor,
    Guid MonitoringRuleRevisionId,
    Guid? CollectionInvocationId,
    DispatchItemOutcome Outcome,
    string? ErrorKind);
