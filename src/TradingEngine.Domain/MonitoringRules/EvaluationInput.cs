namespace TradingEngine.Domain.MonitoringRules;

public sealed record EvaluationInput(
    Guid MonitoringRuleRevisionId,
    ChartAnalysisDefinition Definition,
    PriceObservation? Previous,
    PriceObservation Current,
    PriceBasis PriceBasis);
