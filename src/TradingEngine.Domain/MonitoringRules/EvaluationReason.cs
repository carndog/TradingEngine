namespace TradingEngine.Domain.MonitoringRules;

public enum EvaluationReason
{
    ConditionsTriggered,
    FirstObservation,
    NoConfiguredZones,
    NoConditionTriggered
}
