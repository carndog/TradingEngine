using NodaTime;

namespace TradingEngine.Domain.MonitoringRules;

public sealed record EvaluationResult(
    Guid MonitoringRuleRevisionId,
    Instant ObservedAt,
    Instant? PreviousObservedAt,
    EvaluationOutcome Outcome,
    EvaluationReason Reason,
    PriceBasis PriceBasis,
    string QuoteCurrency,
    decimal? CurrentBid,
    decimal? CurrentAsk,
    IReadOnlyList<TriggeredCondition> Matches);
