using NodaTime;

namespace TradingEngine.Application.Recommendations;

public sealed record CommittedIntent(
    Guid IntentId,
    Instant ScheduledFor,
    Guid MonitoringRuleRevisionId);
