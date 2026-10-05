namespace TradingEngine.Contracts.MonitoringRules;

public sealed record MonitoringRuleRevisionResponse(
    string ConcurrencyToken,
    MonitoringRuleRevisionDto Revision);
