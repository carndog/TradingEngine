namespace TradingEngine.Domain.MonitoringRules;

public sealed record TriggeredCondition(
    ChartAnalysisIdentifier ZoneId,
    ChartConditionType Condition,
    ChartAnalysisIdentifier ActionId,
    decimal Lower,
    decimal Level,
    decimal Upper,
    decimal PreviousPrice,
    decimal CurrentPrice);
