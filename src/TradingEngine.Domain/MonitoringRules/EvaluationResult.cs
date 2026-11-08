using NodaTime;

namespace TradingEngine.Domain.MonitoringRules;

public sealed record EvaluationResult
{
    public EvaluationResult(
        Guid monitoringRuleRevisionId,
        Instant observedAt,
        Instant? previousObservedAt,
        EvaluationOutcome outcome,
        EvaluationReason reason,
        PriceBasis priceBasis,
        string quoteCurrency,
        decimal? currentBid,
        decimal? currentAsk,
        IReadOnlyList<TriggeredCondition> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        MonitoringRuleRevisionId = monitoringRuleRevisionId;
        ObservedAt = observedAt;
        PreviousObservedAt = previousObservedAt;
        Outcome = outcome;
        Reason = reason;
        PriceBasis = priceBasis;
        QuoteCurrency = quoteCurrency;
        CurrentBid = currentBid;
        CurrentAsk = currentAsk;
        Matches = Array.AsReadOnly(matches.ToArray());
    }

    public Guid MonitoringRuleRevisionId { get; }

    public Instant ObservedAt { get; }

    public Instant? PreviousObservedAt { get; }

    public EvaluationOutcome Outcome { get; }

    public EvaluationReason Reason { get; }

    public PriceBasis PriceBasis { get; }

    public string QuoteCurrency { get; }

    public decimal? CurrentBid { get; }

    public decimal? CurrentAsk { get; }

    public IReadOnlyList<TriggeredCondition> Matches { get; }
}
