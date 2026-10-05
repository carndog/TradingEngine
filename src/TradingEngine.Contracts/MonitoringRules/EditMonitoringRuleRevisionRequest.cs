namespace TradingEngine.Contracts.MonitoringRules;

public sealed record EditMonitoringRuleRevisionRequest(
    string? ChangeReason,
    MonitoringRuleDefinitionDto? Definition,
    DateTimeOffset? EffectiveFrom);
