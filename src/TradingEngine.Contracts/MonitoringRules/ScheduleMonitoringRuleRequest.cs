namespace TradingEngine.Contracts.MonitoringRules;

public sealed record ScheduleMonitoringRuleRequest(
    string? EffectiveFrom,
    string? EffectiveTo);
