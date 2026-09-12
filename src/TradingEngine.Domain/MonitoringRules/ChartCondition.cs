namespace TradingEngine.Domain.MonitoringRules;

public sealed record ChartCondition(ChartConditionType Type, ChartAnalysisIdentifier ActionId);
