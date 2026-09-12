namespace TradingEngine.Domain.MonitoringRules;

public enum ChartAnalysisDefinitionRule
{
    MissingZones,
    InvalidConditionOrder,
    DuplicateZoneId,
    PriceExceedsScale,
    OverlappingZones
}
