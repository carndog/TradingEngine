namespace TradingEngine.Contracts.MonitoringRules;

public sealed record MonitoringRuleDraftsResponse(
    Guid MonitoringRuleId,
    Guid WatchedInstrumentId,
    string ConcurrencyToken,
    IReadOnlyList<MonitoringRuleRevisionDto> Drafts);
