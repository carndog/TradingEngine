namespace TradingEngine.Contracts.MonitoringRules;

public sealed record MonitoringRuleDraftRequest(
    string? ChangeReason,
    RevisionPeriodDto? ProposedPeriod,
    MonitoringRuleDefinitionDto? Definition);
