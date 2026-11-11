using NodaTime;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Application.Recommendations;

public sealed record TriggeredRecommendation(
    Guid IntentId,
    Guid InstrumentId,
    Instant ScheduledFor,
    Instant? PreviousScheduledFor,
    Guid MonitoringRuleRevisionId,
    PriceBasis PriceBasis,
    string QuoteCurrency,
    decimal? CurrentBid,
    decimal? CurrentAsk,
    IReadOnlyList<TriggeredCondition> Matches);
