using NodaTime;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Application.Notifications;

public sealed record PendingNotification(
    Guid IntentId,
    Guid InstrumentId,
    string Symbol,
    string Exchange,
    Instant ScheduledFor,
    Instant? PreviousScheduledFor,
    Guid MonitoringRuleRevisionId,
    PriceBasis PriceBasis,
    string QuoteCurrency,
    decimal? CurrentBid,
    decimal? CurrentAsk,
    IReadOnlyList<TriggeredCondition> Matches,
    NotificationStatus Status,
    int AttemptCount,
    Guid? CollectionInvocationId);
