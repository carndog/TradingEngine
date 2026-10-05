namespace TradingEngine.Contracts.MonitoringRules;

public sealed record ScheduleMonitoringRuleRequest(
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo);
