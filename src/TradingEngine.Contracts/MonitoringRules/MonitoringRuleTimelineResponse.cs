namespace TradingEngine.Contracts.MonitoringRules;

public sealed record MonitoringRuleTimelineResponse(
    Guid MonitoringRuleId,
    Guid WatchedInstrumentId,
    string ConcurrencyToken,
    DateTimeOffset? CoverageOrigin,
    IReadOnlyList<MonitoringRuleRevisionDto> Revisions);
